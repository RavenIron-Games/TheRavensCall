// POST /push — receives one bundle per game-server push (scope §2, §4 route
// table). Reached only via the /push -> function push redirect in
// netlify.toml; nothing here trusts the request path.
//
// Processing order (deliberately fixed, since the scope's route table lists
// status codes without saying which check runs first when more than one
// would apply):
//   1. 413 — Content-Length precheck, then the actual read-byte count.
//   2. JSON parse failure -> 422.
//   3. Field-shape validation (server_id, mod_version, pushed_at,
//      heartbeat_seconds if present, read_token_sha256 format, envelope
//      fields) -> 422.
//   4. Bearer presence, registry lookup by server_id, bearer hash vs the
//      registered write-token hash `w` -> 401 (identical body for all three:
//      missing bearer, wrong bearer, unregistered id).
//   5. read_token_sha256 vs the registered read-token hash `r` -> 409.
//   6. Per-id rate limit (10 s) -> 429 with Retry-After.
//   7. Store + 200.
// This order matches every pairing the scope's §8 test plan exercises
// (e.g. a malformed id -> 422 before the 401 an unregistered-but-well-formed
// id would get; a missing read_token_sha256 -> 422 before the 409 a
// mismatched one would get).

import type { Context } from "@netlify/functions";
import {
  MAX_BODY_BYTES,
  PUSH_MIN_INTERVAL_MS,
  DEFAULT_HEARTBEAT_SECONDS,
  isValidId,
  isValidModVersion,
  isValidIsoInstant,
  isValidHex64,
  isValidHeartbeatSeconds,
  hexEquals,
  sha256Hex,
  jsonResponse,
  trcStore,
  blobKey,
  loadRegistry,
  type ServerMeta,
} from "./_lib/trc.js";

// Bodies shared across the failure modes that must be indistinguishable
// (§4: "the same body whether that id is registered or not", so nobody can
// enumerate servers by probing /push).
const UNAUTHORIZED_BODY = { error: "unauthorized" } as const;

type Envelope = string | null | undefined;

interface PushBody {
  v?: unknown;
  server_id?: unknown;
  mod_version?: unknown;
  pushed_at?: unknown;
  push_interval_seconds?: unknown;
  heartbeat_seconds?: unknown;
  read_token_sha256?: unknown;
  state?: unknown;
  activity?: unknown;
  census?: unknown;
}

function isEnvelopeField(v: unknown): v is Envelope {
  return v === null || v === undefined || typeof v === "string";
}

// Thrown by readBodyLimited once the streamed byte count crosses
// MAX_BODY_BYTES, so a chunked request (no Content-Length, skipping the
// precheck below) is still bounded before it is ever buffered whole.
class PayloadTooLargeError extends Error {}

async function readBodyLimited(req: Request, maxBytes: number): Promise<string> {
  if (!req.body) return "";
  const reader = req.body.getReader();
  const chunks: Uint8Array[] = [];
  let total = 0;
  for (;;) {
    const { done, value } = await reader.read();
    if (done) break;
    if (value) {
      total += value.byteLength;
      if (total > maxBytes) {
        await reader.cancel().catch(() => {});
        throw new PayloadTooLargeError();
      }
      chunks.push(value);
    }
  }
  return Buffer.concat(chunks.map((c) => Buffer.from(c))).toString("utf8");
}

export default async (req: Request, _context: Context): Promise<Response> => {
  if (req.method !== "POST") {
    return jsonResponse(404, { error: "not found" });
  }

  // --- 413: Content-Length precheck (cheap fast path; a chunked request has
  // no Content-Length and falls through to the streamed byte count below) --
  const contentLengthHeader = req.headers.get("content-length");
  if (contentLengthHeader) {
    const declaredLength = Number(contentLengthHeader);
    if (Number.isFinite(declaredLength) && declaredLength > MAX_BODY_BYTES) {
      return jsonResponse(413, { error: "payload too large" });
    }
  }

  // --- 413: actual byte count, enforced while streaming so a chunked body
  // is refused as soon as it crosses the limit rather than buffered whole
  // first (§6: the only bound left otherwise is one the client chooses to
  // send) ------------------------------------------------------------------
  let bodyText: string;
  try {
    bodyText = await readBodyLimited(req, MAX_BODY_BYTES);
  } catch (err) {
    if (err instanceof PayloadTooLargeError) {
      return jsonResponse(413, { error: "payload too large" });
    }
    return jsonResponse(422, { error: "validation failed", field: "body" });
  }

  // --- 422: JSON parse ---------------------------------------------------
  let body: PushBody;
  try {
    body = JSON.parse(bodyText) as PushBody;
  } catch {
    return jsonResponse(422, { error: "validation failed", field: "body" });
  }
  if (typeof body !== "object" || body === null || Array.isArray(body)) {
    return jsonResponse(422, { error: "validation failed", field: "body" });
  }

  // --- 422: field-shape validation ---------------------------------------
  // Type-checked before isValidId ever runs: isValidId takes a string, so a
  // non-string body.server_id (e.g. ["storm10"]) must fail here, not reach
  // the registry lookup as some coerced value that could miss by identity
  // and come back 401 instead of the 422 a bad field is owed (§4).
  if (typeof body.server_id !== "string" || !isValidId(body.server_id)) {
    return jsonResponse(422, { error: "validation failed", field: "server_id" });
  }
  const serverId = body.server_id;

  if (!isValidModVersion(body.mod_version)) {
    return jsonResponse(422, { error: "validation failed", field: "mod_version" });
  }
  const modVersion = body.mod_version as string;

  if (!isValidIsoInstant(body.pushed_at)) {
    return jsonResponse(422, { error: "validation failed", field: "pushed_at" });
  }
  const pushedAt = body.pushed_at as string;

  // heartbeat_seconds: missing -> 600 (default); present but invalid -> 422.
  let heartbeatSeconds: number;
  if (body.heartbeat_seconds === undefined || body.heartbeat_seconds === null) {
    heartbeatSeconds = DEFAULT_HEARTBEAT_SECONDS;
  } else if (isValidHeartbeatSeconds(body.heartbeat_seconds)) {
    heartbeatSeconds = body.heartbeat_seconds;
  } else {
    return jsonResponse(422, { error: "validation failed", field: "heartbeat_seconds" });
  }

  // read_token_sha256: required, 64 hex — missing is 422, same as any other
  // failed field (§4 Validation: "anything from a pushed body ... read_token_sha256
  // as 64 hex characters ... and anything that fails is 422").
  if (!isValidHex64(body.read_token_sha256)) {
    return jsonResponse(422, { error: "validation failed", field: "read_token_sha256" });
  }
  const readTokenSha256 = (body.read_token_sha256 as string).toLowerCase();

  if (!isEnvelopeField(body.state) || !isEnvelopeField(body.activity) || !isEnvelopeField(body.census)) {
    return jsonResponse(422, { error: "validation failed", field: "envelope" });
  }
  const stateEnvelope = (body.state ?? null) as string | null;
  const activityEnvelope = (body.activity ?? null) as string | null;
  const censusEnvelope = (body.census ?? null) as string | null;

  // --- 401: bearer presence, registry lookup, write-token hash ----------
  const authHeader = req.headers.get("authorization") ?? "";
  const bearerMatch = /^Bearer\s+(.+)$/.exec(authHeader);
  if (!bearerMatch || bearerMatch[1].length === 0) {
    return jsonResponse(401, UNAUTHORIZED_BODY);
  }
  const bearerToken = bearerMatch[1];

  const registry = loadRegistry();
  const entry = registry.get(serverId);
  if (!entry) {
    return jsonResponse(401, UNAUTHORIZED_BODY);
  }

  const bearerSha256 = sha256Hex(bearerToken);
  if (!hexEquals(bearerSha256, entry.w)) {
    return jsonResponse(401, UNAUTHORIZED_BODY);
  }

  // --- 409: read_token_sha256 vs the registered read-token hash ---------
  if (!hexEquals(readTokenSha256, entry.r)) {
    return jsonResponse(409, { error: "read token mismatch" });
  }

  // --- 429: per-id rate limit (10 s), strong read+write on <id>/meta -----
  const store = trcStore();
  const metaKey = blobKey(serverId, "meta");
  let prevMeta: ServerMeta | null;
  try {
    prevMeta = (await store.get(metaKey, { type: "json" })) as ServerMeta | null;
  } catch {
    // A thrown read is a storage fault, not "no prior push": treated as a
    // first push it would skip the rate-limit gate below entirely and let
    // the sizes fallback record 0 for envelopes this push did not carry
    // (§4/§6). Answer as the gate itself would on a too-soon retry; only a
    // genuine null return (no throw) means "first push, no prior meta".
    return jsonResponse(429, { error: "too many requests" }, { "Retry-After": "10" });
  }

  const nowMs = Date.now();
  if (prevMeta && typeof prevMeta.last_accepted_at_ms === "number") {
    const elapsed = nowMs - prevMeta.last_accepted_at_ms;
    if (elapsed < PUSH_MIN_INTERVAL_MS) {
      const retryAfterSeconds = Math.max(1, Math.ceil((PUSH_MIN_INTERVAL_MS - elapsed) / 1000));
      return jsonResponse(429, { error: "too many requests" }, { "Retry-After": String(retryAfterSeconds) });
    }
  }

  // --- Store: non-null envelopes only, bytes untouched -------------------
  const nowIso = new Date(nowMs).toISOString();
  const stored: string[] = [];
  const sizes = {
    state: prevMeta?.sizes?.state ?? 0,
    activity: prevMeta?.sizes?.activity ?? 0,
    census: prevMeta?.sizes?.census ?? 0,
  };

  async function storeEnvelope(kind: "state" | "activity" | "census", value: string | null) {
    if (value === null) return;
    const bytes = Buffer.from(value, "utf8");
    const sha256 = sha256Hex(bytes);
    await store.set(blobKey(serverId, kind), value, {
      metadata: { sha256, received_at: nowIso, size: bytes.length },
    });
    sizes[kind] = bytes.length;
    stored.push(kind);
  }

  // Unlike every read above, these writes had no try/catch: a storage 5xx
  // here would escape as an unhandled rejection and whatever the Functions
  // runtime emits for that becomes the response body — never a
  // receiver-authored one, and never one that echoes the request (§6).
  try {
    await storeEnvelope("state", stateEnvelope);
    await storeEnvelope("activity", activityEnvelope);
    await storeEnvelope("census", censusEnvelope);

    const meta: ServerMeta = {
      received_at: nowIso,
      pushed_at: pushedAt,
      mod_version: modVersion,
      heartbeat_seconds: heartbeatSeconds,
      sizes,
      last_accepted_at_ms: nowMs,
    };
    // Kept last, as before: a failed envelope write must never reach here
    // and advance the rate-limit gate.
    await store.set(metaKey, JSON.stringify(meta));
  } catch {
    console.warn(`[push] storage write failed: id=${serverId}, status=500`);
    return jsonResponse(500, { error: "storage unavailable" });
  }

  return jsonResponse(200, { ok: true, stored });
};

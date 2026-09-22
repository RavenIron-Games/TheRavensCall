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

export default async (req: Request, _context: Context): Promise<Response> => {
  if (req.method !== "POST") {
    return jsonResponse(404, { error: "not found" });
  }

  // --- 413: Content-Length precheck -----------------------------------
  const contentLengthHeader = req.headers.get("content-length");
  if (contentLengthHeader) {
    const declaredLength = Number(contentLengthHeader);
    if (Number.isFinite(declaredLength) && declaredLength > MAX_BODY_BYTES) {
      return jsonResponse(413, { error: "payload too large" });
    }
  }

  let bodyText: string;
  try {
    bodyText = await req.text();
  } catch {
    return jsonResponse(422, { error: "validation failed", field: "body" });
  }

  // --- 413: actual byte count ------------------------------------------
  const bodyBytes = Buffer.byteLength(bodyText, "utf8");
  if (bodyBytes > MAX_BODY_BYTES) {
    return jsonResponse(413, { error: "payload too large" });
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
  if (!isValidId(String(body.server_id ?? ""))) {
    return jsonResponse(422, { error: "validation failed", field: "server_id" });
  }
  const serverId = body.server_id as string;

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
  let prevMeta: ServerMeta | null = null;
  try {
    prevMeta = (await store.get(metaKey, { type: "json" })) as ServerMeta | null;
  } catch {
    prevMeta = null;
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
  await store.set(metaKey, JSON.stringify(meta));

  return jsonResponse(200, { ok: true, stored });
};

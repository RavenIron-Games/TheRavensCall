// Shared implementation for push.mts and api.mts: registry parsing, hashing,
// header helpers and validators. A single source of truth so the two route
// handlers can never drift on what counts as a valid id, hash or header.
//
// This file lives under a leading-underscore directory (_lib) inside
// netlify/functions/ so the Netlify CLI and the deployed Functions runtime
// do not try to treat it as a routable function of its own — only files
// directly under netlify/functions/ (not nested in an underscore-prefixed
// directory) are discovered as functions.

import { createHash, timingSafeEqual } from "node:crypto";
import { getStore } from "@netlify/blobs";

// ---------------------------------------------------------------------------
// Constants (scope §2, §4)
// ---------------------------------------------------------------------------

export const STORE_NAME = "trc";
export const MAX_BODY_BYTES = 2 * 1024 * 1024; // 2 MB (§4 Storage/Validation, route table 413)
export const PUSH_MIN_INTERVAL_MS = 10 * 1000; // 10 s per server (route table 429)
export const DEFAULT_HEARTBEAT_SECONDS = 600; // §2: missing/invalid heartbeat_seconds -> 600
export const MIN_HEARTBEAT_SECONDS = 60;
export const MAX_HEARTBEAT_SECONDS = 86400;

export const ID_PATTERN = /^[a-z0-9-]{1,32}$/;
export const HEX64_PATTERN = /^[0-9a-f]{64}$/;
export const MOD_VERSION_PATTERN = /^[0-9A-Za-z.+-]{1,32}$/;

// ---------------------------------------------------------------------------
// Empty envelopes — mirrors Companion.cs EmptyStateJson / EmptyActivityJson
// (lines ~40-61) and WorldCensus.cs BuildEmptyEnvelope (~481-540), each with
// "no_data":true appended, for the "before the first push" 200 response
// (§4 route table).
// ---------------------------------------------------------------------------

export const EMPTY_STATE_JSON =
  '{"generated_at":"","world_name":"","day":0,"online_count":0,"raid_active":false,"raid_type":"","players":{},"no_data":true}';

export const EMPTY_ACTIVITY_JSON =
  '{"generated_at":"","season":{"active":false,"name":"","started_at":null,"standings_since":null,"last_ended":null,"last_ended_at":null,"standings":[]},"events":[],"no_data":true}';

// WorldCensus.EmptyEnvelope() cannot be reproduced exactly (it depends on
// the game server's own CensusIntervalMinutes, which no push carries), so
// per scope §4 the receiver sends enabled:true, interval_minutes:0, empty
// groups/lists, plus "no_data":true. Group keys/order match WorldCensus.cs
// GroupOrder (portals, beds, wards, ships, carts, chests, stations).
export const EMPTY_CENSUS_JSON =
  '{"generated_at":"","enabled":true,"interval_minutes":0,"scanned_objects":0,"duration_ms":0,' +
  '"groups":{"portals":{"total":0,"player_built":0},"beds":{"total":0,"player_built":0},' +
  '"wards":{"total":0,"player_built":0},"ships":{"total":0,"player_built":0},' +
  '"carts":{"total":0,"player_built":0},"chests":{"total":0,"player_built":0},' +
  '"stations":{"total":0,"player_built":0}},"builders":[],"unknown_builders":0,' +
  '"portals":[],"beds":[],"wards":[],"no_data":true}';

export const EMPTY_GAMEDATA_JSON = '{"recipes":[],"items":[],"buildables":[]}';

// ---------------------------------------------------------------------------
// Registry (§4 Registry)
// ---------------------------------------------------------------------------

export interface RegistryEntry {
  w: string; // sha256 of the write token (hex, lowercase)
  r: string; // sha256 of the read token (hex, lowercase)
}

export type Registry = Map<string, RegistryEntry>;

let cachedRegistry: Registry | null = null;
let cachedRawEnv: string | undefined;

/**
 * Parses TRC_SERVERS once per invocation (module-level cache keyed on the
 * raw env string, so a changed env value — which only happens on redeploy,
 * per §4 — never serves a stale parse within one warm instance either).
 *
 * - ids held to ID_PATTERN; an entry whose id fails, or whose w/r is not
 *   64 lowercase hex, is ignored with one console line (no token/hash
 *   value ever logged — id only).
 * - a registry in which two ids share the same w is rejected at load: the
 *   whole registry parses to empty in that case, with one console line,
 *   because a shared write token means either id could write the other's
 *   blobs.
 */
export function loadRegistry(): Registry {
  const raw = process.env.TRC_SERVERS;
  if (cachedRegistry !== null && cachedRawEnv === raw) {
    return cachedRegistry;
  }
  cachedRawEnv = raw;
  cachedRegistry = parseRegistry(raw);
  return cachedRegistry;
}

function parseRegistry(raw: string | undefined): Registry {
  const registry: Registry = new Map();
  if (!raw) {
    console.warn("[trc] TRC_SERVERS is not set; no servers registered");
    return registry;
  }

  let parsed: unknown;
  try {
    parsed = JSON.parse(raw);
  } catch {
    console.warn("[trc] TRC_SERVERS is not valid JSON; no servers registered");
    return registry;
  }

  if (typeof parsed !== "object" || parsed === null || Array.isArray(parsed)) {
    console.warn("[trc] TRC_SERVERS is not a JSON object; no servers registered");
    return registry;
  }

  const seenWriteHashes = new Set<string>();
  const duplicateWriteHashes = new Set<string>();

  for (const [id, value] of Object.entries(parsed as Record<string, unknown>)) {
    if (!ID_PATTERN.test(id)) {
      console.warn(`[trc] TRC_SERVERS entry ignored: invalid id`);
      continue;
    }
    if (typeof value !== "object" || value === null) {
      console.warn(`[trc] TRC_SERVERS entry ignored for id (bad shape): ${id}`);
      continue;
    }
    const w = (value as Record<string, unknown>).w;
    const r = (value as Record<string, unknown>).r;
    if (typeof w !== "string" || !HEX64_PATTERN.test(w)) {
      console.warn(`[trc] TRC_SERVERS entry ignored: bad w for id: ${id}`);
      continue;
    }
    if (typeof r !== "string" || !HEX64_PATTERN.test(r)) {
      console.warn(`[trc] TRC_SERVERS entry ignored: bad r for id: ${id}`);
      continue;
    }

    if (seenWriteHashes.has(w)) {
      duplicateWriteHashes.add(w);
    }
    seenWriteHashes.add(w);

    registry.set(id, { w, r });
  }

  if (duplicateWriteHashes.size > 0) {
    console.warn(
      "[trc] TRC_SERVERS rejected at load: two or more ids share the same write token hash"
    );
    return new Map();
  }

  return registry;
}

// ---------------------------------------------------------------------------
// Hashing / constant-time compare
// ---------------------------------------------------------------------------

export function sha256Hex(input: string | Buffer): string {
  return createHash("sha256").update(input).digest("hex");
}

/**
 * Constant-time comparison of two hex strings of the SAME expected length
 * (64 for sha256). Uses crypto.timingSafeEqual on equal-length buffers;
 * falls back to `false` immediately (no compare) when lengths differ,
 * which is safe because both operands here are always validated as
 * exactly 64 lowercase hex characters before this is called — a length
 * mismatch means the request-supplied value already failed shape
 * validation, not a secret-dependent branch.
 */
export function hexEquals(a: string, b: string): boolean {
  if (a.length !== b.length) return false;
  const bufA = Buffer.from(a, "utf8");
  const bufB = Buffer.from(b, "utf8");
  if (bufA.length !== bufB.length) return false;
  return timingSafeEqual(bufA, bufB);
}

// ---------------------------------------------------------------------------
// Validators (§4 Validation)
// ---------------------------------------------------------------------------

export function isValidId(id: string): boolean {
  return ID_PATTERN.test(id);
}

export function isValidModVersion(v: unknown): v is string {
  return typeof v === "string" && MOD_VERSION_PATTERN.test(v);
}

export function isValidHex64(v: unknown): v is string {
  return typeof v === "string" && HEX64_PATTERN.test(v);
}

/** ISO-8601 instant: parseable by Date and round-trips to a finite time. */
export function isValidIsoInstant(v: unknown): v is string {
  if (typeof v !== "string" || v.length === 0) return false;
  const ms = Date.parse(v);
  return Number.isFinite(ms);
}

/**
 * heartbeat_seconds per §2/§4: a MISSING value defaults to 600; a PRESENT
 * but invalid one (wrong type, non-integer, or outside 60..86400) fails
 * validation (422 at the route level). This function only classifies a
 * present value; callers handle "missing -> 600" themselves.
 */
export function isValidHeartbeatSeconds(v: unknown): v is number {
  return (
    typeof v === "number" &&
    Number.isInteger(v) &&
    v >= MIN_HEARTBEAT_SECONDS &&
    v <= MAX_HEARTBEAT_SECONDS
  );
}

// ---------------------------------------------------------------------------
// Headers (§4, §6)
// ---------------------------------------------------------------------------

export const CORS_HEADERS: Record<string, string> = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Methods": "GET, OPTIONS",
  "Access-Control-Allow-Headers": "X-Api-Token, If-None-Match",
  "Access-Control-Expose-Headers": "ETag",
};

export const SECURITY_HEADERS: Record<string, string> = {
  "X-Content-Type-Options": "nosniff",
  "Referrer-Policy": "no-referrer",
};

/** Base headers every function response carries: nosniff + no-referrer. */
export function baseHeaders(extra?: Record<string, string>): Record<string, string> {
  return { ...SECURITY_HEADERS, ...(extra ?? {}) };
}

/** Headers for the token-gated read routes: base + CORS. */
export function readHeaders(extra?: Record<string, string>): Record<string, string> {
  return { ...SECURITY_HEADERS, ...CORS_HEADERS, ...(extra ?? {}) };
}

/**
 * Builds a JSON Response with the given status and headers, always via
 * JSON.stringify (never string concatenation), and always carrying the
 * nosniff/no-referrer headers.
 */
export function jsonResponse(
  status: number,
  body: unknown,
  extraHeaders?: Record<string, string>
): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: {
      "Content-Type": "application/json",
      ...baseHeaders(extraHeaders),
    },
  });
}

// ---------------------------------------------------------------------------
// Blobs store accessor
// ---------------------------------------------------------------------------

export function trcStore() {
  return getStore({ name: STORE_NAME, consistency: "strong" });
}

export function blobKey(id: string, kind: "state" | "activity" | "census" | "meta"): string {
  return `${id}/${kind}`;
}

// ---------------------------------------------------------------------------
// Meta shape stored at <id>/meta
// ---------------------------------------------------------------------------

export interface ServerMeta {
  received_at: string; // receiver clock, ISO-8601 — set on every accepted push
  pushed_at: string; // echoed only, never trusted for age
  mod_version: string;
  heartbeat_seconds: number;
  sizes: { state: number; activity: number; census: number };
  last_accepted_at_ms: number; // receiver clock, epoch ms — the 429 rate-limit gate
}

export function emptyEnvelopeFor(kind: "state" | "activity" | "census"): string {
  if (kind === "state") return EMPTY_STATE_JSON;
  if (kind === "activity") return EMPTY_ACTIVITY_JSON;
  return EMPTY_CENSUS_JSON;
}

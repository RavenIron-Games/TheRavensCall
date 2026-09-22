// GET /s/<id>/api/state|activity|census|health|gamedata, DELETE /s/<id>/api,
// OPTIONS /s/<id>/api/* — scope §4 route table, verbatim.
//
// Reached only via the /s/:id/api/* -> function api redirect in
// netlify.toml (force = true). The id is taken from the URL path and
// validated against the registry charset BEFORE it is used for anything;
// only a validated, registered id ever becomes a Blobs key.

import type { Context } from "@netlify/functions";
import {
  isValidId,
  isValidHex64,
  hexEquals,
  sha256Hex,
  jsonResponse,
  readHeaders,
  trcStore,
  blobKey,
  loadRegistry,
  emptyEnvelopeFor,
  EMPTY_GAMEDATA_JSON,
  type ServerMeta,
} from "./_lib/trc.js";

// Identical body for a wrong token, a missing token and an unregistered id,
// so nobody can enumerate servers (§4).
const TOKEN_REQUIRED_BODY = { error: "token required" } as const;
const UNAUTHORIZED_BODY = { error: "unauthorized" } as const;

function stripEtagQuotes(raw: string): string {
  let v = raw.trim();
  if (v.startsWith("W/")) v = v.slice(2);
  if (v.startsWith('"') && v.endsWith('"') && v.length >= 2) v = v.slice(1, -1);
  return v;
}

function notFound(): Response {
  return jsonResponse(404, { error: "not found" });
}

export default async (req: Request, _context: Context): Promise<Response> => {
  const url = new URL(req.url);
  // Expected shape: /s/<id>/api[/<route>]
  const segments = url.pathname.split("/").filter((s) => s.length > 0);
  if (segments.length < 3 || segments[0] !== "s" || segments[2] !== "api") {
    return notFound();
  }
  const rawId = segments[1];
  const route = segments.slice(3).join("/"); // "" for DELETE /s/<id>/api

  if (req.method === "OPTIONS") {
    return new Response(null, {
      status: 204,
      headers: readHeaders(),
    });
  }

  if (req.method === "GET") {
    // A ?token= query parameter is always refused, before anything else,
    // and never logged (§4).
    if (url.searchParams.has("token")) {
      return jsonResponse(400, { error: "use the X-Api-Token header" });
    }

    if (route !== "state" && route !== "activity" && route !== "census" && route !== "health" && route !== "gamedata") {
      return notFound();
    }

    // The id must be validated against the charset before it is used for
    // anything, including a registry lookup — an invalid shape can never
    // match a registered id anyway, so it falls into the same bucket as
    // "unregistered id" below.
    const registry = loadRegistry();
    const entry = isValidId(rawId) ? registry.get(rawId) : undefined;

    const tokenHeader = req.headers.get("x-api-token");
    if (!tokenHeader) {
      return jsonResponse(401, TOKEN_REQUIRED_BODY);
    }
    if (!entry) {
      return jsonResponse(401, TOKEN_REQUIRED_BODY);
    }
    const tokenSha256 = sha256Hex(tokenHeader);
    if (!hexEquals(tokenSha256, entry.r)) {
      return jsonResponse(401, TOKEN_REQUIRED_BODY);
    }

    const id = rawId;
    const store = trcStore();

    if (route === "gamedata") {
      return jsonResponse(200, JSON.parse(EMPTY_GAMEDATA_JSON), readHeaders());
    }

    if (route === "health") {
      let meta: ServerMeta | null = null;
      try {
        meta = (await store.get(blobKey(id, "meta"), { type: "json" })) as ServerMeta | null;
      } catch {
        meta = null;
      }
      if (!meta) {
        return jsonResponse(200, { status: "ok", hosted: true }, readHeaders());
      }
      const ageSeconds = Math.max(0, Math.floor((Date.now() - Date.parse(meta.received_at)) / 1000));
      const staleAfterSeconds = 3 * meta.heartbeat_seconds;
      return jsonResponse(
        200,
        {
          status: "ok",
          hosted: true,
          pushed_at: meta.pushed_at,
          age_seconds: ageSeconds,
          stale_after_seconds: staleAfterSeconds,
        },
        readHeaders()
      );
    }

    // state | activity | census
    const kind = route as "state" | "activity" | "census";
    let result: Awaited<ReturnType<typeof store.getWithMetadata>> | null;
    try {
      result = await store.getWithMetadata(blobKey(id, kind), { type: "text" });
    } catch {
      result = null;
    }

    if (!result) {
      // Before the first push carrying this envelope: 200, empty envelope
      // of the right shape, no_data:true, no ETag, no-store (§4).
      return new Response(emptyEnvelopeFor(kind), {
        status: 200,
        headers: {
          "Content-Type": "application/json",
          "Cache-Control": "no-store",
          ...readHeaders(),
        },
      });
    }

    const sha256 = String(result.metadata?.sha256 ?? "");
    const etag = `"${sha256}"`;

    const ifNoneMatch = req.headers.get("if-none-match");
    if (ifNoneMatch && sha256 && stripEtagQuotes(ifNoneMatch) === sha256) {
      return new Response(null, {
        status: 304,
        headers: {
          ETag: etag,
          "Cache-Control": "private, no-cache",
          Vary: "X-Api-Token",
          ...readHeaders(),
        },
      });
    }

    return new Response(result.data, {
      status: 200,
      headers: {
        "Content-Type": "application/json",
        ETag: etag,
        "Cache-Control": "private, no-cache",
        Vary: "X-Api-Token",
        ...readHeaders(),
      },
    });
  }

  if (req.method === "DELETE") {
    if (route !== "") {
      // DELETE only exists at exactly /s/<id>/api (§4: the /s/:id/api/*
      // rule is the only /s/ rule reaching a function; anything deeper
      // than that under DELETE has no defined route).
      return notFound();
    }

    const registry = loadRegistry();
    const entry = isValidId(rawId) ? registry.get(rawId) : undefined;

    const authHeader = req.headers.get("authorization") ?? "";
    const bearerMatch = /^Bearer\s+(.+)$/.exec(authHeader);
    if (!bearerMatch || bearerMatch[1].length === 0) {
      return jsonResponse(401, UNAUTHORIZED_BODY);
    }
    if (!entry) {
      return jsonResponse(401, UNAUTHORIZED_BODY);
    }
    const bearerSha256 = sha256Hex(bearerMatch[1]);
    if (!hexEquals(bearerSha256, entry.w)) {
      return jsonResponse(401, UNAUTHORIZED_BODY);
    }

    const id = rawId;
    const store = trcStore();
    await Promise.all([
      store.delete(blobKey(id, "state")),
      store.delete(blobKey(id, "activity")),
      store.delete(blobKey(id, "census")),
      store.delete(blobKey(id, "meta")),
    ]);

    // No CORS headers here: DELETE is not in Access-Control-Allow-Methods
    // (§4 lists GET, OPTIONS only) — unpublishing is an admin action via
    // curl/script, not a browser fetch.
    return jsonResponse(200, { deleted: true });
  }

  return notFound();
};

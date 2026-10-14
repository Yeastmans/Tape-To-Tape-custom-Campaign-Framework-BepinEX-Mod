// Cloudflare Worker: proxies T2T Campaign Creator ↔ Dropbox.
// Secrets live in Worker environment variables (Cloudflare dashboard), never
// in the shipped exe. Deploy: copy this whole file into a new Worker, add the
// three env vars, save.
//
// Endpoints:
//   POST /upload        (multipart/form-data: file, meta)      — upload a campaign zip
//   GET  /list                                                 — list campaigns
//   GET  /download?path=/Foo.zip                               — stream a campaign zip
//
// Env vars (Workers > Settings > Variables, all encrypted):
//   DROPBOX_APP_KEY
//   DROPBOX_APP_SECRET
//   DROPBOX_REFRESH_TOKEN

const UA = "T2T-CampaignCreator-Worker/1.0";
// Per-upload sanity cap — rejects zips larger than this to prevent abuse.
const MAX_UPLOAD_BYTES = 20 * 1024 * 1024; // 20 MB
const MAX_CAMPAIGN_BYTES = 512 * 1024 * 1024;
const CHUNK_BYTES = 8 * 1024 * 1024;

async function boundedBody(req, maximum) {
  if (Number(req.headers.get("Content-Length")) > maximum) throw new Error("Upload request is too large.");
  const reader = req.body?.getReader();
  const chunks = []; let size = 0;
  if (reader) while (true) {
    const { done, value } = await reader.read();
    if (done) break;
    size += value.length;
    if (size > maximum) { await reader.cancel(); throw new Error("Upload request is too large."); }
    chunks.push(value);
  }
  const result = new Uint8Array(size); let offset = 0;
  for (const chunk of chunks) { result.set(chunk, offset); offset += chunk.length; }
  return result;
}

async function ticketKey(env) {
  return crypto.subtle.importKey("raw", new TextEncoder().encode("campaign-upload-v1:" + env.DROPBOX_APP_SECRET),
    { name: "HMAC", hash: "SHA-256" }, false, ["sign", "verify"]);
}
function base64(bytes) { return btoa(String.fromCharCode(...bytes)); }
function unbase64(text) { return Uint8Array.from(atob(text), c => c.charCodeAt(0)); }
async function signTicket(value, env) {
  const data = new TextEncoder().encode(JSON.stringify(value));
  const signature = new Uint8Array(await crypto.subtle.sign("HMAC", await ticketKey(env), data));
  return base64(data) + "." + base64(signature);
}
async function readTicket(req, env) {
  const raw = req.headers.get("X-Campaign-Upload") || "";
  if (raw.length > 4096) throw new Error("Invalid upload session.");
  const parts = raw.split(".");
  if (parts.length !== 2 || !await crypto.subtle.verify("HMAC", await ticketKey(env), unbase64(parts[1]), unbase64(parts[0])))
    throw new Error("Invalid upload session.");
  const value = JSON.parse(new TextDecoder().decode(unbase64(parts[0])));
  if (value.expires < Date.now() || !Number.isSafeInteger(value.size) || value.size <= 0 || value.size > MAX_CAMPAIGN_BYTES)
    throw new Error("Upload session expired. Please retry the upload.");
  return value;
}
async function sessionRequest(env, route, argument, body = new Uint8Array()) {
  const token = await getAccessToken(env);
  return fetch("https://content.dropboxapi.com/2/files/upload_session/" + route, {
    method: "POST", headers: { Authorization: `Bearer ${token}`, "Content-Type": "application/octet-stream",
      "Dropbox-API-Arg": JSON.stringify(argument), "User-Agent": UA }, body
  });
}
async function startUpload(req, env) {
  const input = JSON.parse(new TextDecoder().decode(await boundedBody(req, 16384)));
  if (!Number.isSafeInteger(input.size) || input.size <= 0 || input.size > MAX_CAMPAIGN_BYTES)
    return json({ error: "Campaigns must be smaller than 512 MB." }, 413);
  const response = await sessionRequest(env, "start", { close: false });
  if (!response.ok) return json({ error: "The community storage could not start the upload." }, 502);
  const session = await response.json();
  const ticket = await signTicket({ session: session.session_id, size: input.size,
    path: "/" + safeName(input.name), expires: Date.now() + 60 * 60 * 1000 }, env);
  return json({ ok: true, ticket, chunkBytes: CHUNK_BYTES });
}
async function appendUpload(req, url, env) {
  const ticket = await readTicket(req, env);
  const offset = Number(url.searchParams.get("offset"));
  const body = await boundedBody(req, CHUNK_BYTES);
  if (!Number.isSafeInteger(offset) || offset < 0 || body.length === 0 || offset + body.length > ticket.size)
    return json({ error: "Invalid campaign upload offset or chunk size." }, 400);
  const next = offset + body.length;
  const response = await sessionRequest(env, "append_v2", { cursor: { session_id: ticket.session, offset }, close: false }, body);
  if (!response.ok) {
    const failure = await response.json();
    // A retried request can arrive after its first response was lost.
    if (response.status !== 409 || failure.error?.[".tag"] !== "incorrect_offset" || failure.error?.correct_offset !== next)
      return json({ error: "The community storage could not accept this upload chunk. Please retry." }, 502);
  }
  return json({ ok: true, offset: next });
}
async function finishUpload(req, env) {
  const ticket = await readTicket(req, env);
  const response = await sessionRequest(env, "finish", {
    cursor: { session_id: ticket.session, offset: ticket.size },
    commit: { path: ticket.path, mode: "add", autorename: false, mute: true, strict_conflict: true }
  });
  if (!response.ok) return json({ error: "The community storage could not finish the upload. Refresh the community list before retrying." }, 502);
  const file = await response.json();
  return json({ ok: true, path: file.path_display || file.path_lower, size: file.size });
}

// -- Dropbox OAuth: exchange refresh token for short-lived access token.
async function getAccessToken(env) {
  const body = new URLSearchParams({
    grant_type: "refresh_token",
    refresh_token: env.DROPBOX_REFRESH_TOKEN,
    client_id: env.DROPBOX_APP_KEY,
    client_secret: env.DROPBOX_APP_SECRET,
  });
  const r = await fetch("https://api.dropboxapi.com/oauth2/token", {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded", "User-Agent": UA },
    body,
  });
  if (!r.ok) throw new Error(`token refresh failed: ${r.status} ${await r.text()}`);
  const j = await r.json();
  return j.access_token;
}

// -- Sanitize filename: strip dangerous chars, cap length, add timestamp.
function safeName(raw) {
  const cleaned = (raw || "campaign").replace(/[^\w\- .]/g, "_").slice(0, 120);
  const ts = new Date().toISOString().replace(/[:.]/g, "-");
  return `${ts} - ${cleaned}.zip`;
}

// -- POST /upload: multipart with `file` (zip) + optional `meta` (JSON string).
async function handleUpload(req, env) {
  const ct = req.headers.get("Content-Type") || "";
  if (!ct.startsWith("multipart/form-data"))
    return json({ error: "expected multipart/form-data" }, 400);
  const form = await req.formData();
  const file = form.get("file");
  if (!file || typeof file === "string")
    return json({ error: "missing 'file' field" }, 400);
  const blob = await file.arrayBuffer();
  if (blob.byteLength > MAX_UPLOAD_BYTES)
    return json({ error: `file too large (max ${MAX_UPLOAD_BYTES} bytes)` }, 413);
  const name = safeName(form.get("name") || file.name || "campaign");
  const meta = form.get("meta") || "{}";

  const token = await getAccessToken(env);
  const dbxApiArg = {
    path: "/" + name,
    mode: "add",
    autorename: true,
    mute: true,
    // Stash submitter metadata in Dropbox's property template — not used here,
    // but kept as the commit description so it shows in the web UI.
    strict_conflict: false,
  };
  const r = await fetch("https://content.dropboxapi.com/2/files/upload", {
    method: "POST",
    headers: {
      Authorization: `Bearer ${token}`,
      "Content-Type": "application/octet-stream",
      "Dropbox-API-Arg": JSON.stringify(dbxApiArg),
      "User-Agent": UA,
    },
    body: blob,
  });
  if (!r.ok) return json({ error: `dropbox upload failed: ${r.status} ${await r.text()}` }, 502);
  const j = await r.json();

  // Attach metadata as a file-property (optional) — for now we just return OK.
  return json({ ok: true, path: j.path_display || j.path_lower, size: j.size, meta_echo: meta });
}

// -- GET /list: returns [{ name, path, size, modified }, ...]
async function handleList(env) {
  const token = await getAccessToken(env);
  const r = await fetch("https://api.dropboxapi.com/2/files/list_folder", {
    method: "POST",
    headers: {
      Authorization: `Bearer ${token}`,
      "Content-Type": "application/json",
      "User-Agent": UA,
    },
    body: JSON.stringify({ path: "", recursive: false, include_media_info: false }),
  });
  if (!r.ok) return json({ error: `dropbox list failed: ${r.status} ${await r.text()}` }, 502);
  const j = await r.json();
  const items = (j.entries || [])
    .filter((e) => e[".tag"] === "file" && /\.zip$/i.test(e.name))
    .map((e) => ({
      name: e.name,
      path: e.path_display || e.path_lower,
      size: e.size,
      modified: e.client_modified || e.server_modified,
    }))
    .sort((a, b) => b.modified.localeCompare(a.modified));
  return json({ ok: true, count: items.length, items });
}

// -- GET /download?path=/Foo.zip : stream the zip bytes back.
async function handleDownload(url, env) {
  const path = url.searchParams.get("path");
  if (!path) return json({ error: "missing ?path=" }, 400);
  const token = await getAccessToken(env);
  const r = await fetch("https://content.dropboxapi.com/2/files/download", {
    method: "POST",
    headers: {
      Authorization: `Bearer ${token}`,
      "Dropbox-API-Arg": JSON.stringify({ path }),
      "User-Agent": UA,
    },
  });
  if (!r.ok) return json({ error: `dropbox download failed: ${r.status} ${await r.text()}` }, 502);
  const contentLength = r.headers.get("Content-Length") || "";
  return new Response(r.body, {
    status: 200,
    headers: {
      "Content-Type": "application/zip",
      "Content-Length": contentLength,
      "Access-Control-Allow-Origin": "*",
    },
  });
}

function json(obj, status = 200) {
  return new Response(JSON.stringify(obj), {
    status,
    headers: { "Content-Type": "application/json", "Access-Control-Allow-Origin": "*" },
  });
}

export default {
  async fetch(req, env) {
    const url = new URL(req.url);
    // Permissive CORS so the tkinter app + browsers can both call.
    if (req.method === "OPTIONS") {
      return new Response(null, {
        headers: {
          "Access-Control-Allow-Origin": "*",
          "Access-Control-Allow-Methods": "GET, POST, OPTIONS",
          "Access-Control-Allow-Headers": "Content-Type, Authorization, X-Campaign-Upload",
        },
      });
    }
    try {
      if (url.pathname === "/upload/start" && req.method === "POST") return await startUpload(req, env);
      if (url.pathname === "/upload/chunk" && req.method === "POST") return await appendUpload(req, url, env);
      if (url.pathname === "/upload/finish" && req.method === "POST") return await finishUpload(req, env);
      if (url.pathname === "/upload" && req.method === "POST") return await handleUpload(req, env);
      if (url.pathname === "/list") return await handleList(env);
      if (url.pathname === "/download") return await handleDownload(url, env);
      if (url.pathname === "/" || url.pathname === "/health") {
        return json({ ok: true, service: "t2t-campaigns", chunkedUpload: true, maxCampaignBytes: MAX_CAMPAIGN_BYTES,
          chunkBytes: CHUNK_BYTES, endpoints: ["/upload", "/upload/start", "/upload/chunk", "/upload/finish", "/list", "/download?path=..."] });
      }
      return json({ error: "not found" }, 404);
    } catch (e) {
      return json({ error: String(e && e.message || e) }, 500);
    }
  },
};

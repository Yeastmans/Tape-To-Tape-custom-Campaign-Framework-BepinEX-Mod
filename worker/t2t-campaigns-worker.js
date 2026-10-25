// Cloudflare Worker: proxies T2T Campaign Creator ↔ Dropbox.
// Secrets live in Worker environment variables (Cloudflare dashboard), never
// in the shipped exe. Deploy: copy this whole file into a new Worker, add the
// environment variables, save. See COMMUNITY-MANAGEMENT.md for management setup.
//
// Endpoints:
//   POST /upload        (multipart/form-data: file, meta)      — upload a campaign zip
//   GET  /list                                                 — list campaigns
//   GET  /download?path=/Foo.zip                               — stream a campaign zip
//   POST /manage/prepare, /manage/status, /manage/delete         — private code workflow
//   POST /manage/claim                                         — administrator legacy assignment
//
// Env vars (Workers > Settings > Variables, all encrypted):
//   DROPBOX_APP_KEY
//   DROPBOX_APP_SECRET
//   DROPBOX_REFRESH_TOKEN
//   CAMPAIGN_MANAGEMENT_SECRET  (stable random secret, at least 32 characters)
//   CAMPAIGN_ADMIN_SECRET       (optional; enables manual legacy assignment)
// Optional plain-text variable: CAMPAIGN_FILE_ID_ALIASES, a JSON object mapping
// migrated Dropbox file IDs to their original public Studio IDs. Keep forever.

const UA = "T2T-CampaignCreator-Worker/1.0";
// Per-upload sanity cap — rejects zips larger than this to prevent abuse.
const MAX_UPLOAD_BYTES = 20 * 1024 * 1024; // 20 MB
const MAX_CAMPAIGN_BYTES = 512 * 1024 * 1024;
// Keep each request below the free Worker's CPU budget. Studio reads this size
// when starting a session, so this also repairs already-released clients.
const CHUNK_BYTES = 1024 * 1024;
const MAX_CHUNK_BYTES = 8 * 1024 * 1024; // Existing sessions may still send 8 MiB.
const MAX_ICON_CHARACTERS = 350000;
const MAX_METADATA_BYTES = 384 * 1024;
// JSON callers may encode each character as \uXXXX; the stored sidecar is compact.
const MAX_PRESENTATION_REQUEST_BYTES = 6 * (MAX_ICON_CHARACTERS + 240 + 160 + 60 + 2000) + 4096;

class HttpError extends Error {
  constructor(status, message) { super(message); this.status = status; }
}
function fail(status, message) { throw new HttpError(status, message); }
function isLegacyPath(path) {
  return typeof path === "string" && /^\/[^/\\\x00-\x1f]+\.zip$/i.test(path);
}
function isManagedPath(path) {
  return typeof path === "string" && /^\/managed\/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12} - [^/\\\x00-\x1f]+\.zip$/i.test(path);
}
function isPublicPath(path) { return isLegacyPath(path) || isManagedPath(path); }
function displayName(file) {
  let name = file.name || "";
  if (isManagedPath(file.path_display || file.path_lower)) name = name.replace(/^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12} - /i, "");
  return name.replace(/^\d{4}-\d{2}-\d{2}T\d{2}-\d{2}-\d{2}-\d{3}Z - /, "").replace(/\.zip$/i, "").trim() || "Campaign";
}
function publicFileId(file, env) {
  if (!env?.CAMPAIGN_FILE_ID_ALIASES) return file.id;
  let aliases;
  try { aliases = JSON.parse(env.CAMPAIGN_FILE_ID_ALIASES); } catch { fail(503, "Campaign migration settings are invalid."); }
  if (!aliases || typeof aliases !== "object" || Array.isArray(aliases)) fail(503, "Campaign migration settings are invalid.");
  if (!Object.hasOwn(aliases, file.id)) return file.id;
  const id = aliases[file.id];
  if (typeof id !== "string" || !/^id:[^\s]{1,250}$/.test(id)) fail(503, "Campaign migration settings are invalid.");
  return id;
}
function publicFile(file, presentation, env) {
  const name = presentation?.displayName || displayName(file);
  return { id: publicFileId(file, env), name, campaignName: presentation?.campaignName || name,
    author: presentation?.author || "", description: presentation?.description || "", icon: presentation?.icon || "",
    path: file.path_display || file.path_lower, rev: file.rev,
    size: file.size, modified: file.server_modified || file.client_modified, contentHash: file.content_hash || "" };
}
function presentationInput(value) {
  if (!value || typeof value !== "object" || Array.isArray(value)) fail(400, "Campaign metadata must be an object.");
  const text = (key, maximum) => {
    if (value[key] === undefined) return "";
    if (typeof value[key] !== "string" || value[key].length > maximum || /[\x00-\x08\x0b\x0c\x0e-\x1f]/.test(value[key]))
      fail(400, "Invalid campaign " + key + ".");
    return value[key].trim();
  };
  const result = { displayName: text("displayName", 240), campaignName: text("campaignName", 160),
    author: text("author", 60), description: text("description", 2000), icon: text("icon", MAX_ICON_CHARACTERS) };
  if (result.icon) {
    const match = /^data:image\/png;base64,([A-Za-z0-9+/]+={0,2})$/.exec(result.icon);
    let bytes;
    try { if (!match || match[1].length % 4 !== 0) throw new Error(); bytes = unbase64(match[1]); }
    catch { fail(400, "Campaign icon must be a small PNG image."); }
    if (bytes.length < 33 || ![137,80,78,71,13,10,26,10].every((v, i) => bytes[i] === v) ||
      ![0,0,0,13,73,72,68,82].every((v, i) => bytes[i + 8] === v)) fail(400, "Campaign icon must be a PNG image.");
    const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength), width = view.getUint32(16), height = view.getUint32(20);
    if (width < 1 || height < 1 || width > 512 || height > 512) fail(400, "Campaign icon must be no larger than 512 by 512 pixels.");
  }
  return result;
}
async function inputJson(req, maximum = 16384, allowEmpty = false) {
  try {
    const bytes = await boundedBody(req, maximum);
    if (allowEmpty && bytes.length === 0) return {};
    const value = JSON.parse(new TextDecoder().decode(bytes));
    if (value && typeof value === "object" && !Array.isArray(value)) return value;
  } catch { /* Public errors must not include submitted credentials. */ }
  fail(400, "Expected a small JSON object.");
}
async function presentationPath(id) {
  const hash = new Uint8Array(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(id)));
  return "/.campaign-metadata/" + Array.from(hash, x => x.toString(16).padStart(2, "0")).join("") + ".json";
}
async function readPresentation(file, env, token) {
  token ??= await getAccessToken(env);
  const path = await presentationPath(file.id);
  const response = await fetch("https://content.dropboxapi.com/2/files/download", { method: "POST",
    headers: { Authorization: `Bearer ${token}`, "Dropbox-API-Arg": JSON.stringify({ path }), "User-Agent": UA } });
  if (!response.ok) {
    let error; try { error = JSON.parse(new TextDecoder().decode(await boundedBody(response, 16384))).error; } catch { /* generic failure below */ }
    if (response.status === 409 && error?.[".tag"] === "path" && error.path?.[".tag"] === "not_found") return null;
    fail(502, "Campaign details could not be read.");
  }
  const stored = JSON.parse(new TextDecoder().decode(await boundedBody(response, MAX_METADATA_BYTES)));
  const details = JSON.parse(response.headers.get("Dropbox-API-Result") || "{}");
  if (stored.v !== 1 || stored.id !== file.id || typeof details.rev !== "string" || !details.rev)
    fail(502, "Campaign details could not be verified.");
  return { path, revision: details.rev, presentation: presentationInput(stored.metadata) };
}
async function presentFile(file, env, token) {
  try { return publicFile(file, (await readPresentation(file, env, token))?.presentation, env); }
  catch { return publicFile(file, undefined, env); } // A missing/corrupt/unavailable preview never hides a public ZIP.
}
async function savePresentation(file, presentation, env) {
  const token = await getAccessToken(env), prior = await readPresentation(file, env, token);
  // Read the sidecar revision BEFORE checking the ZIP revision. Conditional write
  // then prevents an older finish racing a newer finish from replacing its details.
  const current = await metadata(env, file.id);
  if (!current || current.id !== file.id || current.rev !== file.rev) fail(409, "Campaign changed while saving its details.");
  const path = prior?.path || await presentationPath(file.id);
  const response = await fetch("https://content.dropboxapi.com/2/files/upload", { method: "POST",
    headers: { Authorization: `Bearer ${token}`, "Content-Type": "application/octet-stream", "User-Agent": UA,
      "Dropbox-API-Arg": JSON.stringify({ path, mode: prior ? { ".tag": "update", update: prior.revision } : "add",
        autorename: false, mute: true, strict_conflict: true }) },
    body: JSON.stringify({ v: 1, id: file.id, zipRevision: file.rev, metadata: presentation }) });
  if (!response.ok) fail(502, "Campaign details could not be saved.");
}
function requireRevision(value) {
  if (typeof value !== "string" || !/^[0-9a-f]{9,}$/i.test(value)) fail(400, "The current campaign revision is required. Refresh the community list.");
  return value;
}
async function managementKey(env) {
  if (typeof env.CAMPAIGN_MANAGEMENT_SECRET !== "string" || env.CAMPAIGN_MANAGEMENT_SECRET.length < 32)
    fail(503, "Campaign management is not configured on the server yet.");
  return crypto.subtle.importKey("raw", new TextEncoder().encode("campaign-management-v1:" + env.CAMPAIGN_MANAGEMENT_SECRET),
    { name: "HMAC", hash: "SHA-256" }, false, ["sign", "verify"]);
}
async function signManagement(value, env) {
  const bytes = new TextEncoder().encode(JSON.stringify(value));
  return "cm1." + base64(bytes) + "." + base64(new Uint8Array(await crypto.subtle.sign("HMAC", await managementKey(env), bytes)));
}
async function readManagement(req, env) {
  const raw = req.headers.get("X-Campaign-Manage") || "";
  const key = await managementKey(env);
  try {
    if (raw.length > 4096) throw new Error();
    const parts = raw.split(".");
    if (parts.length !== 3 || parts[0] !== "cm1" || !await crypto.subtle.verify("HMAC", key, unbase64(parts[2]), unbase64(parts[1]))) throw new Error();
    const value = JSON.parse(new TextDecoder().decode(unbase64(parts[1])));
    if (value.v !== 1 || !((value.type === "managed" && isManagedPath(value.path)) ||
      (value.type === "legacy" && typeof value.id === "string" && /^id:[^\s]{1,250}$/.test(value.id)))) throw new Error();
    return { code: raw, ...value };
  } catch { fail(403, "A valid private management code for this campaign is required."); }
}
async function dropboxJson(env, route, argument) {
  const token = await getAccessToken(env);
  try {
    return await fetch("https://api.dropboxapi.com/2/files/" + route, { method: "POST",
      headers: { Authorization: `Bearer ${token}`, "Content-Type": "application/json", "User-Agent": UA }, body: JSON.stringify(argument) });
  } catch { fail(502, "Community storage is temporarily unavailable. Please retry."); }
}
async function metadata(env, path) {
  const response = await dropboxJson(env, "get_metadata", { path });
  if (response.ok) return response.json();
  let error; try { error = (await response.json()).error; } catch { /* upstream error */ }
  if (response.status === 409 && error?.[".tag"] === "path" && error.path?.[".tag"] === "not_found") return null;
  fail(502, "Community storage could not find the campaign. Please retry.");
}
async function managedFile(capability, env) {
  const file = await metadata(env, capability.type === "legacy" ? capability.id : capability.path);
  if (!file) return null;
  const path = file.path_display || file.path_lower;
  if (file[".tag"] !== "file" || !isPublicPath(path) ||
      (capability.type === "legacy" ? file.id !== capability.id : path.toLowerCase() !== capability.path.toLowerCase()))
    fail(403, "This code does not authorize the selected campaign.");
  return file;
}
async function prepareManagement(req, env) {
  const input = await inputJson(req);
  if (input.name !== undefined && typeof input.name !== "string") fail(400, "Campaign name must be text.");
  const path = "/managed/" + crypto.randomUUID() + " - " + safeName(input.name);
  const managementCode = await signManagement({ v: 1, type: "managed", path }, env);
  return json({ ok: true, managementCode, path, name: path.slice(path.lastIndexOf("/") + 1) });
}
async function managementStatus(req, env) {
  const capability = await readManagement(req, env), file = await managedFile(capability, env);
  return json(file ? { ok: true, exists: true, path: file.path_display || file.path_lower, item: await presentFile(file, env) } :
    { ok: true, exists: false, path: capability.path || "" });
}
async function deleteManaged(req, env) {
  const capability = await readManagement(req, env), input = await inputJson(req), rev = requireRevision(input.rev);
  const file = await managedFile(capability, env);
  if (!file) fail(404, "This campaign is no longer posted.");
  if (file.rev !== rev) fail(409, "This campaign changed. Refresh it before deleting.");
  const id = publicFileId(file, env);
  const response = await dropboxJson(env, "delete_v2", { path: file.id, parent_rev: rev });
  if (!response.ok) fail(response.status === 409 ? 409 : 502, "The campaign could not be deleted. Refresh it before retrying.");
  return json({ ok: true, deleted: true, id, path: file.path_display || file.path_lower });
}
async function claimLegacy(req, env) {
  const provided = req.headers.get("X-Campaign-Admin") || "";
  if (!env.CAMPAIGN_ADMIN_SECRET || !provided) fail(403, "Administrator authorization is required.");
  // Compare fixed-length digests without revealing how much of the secret matched.
  const digest = value => crypto.subtle.digest("SHA-256", new TextEncoder().encode(value));
  const a = new Uint8Array(await digest(provided)), b = new Uint8Array(await digest(env.CAMPAIGN_ADMIN_SECRET));
  let mismatch = 0; for (let i = 0; i < a.length; i++) mismatch |= a[i] ^ b[i];
  if (mismatch) fail(403, "Administrator authorization is required.");
  const input = await inputJson(req);
  if (!isLegacyPath(input.path)) fail(400, "Select one legacy campaign ZIP in the public root.");
  const file = await metadata(env, input.path);
  if (!file) fail(404, "This campaign is no longer posted.");
  if (file[".tag"] !== "file" || !isLegacyPath(file.path_display || file.path_lower)) fail(400, "Select one legacy campaign ZIP.");
  const managementCode = await signManagement({ v: 1, type: "legacy", id: file.id }, env);
  return json({ ok: true, managementCode, ...await presentFile(file, env) });
}

async function boundedBody(req, maximum) {
  const declared = req.headers.get("Content-Length");
  if (declared !== null) {
    const size = Number(declared);
    if (!Number.isSafeInteger(size) || size < 0) fail(400, "Invalid upload request length.");
    if (size > maximum) fail(413, "Upload request is too large.");
    // The HTTP runtime enforces Content-Length. Read the bounded body natively
    // instead of retaining every network fragment and copying it in JavaScript.
    const bytes = new Uint8Array(await req.arrayBuffer());
    if (bytes.length !== size) fail(400, "Upload request length does not match its body.");
    return bytes;
  }
  const reader = req.body?.getReader();
  const chunks = []; let size = 0;
  if (reader) while (true) {
    const { done, value } = await reader.read();
    if (done) break;
    size += value.length;
    if (size > maximum) { await reader.cancel(); fail(413, "Upload request is too large."); }
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
  try {
    if (raw.length > 4096) throw new Error();
    const parts = raw.split(".");
    if (parts.length !== 2 || !await crypto.subtle.verify("HMAC", await ticketKey(env), unbase64(parts[1]), unbase64(parts[0]))) throw new Error();
    const value = JSON.parse(new TextDecoder().decode(unbase64(parts[0])));
    if (!Number.isSafeInteger(value.expires) || value.expires < Date.now() ||
      typeof value.session !== "string" || !value.session || !Number.isSafeInteger(value.size) || value.size <= 0 || value.size > MAX_CAMPAIGN_BYTES) throw new Error();
    return value;
  } catch { fail(403, "Upload session is invalid or expired. Please retry the upload."); }
}
async function sessionRequest(env, route, argument, body = new Uint8Array()) {
  const token = await getAccessToken(env);
  try { return await fetch("https://content.dropboxapi.com/2/files/upload_session/" + route, {
    method: "POST", headers: { Authorization: `Bearer ${token}`, "Content-Type": "application/octet-stream",
      "Dropbox-API-Arg": JSON.stringify(argument), "User-Agent": UA }, body
  }); } catch { fail(502, "The upload response was lost. Check the saved campaign status before retrying."); }
}
async function ownerFingerprint(capability) {
  return base64(new Uint8Array(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(capability.code))));
}
async function startUpload(req, env) {
  const input = await inputJson(req);
  if (!Number.isSafeInteger(input.size) || input.size <= 0 || input.size > MAX_CAMPAIGN_BYTES)
    return json({ error: "Campaigns must be smaller than 512 MB." }, 413);
  let ownership = {}, path;
  if (req.headers.has("X-Campaign-Manage") || input.operation !== undefined) {
    const capability = await readManagement(req, env);
    if (input.operation !== "create" && input.operation !== "replace") fail(400, "Choose create or replace for this campaign upload.");
    const rev = input.operation === "replace" ? requireRevision(input.rev) : undefined;
    const file = await managedFile(capability, env);
    if (input.operation === "create") {
      if (capability.type !== "managed") fail(403, "Legacy codes can only manage their existing campaign.");
      if (file) fail(409, "This campaign is already posted. Refresh its status and choose Replace ZIP.");
      path = capability.path;
    } else {
      if (!file) fail(404, "This campaign is no longer posted.");
      if (file.rev !== rev) fail(409, "This campaign changed. Refresh it before replacing the ZIP.");
      path = file.id;
    }
    ownership = { managed: true, owner: await ownerFingerprint(capability), operation: input.operation, rev,
      targetId: file?.id, publicPath: file?.path_display || file?.path_lower || capability.path };
  } else {
    if (input.name !== undefined && typeof input.name !== "string") fail(400, "Campaign name must be text.");
    path = "/" + safeName(input.name);
  }
  const response = await sessionRequest(env, "start", { close: false });
  if (!response.ok) return json({ error: "The community storage could not start the upload." }, 502);
  const session = await response.json();
  const ticket = await signTicket({ session: session.session_id, size: input.size,
    path, ...ownership, expires: Date.now() + 60 * 60 * 1000 }, env);
  return json({ ok: true, ticket, chunkBytes: CHUNK_BYTES, path: ownership.publicPath || path });
}
async function appendUpload(req, url, env) {
  const ticket = await readTicket(req, env);
  const offset = Number(url.searchParams.get("offset"));
  const body = await boundedBody(req, MAX_CHUNK_BYTES);
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
  const input = await inputJson(req, MAX_PRESENTATION_REQUEST_BYTES, true);
  const presentation = input.metadata === undefined ? undefined : presentationInput(input.metadata);
  if (presentation !== undefined && !ticket.managed) fail(403, "Campaign details require verified campaign management access.");
  let mode = "add";
  if (ticket.managed) {
    const capability = await readManagement(req, env);
    if (await ownerFingerprint(capability) !== ticket.owner) fail(403, "This code does not authorize the selected upload.");
    const file = await managedFile(capability, env);
    if (ticket.operation === "replace") {
      if (!file) fail(404, "This campaign is no longer posted.");
      if (file.id !== ticket.targetId || file.rev !== ticket.rev) fail(409, "This campaign changed during upload. Refresh it before retrying.");
      mode = { ".tag": "update", update: ticket.rev };
    } else if (ticket.operation !== "create" || capability.type !== "managed" || ticket.path !== capability.path) {
      fail(403, "This code does not authorize the selected upload.");
    } else if (file) fail(409, "This campaign is already posted. Check its saved status before retrying.");
  }
  const response = await sessionRequest(env, "finish", {
    cursor: { session_id: ticket.session, offset: ticket.size },
    commit: { path: ticket.path, mode, autorename: false, mute: true, strict_conflict: true }
  });
  if (!response.ok) return json({ error: "The community storage could not finish the upload. Refresh the community list before retrying." }, response.status === 409 ? 409 : 502);
  const file = await response.json();
  if (presentation !== undefined) {
    try {
      await savePresentation(file, presentation, env);
      return json({ ok: true, ...publicFile(file, presentation, env), metadataSaved: true });
    } catch {
      // ZIP and JSON are separate atomic Dropbox commits. Never turn a successful
      // ZIP commit into a generic upload error that encourages a duplicate upload.
      return json({ ok: true, ...await presentFile(file, env), metadataSaved: false,
        metadataWarning: "The campaign ZIP was uploaded, but its title, description and icon could not be confirmed saved. Refresh the community list before trying again." });
    }
  }
  return json({ ok: true, ...await presentFile(file, env) });
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
  if (!r.ok) fail(502, "Community storage authorization is temporarily unavailable.");
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
  if (!r.ok) return json({ error: "Community storage could not accept the campaign upload." }, 502);
  const j = await r.json();

  // Attach metadata as a file-property (optional) — for now we just return OK.
  return json({ ok: true, path: j.path_display || j.path_lower, size: j.size, meta_echo: meta });
}

// -- GET /list: returns [{ name, path, size, modified }, ...]
async function handleList(env) {
  const files = []; let cursor;
  do {
    const response = await dropboxJson(env, cursor ? "list_folder/continue" : "list_folder", cursor ? { cursor } :
      { path: "", recursive: true, include_media_info: false });
    if (!response.ok) fail(502, "Community storage could not list campaigns.");
    const page = await response.json();
    for (const file of page.entries || [])
      if (file[".tag"] === "file" && isPublicPath(file.path_display || file.path_lower)) files.push(file);
    cursor = page.has_more ? page.cursor : null;
  } while (cursor);
  let token; try { token = await getAccessToken(env); } catch { /* previews are optional */ }
  const items = new Array(files.length); let next = 0;
  await Promise.all(Array.from({ length: Math.min(4, files.length) }, async () => {
    while (next < files.length) {
      const index = next++;
      items[index] = token ? await presentFile(files[index], env, token) : publicFile(files[index], undefined, env);
    }
  }));
  items.sort((a, b) => (b.modified || "").localeCompare(a.modified || ""));
  return json({ ok: true, count: items.length, items });
}

function downloadFilename(name) {
  let stem = (name || "Campaign").normalize("NFC")
    .replace(/[<>:"/\\|?*\x00-\x1f\x7f\u202a-\u202e\u2066-\u2069\ud800-\udfff]/gu, "_")
    .trim().replace(/[. ]+$/g, "").replace(/(?:\.zip|\.t2tcampaign)+$/i, "").replace(/[. ]+$/g, "");
  if (/^(con|prn|aux|nul|com[1-9¹²³]|lpt[1-9¹²³])(?:\.|$)/i.test(stem)) stem = "Campaign-" + stem;
  // Bound UTF-8 bytes as well as characters, without splitting a Unicode scalar.
  const encoder = new TextEncoder(); let readable = "", bytes = 0;
  for (const c of stem) {
    bytes += encoder.encode(c).length;
    if (bytes > 120) break;
    readable += c;
  }
  return (readable.replace(/[. ]+$/g, "") || "Campaign") + ".zip";
}
function downloadDisposition(name) {
  const filename = downloadFilename(name);
  const ascii = filename.replace(/[^\x20-\x7e]|%/g, "_");
  const encoded = encodeURIComponent(filename).replace(/['()*]/g, c => "%" + c.charCodeAt(0).toString(16).toUpperCase());
  return `attachment; filename="${ascii}"; filename*=UTF-8''${encoded}`;
}

// -- GET /download?path=/Foo.zip : stream the zip bytes back.
async function handleDownload(url, env) {
  const path = url.searchParams.get("path");
  if (!isPublicPath(path)) return json({ error: "Select a public campaign ZIP path." }, 400);
  const token = await getAccessToken(env);
  const r = await fetch("https://content.dropboxapi.com/2/files/download", {
    method: "POST",
    headers: {
      Authorization: `Bearer ${token}`,
      "Dropbox-API-Arg": JSON.stringify({ path }),
      "User-Agent": UA,
    },
  });
  if (!r.ok) return json({ error: "Community storage could not download this campaign." }, 502);
  let name = displayName({ name: path.split("/").at(-1), path_display: path });
  try {
    // Dropbox supplies the file identity with the bytes; optional presentation
    // lookup must never turn an otherwise usable ZIP download into an error.
    const file = JSON.parse(r.headers.get("Dropbox-API-Result") || "{}");
    if (typeof file.id === "string" && typeof file.name === "string") {
      name = displayName(file);
      const presentation = (await readPresentation(file, env, token))?.presentation;
      name = presentation?.campaignName || presentation?.displayName || name;
    }
  } catch { /* Retain the readable storage-name fallback. */ }
  const contentLength = r.headers.get("Content-Length") || "";
  return new Response(r.body, {
    status: 200,
    headers: {
      "Content-Type": "application/zip",
      "Content-Disposition": downloadDisposition(name),
      "Content-Length": contentLength,
      "Access-Control-Allow-Origin": "*",
      "Access-Control-Expose-Headers": "Content-Disposition",
    },
  });
}

function json(obj, status = 200) {
  return new Response(JSON.stringify(obj), {
    status,
    headers: { "Content-Type": "application/json", "Access-Control-Allow-Origin": "*", "Cache-Control": "no-store" },
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
          "Access-Control-Allow-Headers": "Content-Type, Authorization, X-Campaign-Upload, X-Campaign-Manage, X-Campaign-Admin",
        },
      });
    }
    try {
      if (url.pathname === "/manage/prepare" && req.method === "POST") return await prepareManagement(req, env);
      if (url.pathname === "/manage/status" && req.method === "POST") return await managementStatus(req, env);
      if (url.pathname === "/manage/delete" && req.method === "POST") return await deleteManaged(req, env);
      if (url.pathname === "/manage/claim" && req.method === "POST") return await claimLegacy(req, env);
      if (url.pathname === "/upload/start" && req.method === "POST") return await startUpload(req, env);
      if (url.pathname === "/upload/chunk" && req.method === "POST") return await appendUpload(req, url, env);
      if (url.pathname === "/upload/finish" && req.method === "POST") return await finishUpload(req, env);
      if (url.pathname === "/upload" && req.method === "POST") return await handleUpload(req, env);
      if (url.pathname === "/list") return await handleList(env);
      if (url.pathname === "/download") return await handleDownload(url, env);
      if (url.pathname === "/" || url.pathname === "/health") {
        return json({ ok: true, service: "t2t-campaigns", chunkedUpload: true, maxCampaignBytes: MAX_CAMPAIGN_BYTES,
          campaignManagement: typeof env.CAMPAIGN_MANAGEMENT_SECRET === "string" && env.CAMPAIGN_MANAGEMENT_SECRET.length >= 32,
          campaignMetadata: true,
          chunkBytes: CHUNK_BYTES, endpoints: ["/upload", "/upload/start", "/upload/chunk", "/upload/finish", "/list", "/download?path=...",
            "/manage/prepare", "/manage/status", "/manage/delete", "/manage/claim"] });
      }
      return json({ error: "not found" }, 404);
    } catch (e) {
      return json({ error: e instanceof HttpError ? e.message : "The request could not be completed. Please retry." }, e instanceof HttpError ? e.status : 500);
    }
  },
};

import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { webcrypto } from 'node:crypto';
import { deflateSync } from 'node:zlib';

globalThis.crypto ??= webcrypto;
const source = await readFile(new URL('../../worker/t2t-campaigns-worker.js', import.meta.url), 'utf8');
const worker = (await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'))).default;
const env = { DROPBOX_APP_KEY: 'test-key', DROPBOX_APP_SECRET: 'test-dropbox-secret', DROPBOX_REFRESH_TOKEN: 'test-refresh',
  CAMPAIGN_MANAGEMENT_SECRET: 'test-only-stable-management-secret-123456789', CAMPAIGN_ADMIN_SECRET: 'test-only-admin-secret' };
const reply = (body, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });

// Only Dropbox HTTP is substituted. Requests execute the actual Worker, crypto,
// body limits and routing; this fake models revision conflicts and stored bytes.
function storage() {
  const files = new Map(), sessions = new Map(); let sequence = 0, revision = 100000000;
  let failFinish = false, loseFinish = false, failMetadataWrite = false, failMetadataRead = false, loseMetadataWrite = false;
  const find = path => [...files.values()].find(f => f.id === path || f.path_lower === path.toLowerCase());
  function put(path, bytes, previous) {
    const file = { '.tag': 'file', id: previous?.id ?? 'id:file' + ++sequence, rev: (++revision).toString(16).padStart(9, '0'),
      name: path.split('/').at(-1), path_display: path, path_lower: path.toLowerCase(), size: bytes.length,
      client_modified: '2026-10-17T12:00:00Z', server_modified: '2026-10-17T12:00:00Z', content_hash: 'ab'.repeat(32), bytes };
    files.set(file.path_lower, file); return file;
  }
  const clean = f => { const { bytes, ...metadata } = f; return metadata; };
  const missing = () => reply({ error: { '.tag': 'path', path: { '.tag': 'not_found' } } }, 409);
  globalThis.fetch = async (url, init = {}) => {
    url = String(url);
    assert.ok(url.startsWith('https://api.dropboxapi.com/') || url.startsWith('https://content.dropboxapi.com/'), 'No external requests allowed');
    if (url.endsWith('/oauth2/token')) return reply({ access_token: 'test-access-token' });
    const headers = new Headers(init.headers);
    const arg = JSON.parse(headers.get('Dropbox-API-Arg') ?? init.body ?? '{}');
    const bytes = () => typeof init.body === 'string' ? new TextEncoder().encode(init.body) : new Uint8Array(init.body ?? []);
    if (url.endsWith('/files/get_metadata')) { const f = find(arg.path); return f ? reply(clean(f)) : missing(); }
    if (url.endsWith('/files/list_folder') || url.endsWith('/files/list_folder/continue')) {
      const all = [...files.values()].map(clean); const offset = arg.cursor ? Number(arg.cursor) : 0;
      const entries = all.slice(offset, offset + 2); const next = offset + entries.length;
      return reply({ entries, cursor: String(next), has_more: next < all.length });
    }
    if (url.endsWith('/upload_session/start')) { const session_id = 'session-' + ++sequence; sessions.set(session_id, []); return reply({ session_id }); }
    if (url.endsWith('/upload_session/append_v2')) {
      const data = sessions.get(arg.cursor.session_id);
      if (!data) return reply({ error: { '.tag': 'not_found' } }, 409);
      if (data.length !== arg.cursor.offset) return reply({ error: { '.tag': 'incorrect_offset', correct_offset: data.length } }, 409);
      data.push(...bytes()); return reply(null);
    }
    if (url.endsWith('/upload_session/finish')) {
      if (failFinish) { failFinish = false; return reply({ error: 'upstream_failure' }, 503); }
      const data = sessions.get(arg.cursor.session_id);
      if (!data) return reply({ error: { '.tag': 'lookup_failed', lookup_failed: { '.tag': 'not_found' } } }, 409);
      if (data.length !== arg.cursor.offset) return reply({ error: { '.tag': 'lookup_failed', lookup_failed: { '.tag': 'incorrect_offset', correct_offset: data.length } } }, 409);
      const previous = find(arg.commit.path);
      if ((arg.commit.mode === 'add' && previous) || (arg.commit.mode?.['.tag'] === 'update' && (!previous || previous.rev !== arg.commit.mode.update)))
        return reply({ error: { '.tag': 'path', path: { '.tag': 'conflict' } } }, 409);
      assert.equal(arg.commit.autorename, false); assert.equal(arg.commit.strict_conflict, true);
      const file = put(previous?.path_display ?? arg.commit.path, Uint8Array.from(data), previous);
      sessions.delete(arg.cursor.session_id);
      if (loseFinish) { loseFinish = false; throw new Error('Simulated lost response after Dropbox committed'); }
      return reply(clean(file));
    }
    if (url.endsWith('/files/delete_v2')) {
      const file = find(arg.path); if (!file) return missing();
      if (arg.parent_rev !== file.rev) return reply({ error: { '.tag': 'path_write', path_write: { '.tag': 'conflict' } } }, 409);
      files.delete(file.path_lower); return reply({ metadata: clean(file) });
    }
    if (url.endsWith('/files/download')) {
      if (failMetadataRead && arg.path.startsWith('/.campaign-metadata/')) return reply({ error: 'metadata_read_failure' }, 503);
      const file = find(arg.path); return file ? new Response(file.bytes, { headers: { 'Dropbox-API-Result': JSON.stringify(clean(file)) } }) : missing();
    }
    if (url.endsWith('/files/upload')) {
      if (arg.path.startsWith('/.campaign-metadata/')) {
        if (failMetadataWrite) { failMetadataWrite = false; return reply({ error: 'metadata_write_failure' }, 503); }
        const previous = find(arg.path);
        if ((arg.mode === 'add' && previous) || (arg.mode?.['.tag'] === 'update' && (!previous || previous.rev !== arg.mode.update)))
          return reply({ error: 'metadata_conflict' }, 409);
        assert.equal(arg.autorename, false); assert.equal(arg.strict_conflict, true);
        const file = put(arg.path, bytes(), previous);
        if (loseMetadataWrite) { loseMetadataWrite = false; throw new Error('Simulated lost response after metadata commit'); }
        return reply(clean(file));
      }
      return reply(clean(put(arg.path, bytes())));
    }
    throw new Error('Unimplemented fake endpoint: ' + url);
  };
  return { files, put, find, failFinish() { failFinish = true; }, loseFinish() { loseFinish = true; },
    failMetadataWrite() { failMetadataWrite = true; }, failMetadataRead() { failMetadataRead = true; },
    loseMetadataWrite() { loseMetadataWrite = true; } };
}
async function call(path, { body, code, ticket, admin, method = 'POST', environment = env } = {}) {
  const headers = {};
  if (code) headers['X-Campaign-Manage'] = code;
  if (ticket) headers['X-Campaign-Upload'] = ticket;
  if (admin) headers['X-Campaign-Admin'] = admin;
  if (body !== undefined && !(body instanceof Uint8Array)) { headers['Content-Type'] = 'application/json'; body = JSON.stringify(body); }
  return worker.fetch(new Request('https://worker.invalid' + path, { method, headers, body }), environment);
}
async function ok(path, options) { const r = await call(path, options); const value = await r.json(); assert.equal(r.status, 200, JSON.stringify(value)); return value; }
async function prepare(name = 'My campaign') { return ok('/manage/prepare', { body: { name } }); }
async function start(post, bytes, operation = 'create', rev) { return ok('/upload/start', { code: post.managementCode, body: { size: bytes.length, operation, rev } }); }
async function append(post, upload, bytes) { return ok('/upload/chunk?offset=0', { code: post.managementCode, ticket: upload.ticket, body: bytes }); }
async function finish(post, upload, metadata) { return call('/upload/finish', { code: post.managementCode, ticket: upload.ticket,
  body: metadata === undefined ? undefined : { metadata } }); }
async function publish(bytes = new Uint8Array([1, 2, 3])) {
  const post = await prepare(), upload = await start(post, bytes); await append(post, upload, bytes);
  const response = await finish(post, upload); assert.equal(response.status, 200); return { ...post, item: await response.json() };
}

async function migratedPost() {
  const db = storage(), post = await publish();
  const original = db.find(post.path); db.files.delete(original.path_lower);
  const copied = db.put(post.path, original.bytes);
  assert.notEqual(copied.id, post.item.id);
  return { db, post, copied, environment: { ...env,
    CAMPAIGN_FILE_ID_ALIASES: JSON.stringify({ [copied.id]: post.item.id }) } };
}

test('account migration preserves the saved Studio identity in listing and management status', async () => {
  const { post, copied, environment } = await migratedPost();
  const status = await ok('/manage/status', { code: post.managementCode, environment });
  assert.equal(status.item.id, post.item.id);
  assert.equal(status.item.rev, copied.rev);
  const listed = await ok('/list', { method: 'GET', environment });
  assert.equal(listed.items[0].id, post.item.id);
  const download = await call('/download?path=' + encodeURIComponent(post.path), { method: 'GET', environment });
  assert.deepEqual(new Uint8Array(await download.arrayBuffer()), copied.bytes);
});

test('account migration replacement uses the new storage ID while returning the original Studio ID', async () => {
  const { db, post, copied, environment } = await migratedPost();
  for (const metadata of [undefined, { displayName: 'Retained title', description: 'Retained description' }]) {
    const bytes = new Uint8Array([7, 6, 5, 4]);
    const upload = await ok('/upload/start', { code: post.managementCode, environment,
      body: { size: bytes.length, operation: 'replace', rev: db.find(post.path).rev } });
    await ok('/upload/chunk?offset=0', { ticket: upload.ticket, body: bytes, environment });
    const finished = await ok('/upload/finish', { code: post.managementCode, ticket: upload.ticket, environment,
      body: metadata === undefined ? undefined : { metadata } });
    assert.equal(finished.id, post.item.id);
    assert.equal(db.find(post.path).id, copied.id);
    assert.deepEqual(db.find(post.path).bytes, bytes);
    if (metadata) {
      assert.equal(finished.metadataSaved, true);
      assert.equal((await ok('/manage/status', { code: post.managementCode, environment })).item.description, metadata.description);
    }
  }
});

test('account migration deletion confirms the original Studio ID without aliasing a reused path', async () => {
  const { db, post, copied, environment } = await migratedPost();
  const deleted = await ok('/manage/delete', { code: post.managementCode, body: { rev: copied.rev }, environment });
  assert.equal(deleted.id, post.item.id);
  assert.equal(db.find(copied.id), undefined);
  const anotherFile = db.put(post.path, new Uint8Array([99]));
  const status = await ok('/manage/status', { code: post.managementCode, environment });
  assert.equal(status.item.id, anotherFile.id);
  assert.notEqual(status.item.id, post.item.id);
});

test('invalid account migration settings cannot delete a campaign before returning an error', async () => {
  const { db, post, copied } = await migratedPost();
  for (const aliases of ['{', '[]', JSON.stringify({ [copied.id]: 'invalid' })]) {
    const response = await call('/manage/delete', { code: post.managementCode, body: { rev: copied.rev },
      environment: { ...env, CAMPAIGN_FILE_ID_ALIASES: aliases } });
    assert.equal(response.status, 503);
    assert.equal(db.find(post.path).id, copied.id);
  }
});

test('prepare reserves distinct private capabilities before publishing any bytes', async () => {
  const db = storage(); const a = await prepare(), b = await prepare();
  assert.notEqual(a.managementCode, b.managementCode); assert.notEqual(a.path, b.path);
  assert.match(a.path, /^\/managed\/[^/]+\.zip$/); assert.equal(db.files.size, 0);
  assert.deepEqual(await ok('/manage/status', { code: a.managementCode }), { ok: true, exists: false, path: a.path });
});

test('managed upload remains recoverable with saved code after finish response is lost', async () => {
  const db = storage(), post = await prepare(), data = new Uint8Array([11, 22, 33]);
  const upload = await start(post, data); await append(post, upload, data); db.loseFinish();
  assert.equal((await finish(post, upload)).status, 502);
  const status = await ok('/manage/status', { code: post.managementCode });
  assert.equal(status.exists, true); assert.equal(status.item.path, post.path); assert.ok(status.item.id); assert.ok(status.item.rev);
  const download = await call('/download?path=' + encodeURIComponent(post.path), { method: 'GET' });
  assert.deepEqual(new Uint8Array(await download.arrayBuffer()), data);
});

test('replacement preserves public identity and retains original ZIP until successful finish', async () => {
  const db = storage(), post = await publish(), data = new Uint8Array([9, 8, 7, 6]);
  const upload = await start(post, data, 'replace', post.item.rev); await append(post, upload, data);
  assert.deepEqual(db.find(post.path).bytes, new Uint8Array([1, 2, 3]));
  db.failFinish(); assert.equal((await finish(post, upload)).status, 502);
  assert.deepEqual(db.find(post.path).bytes, new Uint8Array([1, 2, 3]));
  assert.equal((await finish(post, upload)).status, 200);
  assert.equal(db.find(post.path).id, post.item.id); assert.deepEqual(db.find(post.path).bytes, data);
});

test('another post code cannot finish a managed upload or replace a chosen arbitrary target', async () => {
  const db = storage(), a = await publish(), b = await prepare(), data = new Uint8Array([4]);
  const upload = await start(a, data, 'replace', a.item.rev); await append(a, upload, data);
  assert.equal((await finish(b, upload)).status, 403);
  assert.equal((await call('/upload/finish', { ticket: upload.ticket })).status, 403);
  assert.equal((await call('/upload/start', { code: b.managementCode, body: { operation: 'replace', size: 1, path: a.path, rev: a.item.rev } })).status, 404);
  assert.deepEqual(db.find(a.path).bytes, new Uint8Array([1, 2, 3]));
});

test('tampered and absent management codes are rejected before mutations', async () => {
  const db = storage(), post = await publish();
  for (const code of [undefined, post.managementCode.slice(0, -4) + 'AAAA', 'not-a-code'])
    assert.equal((await call('/manage/delete', { code, body: { rev: post.item.rev } })).status, 403);
  assert.equal(db.files.size, 1);
});

test('replacement and deletion require the current nonempty revision', async () => {
  const db = storage(), post = await publish();
  for (const rev of [undefined, '', '000000001']) {
    const deletion = await call('/manage/delete', { code: post.managementCode, body: { rev } });
    const replacement = await call('/upload/start', { code: post.managementCode, body: { size: 1, operation: 'replace', rev } });
    assert.equal(deletion.status, rev ? 409 : 400); assert.equal(replacement.status, rev ? 409 : 400);
  }
  assert.equal(db.files.size, 1);
});

test('replacement cannot resurrect a post deleted while its upload is in progress', async () => {
  const db = storage(), post = await publish(), data = new Uint8Array([8]);
  const upload = await start(post, data, 'replace', post.item.rev); await append(post, upload, data);
  const deleted = await ok('/manage/delete', { code: post.managementCode, body: { rev: post.item.rev } });
  assert.equal(deleted.deleted, true); assert.equal(db.files.size, 0);
  assert.ok([404, 409].includes((await finish(post, upload)).status)); assert.equal(db.files.size, 0);
  assert.equal((await ok('/manage/status', { code: post.managementCode })).exists, false);
});

test('competing replacement finishes reject stale revision without overwriting the winner', async () => {
  const db = storage(), post = await publish(), a = await start(post, new Uint8Array([5]), 'replace', post.item.rev), b = await start(post, new Uint8Array([6]), 'replace', post.item.rev);
  await append(post, a, new Uint8Array([5])); await append(post, b, new Uint8Array([6]));
  assert.equal((await finish(post, a)).status, 200); assert.equal((await finish(post, b)).status, 409);
  assert.deepEqual(db.find(post.path).bytes, new Uint8Array([5]));
});

test('manual legacy assignment requires separate admin authorization and binds immutable file ID', async () => {
  const db = storage(), old = db.put('/old.zip', new Uint8Array([1]));
  assert.equal((await call('/manage/claim', { body: { path: old.path_display } })).status, 403);
  assert.equal((await call('/manage/claim', { admin: 'wrong', body: { path: old.path_display } })).status, 403);
  const claim = await ok('/manage/claim', { admin: env.CAMPAIGN_ADMIN_SECRET, body: { path: old.path_display } });
  const moved = db.put('/renamed.zip', old.bytes, old); db.files.delete('/old.zip');
  db.put('/old.zip', new Uint8Array([99]));
  const result = await ok('/manage/delete', { code: claim.managementCode, body: { rev: moved.rev } });
  assert.equal(result.id, old.id); assert.deepEqual(db.find('/old.zip').bytes, new Uint8Array([99]));
  assert.equal((await ok('/manage/status', { code: claim.managementCode })).exists, false);
});

test('listing follows all pages, includes public ZIP metadata and never management credentials', async () => {
  const db = storage(); db.put('/one.zip', new Uint8Array([1])); db.put('/two.zip', new Uint8Array([2]));
  const managed = await publish(); db.put('/private/secret.json', new Uint8Array([3])); db.put('/private/hidden.zip', new Uint8Array([4]));
  const result = await ok('/list', { method: 'GET' });
  assert.equal(result.count, 3); assert.ok(result.items.some(x => x.id === managed.item.id));
  assert.ok(result.items.every(x => x.rev)); assert.ok(!JSON.stringify(result).includes(managed.managementCode));
});

test('download and legacy assignment reject folders, traversal, private records and arbitrary file IDs', async () => {
  storage();
  for (const path of ['/', '/private/secret.json', '/private/hidden.zip', '/../hidden.zip', '/managed/../../private.zip', 'id:any']) {
    assert.equal((await call('/download?path=' + encodeURIComponent(path), { method: 'GET' })).status, 400);
    assert.equal((await call('/manage/claim', { admin: env.CAMPAIGN_ADMIN_SECRET, body: { path } })).status, 400);
  }
});

test('legacy anonymous chunk uploads remain usable and cannot choose managed destination', async () => {
  const db = storage(); const upload = await ok('/upload/start', { body: { name: '../managed/evil', size: 3, path: '/managed/chosen.zip' } });
  await ok('/upload/chunk?offset=0', { ticket: upload.ticket, body: new Uint8Array([7, 8, 9]) });
  const item = await ok('/upload/finish', { ticket: upload.ticket });
  assert.match(item.path, /^\/[^/]+\.zip$/); assert.equal(db.files.size, 1);
});

test('management requires configured signing secret without breaking legacy service', async () => {
  storage(); const disabled = { ...env, CAMPAIGN_MANAGEMENT_SECRET: undefined };
  assert.equal((await call('/manage/prepare', { body: { name: 'X' }, environment: disabled })).status, 503);
  const health = await ok('/health', { method: 'GET' }); assert.equal(health.campaignManagement, true);
  assert.equal((await call('/list', { method: 'GET', environment: disabled })).status, 200);
});

test('malformed or expired upload tickets are rejected as unauthorized input', async () => {
  storage();
  for (const ticket of [undefined, 'not-a-ticket', 'bad.bad', 'x'.repeat(4097)]) {
    const response = await call('/upload/finish', { ticket });
    assert.equal(response.status, 403); assert.ok(!(await response.text()).includes(ticket || 'never-printed'));
  }
  const key = await crypto.subtle.importKey('raw', new TextEncoder().encode('campaign-upload-v1:' + env.DROPBOX_APP_SECRET), { name: 'HMAC', hash: 'SHA-256' }, false, ['sign']);
  const bytes = new TextEncoder().encode(JSON.stringify({ session: 'expired', size: 1, path: '/old.zip', expires: Date.now() - 1000 }));
  const signature = await crypto.subtle.sign('HMAC', key, bytes);
  const ticket = Buffer.from(bytes).toString('base64') + '.' + Buffer.from(signature).toString('base64');
  assert.equal((await call('/upload/finish', { ticket })).status, 403);
});

test('chunk retries acknowledge received bytes and excessive requests reject before commit', async () => {
  const db = storage(), data = new Uint8Array([1, 2, 3]);
  const upload = await ok('/upload/start', { body: { name: 'legacy', size: data.length } });
  await ok('/upload/chunk?offset=0', { ticket: upload.ticket, body: data });
  assert.equal((await ok('/upload/chunk?offset=0', { ticket: upload.ticket, body: data })).offset, 3);
  assert.equal((await call('/upload/chunk?offset=0', { ticket: upload.ticket, body: new Uint8Array(8 * 1024 * 1024 + 1) })).status, 413);
  assert.equal((await call('/upload/finish', { ticket: upload.ticket })).status, 200); assert.equal(db.files.size, 1);
});

test('chunked uploads advertise no application cap and accept ZIPs above 512 MiB', async () => {
  storage(); const post = await prepare();
  const health = await ok('/health', { method: 'GET' }); assert.equal(health.maxCampaignBytes, null);
  const upload = await ok('/upload/start', { code: post.managementCode, body: { size: 536870913, operation: 'create' } });
  assert.equal(upload.chunkBytes, 1048576);
  assert.equal((await append(post, upload, new Uint8Array([80, 75, 3, 4]))).offset, 4);
});

test('upload cursors above 4 GiB reach Dropbox without truncation', async () => {
  storage(); const post = await prepare();
  const upload = await ok('/upload/start', { code: post.managementCode, body: { size: 4294967301, operation: 'create' } });
  const external = globalThis.fetch;
  // Dropbox fixture already holds the prefix; only the final five bytes cross this request.
  globalThis.fetch = async (url, init) => {
    if (!String(url).endsWith('/upload_session/append_v2')) return external(url, init);
    const cursor = JSON.parse(new Headers(init.headers).get('Dropbox-API-Arg')).cursor;
    assert.equal(cursor.offset, 4294967296); assert.equal(cursor.session_id.startsWith('session-'), true);
    assert.deepEqual(new Uint8Array(init.body), new Uint8Array([1, 2, 3, 4, 5]));
    return reply(null);
  };
  const result = await ok('/upload/chunk?offset=4294967296', { ticket: upload.ticket, body: new Uint8Array([1, 2, 3, 4, 5]) });
  assert.equal(result.offset, 4294967301);
});

test('upload lengths and cursors reject invalid or unsafe integers before storage append', async () => {
  storage(); const post = await prepare();
  for (const size of [0, -1, 1.5, '536870913', null, 9007199254740992])
    assert.equal((await call('/upload/start', { code: post.managementCode, body: { size, operation: 'create' } })).status, 400);
  const upload = await start(post, new Uint8Array([1, 2, 3]));
  for (const offset of ['-1', '0.5', '9007199254740992', 'NaN', '3'])
    assert.equal((await call('/upload/chunk?offset=' + offset, { ticket: upload.ticket, body: new Uint8Array([1]) })).status, 400);
  assert.equal((await append(post, upload, new Uint8Array([1, 2, 3]))).offset, 3);
});

test('long uploads survive an hour and expire after seven days', async () => {
  storage(); const post = await prepare(), originalNow = Date.now, issued = originalNow();
  try {
    Date.now = () => issued;
    const upload = await start(post, new Uint8Array([1, 2, 3]));
    Date.now = () => issued + 60 * 60 * 1000 + 1;
    assert.equal((await append(post, upload, new Uint8Array([1]))).offset, 1);
    Date.now = () => issued + 7 * 24 * 60 * 60 * 1000 - 1;
    assert.equal((await ok('/upload/chunk?offset=1', { ticket: upload.ticket, body: new Uint8Array([2]) })).offset, 2);
    Date.now = () => issued + 7 * 24 * 60 * 60 * 1000 + 1;
    assert.equal((await call('/upload/chunk?offset=2', { ticket: upload.ticket, body: new Uint8Array([3]) })).status, 403);
    assert.equal((await finish(post, upload)).status, 403);
  } finally { Date.now = originalNow; }
});

test('legacy multipart upload retains its 20 MiB file bound', async () => {
  const db = storage(), form = new FormData();
  form.set('file', new Blob([new Uint8Array(20971521)]), 'large.zip');
  const result = await worker.fetch(new Request('https://worker.invalid/upload', { method: 'POST', body: form }), env);
  assert.equal(result.status, 413); assert.equal(db.files.size, 0);
});

test('old multipart client still publishes a public root ZIP', async () => {
  const db = storage(), form = new FormData(); form.set('name', 'Old client'); form.set('meta', '{"author":"legacy"}');
  form.set('file', new Blob([new Uint8Array([42, 43])], { type: 'application/zip' }), 'old.zip');
  const response = await worker.fetch(new Request('https://worker.invalid/upload', { method: 'POST', body: form }), env);
  assert.equal(response.status, 200); const item = await response.json();
  assert.match(item.path, /^\/[^/]+\.zip$/); assert.deepEqual(db.find(item.path).bytes, new Uint8Array([42, 43]));
});

test('an unset administrator secret cannot assign a legacy code', async () => {
  storage(); const response = await call('/manage/claim', { environment: { ...env, CAMPAIGN_ADMIN_SECRET: undefined }, admin: 'anything', body: { path: '/legacy.zip' } });
  assert.equal(response.status, 403);
});

const tinyPng = 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jfN0AAAAASUVORK5CYII=';
const presentation = { displayName: 'NHL | Canucks Reloaded — Yeast-the-man', campaignName: 'NHL | Canucks Reloaded',
  author: 'Yeast-the-man', description: 'A complete campaign.\nChoose your route.', icon: tinyPng };

function downloadedName(response) {
  const header = response.headers.get('Content-Disposition');
  assert.ok(header, 'campaign download must advertise its readable filename');
  assert.match(header, /^attachment; filename="[\x20-\x21\x23-\x7e]+"; filename\*=UTF-8''/);
  assert.equal(response.headers.get('Access-Control-Expose-Headers'), 'Content-Disposition');
  return decodeURIComponent(header.split("filename*=UTF-8''")[1]);
}

test('campaign downloads use the presentation title without changing bytes or storage identity', async () => {
  const db = storage(), post = await prepare('Stored old title'), bytes = new Uint8Array([3, 1, 4]);
  const upload = await start(post, bytes); await append(post, upload, bytes); await finish(post, upload, presentation);
  const before = structuredClone([...db.files]);
  const request = globalThis.fetch, calls = [];
  globalThis.fetch = async (url, init) => { calls.push(String(url)); return request(url, init); };
  const response = await call('/download?path=' + encodeURIComponent(post.path), { method: 'GET' });
  assert.equal(response.status, 200);
  assert.equal(downloadedName(response), 'NHL _ Canucks Reloaded.zip');
  assert.deepEqual(new Uint8Array(await response.arrayBuffer()), bytes);
  assert.deepEqual([...db.files], before);
  assert.ok(calls.every(url => url.endsWith('/oauth2/token') || url.endsWith('/files/download')), 'download must not write, move, or require another file metadata lookup');
});

test('campaign download filenames handle Unicode punctuation reserved names and long titles', async () => {
  const cases = [
    ['Québec 🏒', 'Québec 🏒.zip'],
    ['..\\Bad/:Title?*"<>|\r\n.zip.zip', '.._Bad__Title________.zip'],
    ['CON', 'Campaign-CON.zip'], ['LPT1.notes', 'Campaign-LPT1.notes.zip'],
    ['AUX.zip', 'Campaign-AUX.zip'], ['....', 'Campaign.zip'],
    ['Season.t2tcampaign.zip', 'Season.zip'], ['Bad\ud800name', 'Bad_name.zip'],
    ["Team's (100%)", "Team's (100%).zip"],
    ['🏒'.repeat(40), '🏒'.repeat(30) + '.zip'], ['A'.repeat(160), 'A'.repeat(120) + '.zip']
  ];
  for (const [campaignName, expected] of cases) {
    const db = storage(), post = await prepare(), bytes = new Uint8Array([9]);
    const upload = await start(post, bytes); await append(post, upload, bytes);
    const result = await finish(post, upload, { campaignName, displayName: 'Safe display title' });
    assert.equal(result.status, 200);
    const response = await call('/download?path=' + encodeURIComponent(post.path), { method: 'GET' });
    assert.equal(response.status, 200);
    assert.equal(downloadedName(response), expected, campaignName);
    assert.ok(Buffer.byteLength(expected, 'utf8') <= 124);
    assert.deepEqual(new Uint8Array(await response.arrayBuffer()), db.find(post.path).bytes);
  }
});

test('campaign downloads keep working with missing corrupt or unavailable optional metadata', async () => {
  for (const mode of ['missing', 'corrupt', 'unavailable', 'missing-header', 'bad-header']) {
    const db = storage();
    const path = '/managed/12345678-1234-1234-1234-123456789abc - 2026-10-17T12-30-45-123Z - Legacy Team - Author.zip';
    const file = db.put(path, new Uint8Array([7, 8]));
    if (mode === 'corrupt') {
      const hash = Buffer.from(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(file.id))).toString('hex');
      db.put('/.campaign-metadata/' + hash + '.json', new TextEncoder().encode('bad-json'));
    }
    if (mode === 'unavailable') db.failMetadataRead();
    if (mode.endsWith('header')) {
      const request = globalThis.fetch;
      globalThis.fetch = async (url, init) => {
        const response = await request(url, init);
        if (String(url).endsWith('/files/download')) {
          if (mode === 'missing-header') response.headers.delete('Dropbox-API-Result');
          else response.headers.set('Dropbox-API-Result', 'bad-json');
        }
        return response;
      };
    }
    const response = await call('/download?path=' + encodeURIComponent(path), { method: 'GET' });
    assert.equal(response.status, 200, mode);
    assert.equal(downloadedName(response), 'Legacy Team - Author.zip', mode);
    assert.deepEqual(new Uint8Array(await response.arrayBuffer()), file.bytes);
  }
  const db = storage(); db.put('/Season 2026 - Round 1.zip', new Uint8Array([6]));
  const response = await call('/download?path=' + encodeURIComponent('/Season 2026 - Round 1.zip'), { method: 'GET' });
  assert.equal(downloadedName(response), 'Season 2026 - Round 1.zip');
});

test('public names remove recognized storage prefixes without changing identities or ordinary titles', async () => {
  const db = storage();
  const paths = [
    ['/2026-10-17T12-30-45-123Z - NHL - Author.zip', 'NHL - Author'],
    ['/managed/12345678-1234-1234-1234-123456789abc - 2026-10-17T12-30-45-123Z - Canucks.zip', 'Canucks'],
    ['/Season 2026 - Round 1.zip', 'Season 2026 - Round 1'],
    ['/12345678-1234-1234-1234-123456789abc - Ordinary title.zip', '12345678-1234-1234-1234-123456789abc - Ordinary title']
  ];
  for (const [path] of paths) db.put(path, new Uint8Array([1]));
  const list = await ok('/list', { method: 'GET' });
  for (const [path, name] of paths) {
    const item = list.items.find(x => x.path === path), file = db.find(path);
    assert.equal(item.name, name); assert.equal(item.campaignName, name); assert.equal(item.id, file.id);
    assert.equal(item.rev, file.rev); assert.equal(item.contentHash, file.content_hash); assert.equal(item.description, ''); assert.equal(item.icon, '');
  }
});

test('metadata persists outside public ZIPs and is returned by finish list and management status', async () => {
  const db = storage(), post = await prepare(), bytes = new Uint8Array([1, 2]);
  const upload = await start(post, bytes); await append(post, upload, bytes);
  const response = await finish(post, upload, presentation); assert.equal(response.status, 200);
  const result = await response.json(); assert.equal(result.metadataSaved, true); assert.equal(result.name, presentation.displayName);
  const list = await ok('/list', { method: 'GET' }), status = await ok('/manage/status', { code: post.managementCode });
  assert.equal(list.count, 1);
  for (const item of [result, list.items[0], status.item]) {
    for (const key of ['campaignName', 'author', 'description', 'icon']) assert.equal(item[key], presentation[key]);
    assert.equal(item.name, presentation.displayName); assert.equal(item.path, post.path);
  }
  const sidecar = [...db.files.values()].find(x => x.path_display.startsWith('/.campaign-metadata/'));
  assert.ok(sidecar); assert.match(sidecar.path_display, /^\/\.campaign-metadata\/[a-f0-9]{64}\.json$/);
  assert.ok(!new TextDecoder().decode(sidecar.bytes).includes(post.managementCode));
  assert.equal((await call('/download?path=' + encodeURIComponent(sidecar.path_display), { method: 'GET' })).status, 400);
  assert.equal((await ok('/health', { method: 'GET' })).campaignMetadata, true);
});

test('replacement without presentation preserves metadata and metadata failures do not misreport committed ZIPs', async () => {
  const db = storage(), post = await prepare(), first = new Uint8Array([1]);
  let upload = await start(post, first); await append(post, upload, first);
  let response = await finish(post, upload, presentation), current = await response.json();
  const sidecar = [...db.files.values()].find(x => x.path_display.startsWith('/.campaign-metadata/'));
  assert.ok(sidecar, 'metadata must be persisted');
  upload = await start(post, new Uint8Array([2]), 'replace', current.rev); await append(post, upload, new Uint8Array([2]));
  response = await finish(post, upload); current = await response.json();
  assert.equal(current.name, presentation.displayName); assert.equal(current.description, presentation.description);
  assert.deepEqual(db.find(sidecar.path_display).bytes, sidecar.bytes);
  upload = await start(post, new Uint8Array([3]), 'replace', current.rev); await append(post, upload, new Uint8Array([3]));
  db.failMetadataWrite(); response = await finish(post, upload, { ...presentation, description: 'New description' });
  assert.equal(response.status, 200); current = await response.json();
  assert.equal(current.ok, true); assert.equal(current.metadataSaved, false); assert.ok(current.metadataWarning);
  assert.equal(current.id, db.find(post.path).id); assert.equal(current.rev, db.find(post.path).rev);
  assert.deepEqual(db.find(post.path).bytes, new Uint8Array([3]));
  assert.deepEqual(db.find(sidecar.path_display).bytes, sidecar.bytes);
});

test('invalid presentation rejects before ZIP commit and cannot be submitted by another owner', async () => {
  const db = storage(), post = await prepare(), other = await prepare(), bytes = new Uint8Array([1]);
  const upload = await start(post, bytes); await append(post, upload, bytes);
  const oversizedPng = Buffer.from(tinyPng.split(',')[1], 'base64'); oversizedPng.writeUInt32BE(513, 16);
  for (const metadata of [null, [], { displayName: 'x'.repeat(241) }, { author: 'x'.repeat(61) }, { campaignName: 'x'.repeat(161) },
    { description: 'x'.repeat(2001) }, { icon: 'data:image/png;base64,AAAA' }, { icon: tinyPng + 'a'.repeat(350000) },
    { icon: 'data:image/png;base64,' + oversizedPng.toString('base64') }, { icon: 4 }, { author: {} }]) {
    const response = await finish(post, upload, metadata); assert.equal(response.status, 400); assert.equal(db.find(post.path), undefined);
  }
  assert.equal((await finish(other, upload, presentation)).status, 403); assert.equal(db.files.size, 0);
  assert.equal((await finish(post, upload, presentation)).status, 200);
});

test('metadata read failure and damaged sidecars keep list available with readable fallback names', async () => {
  const db = storage(), post = await prepare('Readable campaign'), bytes = new Uint8Array([1]);
  const upload = await start(post, bytes); await append(post, upload, bytes);
  assert.equal((await finish(post, upload, presentation)).status, 200);
  const sidecar = [...db.files.values()].find(x => x.path_display.startsWith('/.campaign-metadata/'));
  assert.ok(sidecar, 'metadata must be persisted');
  db.put(sidecar.path_display, new TextEncoder().encode('{broken'), sidecar);
  let list = await ok('/list', { method: 'GET' }); assert.equal(list.count, 1); assert.equal(list.items[0].name, 'Readable campaign');
  db.failMetadataRead(); list = await ok('/list', { method: 'GET' }); assert.equal(list.items[0].name, 'Readable campaign');
});

test('failed ZIP commit never changes metadata', async () => {
  const db = storage(), post = await prepare(), bytes = new Uint8Array([1]);
  const upload = await start(post, bytes); await append(post, upload, bytes); db.failFinish();
  assert.equal((await finish(post, upload, presentation)).status, 502); assert.equal(db.files.size, 0);
});

test('delayed old metadata write cannot overwrite details of a newer replacement', async () => {
  const db = storage(), post = await prepare(), bytes = new Uint8Array([1]);
  let upload = await start(post, bytes); await append(post, upload, bytes);
  const first = await (await finish(post, upload, presentation)).json();
  const request = globalThis.fetch;
  let release, reached;
  const waiting = new Promise(resolve => { reached = resolve; }), gate = new Promise(resolve => { release = resolve; });
  globalThis.fetch = async (url, init) => {
    if (String(url).endsWith('/files/upload') && typeof init.body === 'string' && JSON.parse(init.body).metadata?.description === 'Older replacement') {
      reached(); await gate;
    }
    return request(url, init);
  };
  upload = await start(post, new Uint8Array([2]), 'replace', first.rev); await append(post, upload, new Uint8Array([2]));
  const older = finish(post, upload, { ...presentation, description: 'Older replacement' });
  await waiting;
  const olderRevision = db.find(post.path).rev;
  const newerUpload = await start(post, new Uint8Array([3]), 'replace', olderRevision); await append(post, newerUpload, new Uint8Array([3]));
  const newer = await (await finish(post, newerUpload, { ...presentation, description: 'Newer replacement' })).json();
  assert.equal(newer.metadataSaved, true); release();
  const delayed = await (await older).json(); assert.equal(delayed.ok, true); assert.equal(delayed.metadataSaved, false);
  assert.equal(delayed.rev, olderRevision); assert.deepEqual(db.find(post.path).bytes, new Uint8Array([3]));
  const status = await ok('/manage/status', { code: post.managementCode }); assert.equal(status.item.description, 'Newer replacement');
});

test('a lost metadata response reports uncertainty while keeping the committed ZIP recoverable', async () => {
  const db = storage(), post = await prepare(), bytes = new Uint8Array([1]);
  const upload = await start(post, bytes); await append(post, upload, bytes); db.loseMetadataWrite();
  const result = await (await finish(post, upload, presentation)).json();
  assert.equal(result.ok, true); assert.equal(result.metadataSaved, false); assert.match(result.metadataWarning, /confirmed saved/);
  const status = await ok('/manage/status', { code: post.managementCode });
  assert.equal(status.item.rev, result.rev); assert.equal(status.item.description, presentation.description);
});

test('stored metadata cannot override trusted public identity or expose extra fields', async () => {
  const db = storage(), post = await prepare(), bytes = new Uint8Array([1]);
  const upload = await start(post, bytes); await append(post, upload, bytes);
  const result = await (await finish(post, upload, { ...presentation, id: 'id:forged', path: '/forged.zip', managementCode: 'PRIVATE' })).json();
  const sidecar = [...db.files.values()].find(x => x.path_display.startsWith('/.campaign-metadata/'));
  const stored = JSON.parse(new TextDecoder().decode(sidecar.bytes));
  assert.equal(stored.metadata.managementCode, undefined); assert.equal(stored.metadata.path, undefined);
  stored.metadata.id = 'id:forged'; stored.metadata.path = '/forged.zip'; stored.metadata.managementCode = 'PRIVATE';
  db.put(sidecar.path_display, new TextEncoder().encode(JSON.stringify(stored)), sidecar);
  const item = (await ok('/list', { method: 'GET' })).items[0];
  assert.equal(item.id, result.id); assert.equal(item.path, post.path); assert.equal(item.managementCode, undefined);
  assert.ok(!JSON.stringify(item).includes('PRIVATE'));
});

test('list preview reads use at most four concurrent requests', async () => {
  const db = storage(); for (let i = 0; i < 9; i++) db.put('/Campaign ' + i + '.zip', new Uint8Array([1]));
  const request = globalThis.fetch; let active = 0, maximum = 0;
  globalThis.fetch = async (url, init) => {
    if (!String(url).endsWith('/files/download')) return request(url, init);
    maximum = Math.max(maximum, ++active);
    try { await new Promise(resolve => setImmediate(resolve)); return await request(url, init); }
    finally { active--; }
  };
  const result = await ok('/list', { method: 'GET' }); assert.equal(result.count, 9);
  assert.equal(maximum, 4);
});

test('zero-length finish bodies remain compatible with clients that send Content-Length zero', async () => {
  storage(); const post = await prepare(), bytes = new Uint8Array([1]);
  const upload = await start(post, bytes); await append(post, upload, bytes);
  const response = await call('/upload/finish', { code: post.managementCode, ticket: upload.ticket, body: new Uint8Array() });
  assert.equal(response.status, 200);
});

test('oversized upstream metadata errors stop reading before consuming the full response', async () => {
  const db = storage(); db.put('/Readable.zip', new Uint8Array([1]));
  const request = globalThis.fetch; let chunksRead = 0;
  globalThis.fetch = async (url, init) => {
    if (!String(url).endsWith('/files/download')) return request(url, init);
    return new Response(new ReadableStream({ pull(controller) {
      if (chunksRead++ === 20) controller.close(); else controller.enqueue(new Uint8Array(65536));
    } }), { status: 503 });
  };
  const list = await ok('/list', { method: 'GET' }); assert.equal(list.items[0].name, 'Readable');
  assert.ok(chunksRead <= 9, 'upstream error body must remain bounded');
});

test('legal metadata near the character limit permits JSON unicode escaping', async () => {
  storage(); const post = await prepare(), bytes = new Uint8Array([1]);
  const upload = await start(post, bytes); await append(post, upload, bytes);
  const png = Buffer.from(tinyPng.split(',')[1], 'base64');
  const chunk = Buffer.alloc(260012, 65); chunk.writeUInt32BE(260000, 0); chunk.write('tEXt', 4); chunk.write('Note\0', 8);
  let crc = 0xffffffff;
  for (const value of chunk.subarray(4, -4)) {
    crc ^= value; for (let i = 0; i < 8; i++) crc = (crc >>> 1) ^ ((crc & 1) ? 0xedb88320 : 0);
  }
  chunk.writeUInt32BE((crc ^ 0xffffffff) >>> 0, chunk.length - 4);
  const icon = 'data:image/png;base64,' + Buffer.concat([png.subarray(0, -12), chunk, png.subarray(-12)]).toString('base64');
  assert.ok(icon.length <= 350000);
  const encoded = Array.from(icon, ch => '\\u' + ch.charCodeAt(0).toString(16).padStart(4, '0')).join('');
  const response = await worker.fetch(new Request('https://worker.invalid/upload/finish', { method: 'POST',
    headers: { 'Content-Type': 'application/json', 'X-Campaign-Manage': post.managementCode, 'X-Campaign-Upload': upload.ticket },
    body: '{"metadata":{"icon":"' + encoded + '"}}' }), env);
  assert.equal(response.status, 200); assert.equal((await response.json()).metadataSaved, true);
});

function pngChunk(type, data) {
  const chunk = Buffer.alloc(data.length + 12); chunk.writeUInt32BE(data.length); chunk.write(type, 4); data.copy(chunk, 8);
  let crc = 0xffffffff;
  for (const byte of chunk.subarray(4, -4)) {
    crc ^= byte; for (let bit = 0; bit < 8; bit++) crc = (crc >>> 1) ^ ((crc & 1) ? 0xedb88320 : 0);
  }
  chunk.writeUInt32BE((crc ^ 0xffffffff) >>> 0, chunk.length - 4); return chunk;
}
function coverPng(pixel = [30, 150, 180, 255], scanlines = Buffer.from([0, ...pixel])) {
  const ihdr = Buffer.from([0, 0, 0, 1, 0, 0, 0, 1, 8, 6, 0, 0, 0]);
  return 'data:image/png;base64,' + Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]),
    pngChunk('IHDR', ihdr), pngChunk('IDAT', deflateSync(scanlines)), pngChunk('IEND', Buffer.alloc(0))]).toString('base64');
}
async function coveredPost() {
  const db = storage(), post = await prepare(), bytes = new Uint8Array([3, 1, 4]);
  const upload = await start(post, bytes); await append(post, upload, bytes);
  const item = await (await finish(post, upload, { ...presentation, icon: coverPng() })).json();
  return { db, post: { ...post, item } };
}
const coverBody = (post, icon = coverPng([255, 20, 80, 255])) => ({ id: post.item.id, rev: post.item.rev, icon });

test('cover editing is advertised independently of ZIP upload metadata', async () => {
  storage(); const health = await ok('/health', { method: 'GET' });
  assert.equal(health.campaignCoverEdit, true); assert.ok(health.endpoints.includes('/manage/icon'));
});

test('cover edit changes only the image and never writes uploads moves or deletes the ZIP', async () => {
  const { db, post } = await coveredPost(), original = structuredClone(db.find(post.path)), body = coverBody(post);
  const request = globalThis.fetch, writes = [];
  globalThis.fetch = async (url, init) => {
    if (/\/files\/(upload|delete|move|copy)/.test(String(url))) writes.push({ url: String(url), arg: JSON.parse(new Headers(init.headers).get('Dropbox-API-Arg') ?? init.body) });
    return request(url, init);
  };
  const result = await ok('/manage/icon', { code: post.managementCode, body });
  assert.equal(result.iconSaved, true); assert.equal(result.icon, body.icon);
  for (const key of ['id', 'path', 'rev', 'size', 'contentHash', 'modified', 'name', 'campaignName', 'author', 'description'])
    assert.deepEqual(result[key], post.item[key], key + ' must stay unchanged');
  assert.deepEqual(db.find(post.path), original);
  assert.equal(writes.length, 1); assert.ok(writes[0].url.endsWith('/files/upload'));
  assert.ok(writes[0].arg.path.startsWith('/.campaign-metadata/'));
  const listed = (await ok('/list', { method: 'GET' })).items[0]; assert.equal(listed.icon, body.icon);
  const download = await call('/download?path=' + encodeURIComponent(post.path), { method: 'GET' });
  assert.equal(downloadedName(download), 'NHL _ Canucks Reloaded.zip');
  assert.deepEqual(new Uint8Array(await download.arrayBuffer()), original.bytes);
});

test('cover edit rejects invalid ownership identities stale revisions and extra metadata without changes', async () => {
  const { db, post } = await coveredPost(), other = await publish(), before = structuredClone([...db.files]);
  for (const code of [undefined, 'invalid-code', post.managementCode.slice(0, -4) + 'AAAA', other.managementCode]) {
    const response = await call('/manage/icon', { code, body: coverBody(post) });
    assert.equal(response.status, 403); assert.ok(!(await response.text()).includes(code || 'secret'));
  }
  for (const [patch, status] of [[{ rev: '' }, 400], [{ rev: '000000001' }, 409], [{ id: '' }, 400],
    [{ id: other.item.id }, 403], [{ description: 'replace this too' }, 400], [{ path: other.path }, 400]]) {
    const response = await call('/manage/icon', { code: post.managementCode, body: { ...coverBody(post), ...patch } });
    assert.equal(response.status, status, JSON.stringify(patch));
  }
  assert.deepEqual([...db.files], before);
});

test('cover edit rejects malformed oversized and incomplete PNG data before storage mutations', async () => {
  const { db, post } = await coveredPost(), before = structuredClone([...db.files]);
  const bytes = Buffer.from(coverPng().split(',')[1], 'base64'), corrupt = Buffer.from(bytes); corrupt[corrupt.length - 1] ^= 1;
  const invalid = ['', null, 7, 'data:image/jpeg;base64,AAAA', 'data:image/png;base64,AAAA',
    'data:image/png;base64,' + bytes.subarray(0, 33).toString('base64'), 'data:image/png;base64,' + corrupt.toString('base64'),
    coverPng(undefined, Buffer.from([5, 1, 2, 3, 4])), coverPng(undefined, Buffer.alloc(4000000)),
    'data:image/png;base64,' + 'A'.repeat(350000)];
  for (const icon of invalid) {
    const response = await call('/manage/icon', { code: post.managementCode, body: coverBody(post, icon) });
    assert.equal(response.status, 400);
  }
  const oversized = await call('/manage/icon', { code: post.managementCode, body: new Uint8Array(2200000) });
  assert.ok([400, 413].includes(oversized.status)); assert.deepEqual([...db.files], before);
});

test('cover edit preserves migrated public identity and writes the real storage ID sidecar', async () => {
  const { db, post, copied, environment } = await migratedPost();
  const selected = { ...post, item: { ...post.item, rev: copied.rev } }, original = structuredClone(copied);
  const result = await ok('/manage/icon', { code: post.managementCode, body: coverBody(selected), environment });
  assert.equal(result.id, post.item.id); assert.equal(result.rev, copied.rev); assert.equal(result.path, post.path);
  assert.deepEqual(db.find(post.path), original);
  const sidecar = [...db.files.values()].find(x => x.path_display.startsWith('/.campaign-metadata/'));
  assert.equal(JSON.parse(new TextDecoder().decode(sidecar.bytes)).id, copied.id);
});

test('invalid migration settings fail cover editing before any sidecar write', async () => {
  const { db, post, copied } = await migratedPost(), before = structuredClone([...db.files]);
  const response = await call('/manage/icon', { code: post.managementCode, body: { ...coverBody(post), rev: copied.rev },
    environment: { ...env, CAMPAIGN_FILE_ID_ALIASES: '{' } });
  assert.equal(response.status, 503); assert.deepEqual([...db.files], before);
});

test('cover edit conflicts rather than overwriting a concurrent metadata update', async () => {
  const { db, post } = await coveredPost(), body = coverBody(post), request = globalThis.fetch;
  let intercepted = false;
  globalThis.fetch = async (url, init) => {
    if (String(url).endsWith('/files/upload') && !intercepted) {
      intercepted = true;
      const path = JSON.parse(new Headers(init.headers).get('Dropbox-API-Arg')).path, prior = db.find(path);
      const stored = JSON.parse(new TextDecoder().decode(prior.bytes)); stored.metadata.description = 'A concurrent description';
      db.put(path, new TextEncoder().encode(JSON.stringify(stored)), prior);
    }
    return request(url, init);
  };
  const response = await call('/manage/icon', { code: post.managementCode, body });
  assert.equal(response.status, 409); assert.match((await response.json()).error, /refresh/i);
  const current = (await ok('/manage/status', { code: post.managementCode })).item;
  assert.equal(current.description, 'A concurrent description'); assert.equal(current.icon, post.item.icon);
  assert.equal(current.rev, post.item.rev);
});

test('cover merge uses the exact sidecar snapshot whose revision guards the write', async () => {
  const { db, post } = await coveredPost(), body = coverBody(post), request = globalThis.fetch;
  let reads = 0;
  globalThis.fetch = async (url, init) => {
    if (String(url).endsWith('/files/download')) {
      const path = JSON.parse(new Headers(init.headers).get('Dropbox-API-Arg')).path;
      if (path.startsWith('/.campaign-metadata/')) {
        const response = await request(url, init);
        if (++reads === 1) {
          const prior = db.find(path), stored = JSON.parse(new TextDecoder().decode(prior.bytes));
          stored.metadata.author = 'Concurrent author';
          db.put(path, new TextEncoder().encode(JSON.stringify(stored)), prior);
        }
        return response;
      }
    }
    return request(url, init);
  };
  const response = await call('/manage/icon', { code: post.managementCode, body });
  assert.equal(response.status, 409);
  const current = (await ok('/manage/status', { code: post.managementCode })).item;
  assert.equal(current.author, 'Concurrent author'); assert.equal(current.icon, post.item.icon);
});

test('cover edit detects a ZIP replacement during the sidecar write and never claims unchanged ZIP success', async () => {
  const { db, post } = await coveredPost(), request = globalThis.fetch;
  globalThis.fetch = async (url, init) => {
    if (String(url).endsWith('/files/upload')) db.put(post.path, new Uint8Array([8, 9]), db.find(post.path));
    return request(url, init);
  };
  const response = await call('/manage/icon', { code: post.managementCode, body: coverBody(post) });
  assert.equal(response.status, 409); assert.deepEqual(db.find(post.path).bytes, new Uint8Array([8, 9]));
});

test('cover storage errors preserve ZIP and access and report a refreshable failure', async () => {
  for (const failure of ['failMetadataWrite', 'failMetadataRead', 'loseMetadataWrite']) {
    const { db, post } = await coveredPost(), original = structuredClone(db.find(post.path)); db[failure]();
    const response = await call('/manage/icon', { code: post.managementCode, body: coverBody(post) });
    assert.equal(response.status, 502, failure); assert.match((await response.json()).error, /refresh|retry/i);
    assert.deepEqual(db.find(post.path), original);
    assert.equal((await ok('/manage/status', { code: post.managementCode })).exists, true);
  }
});

const poolPath = '/managed-pools/11111111-2222-3333-4444-555555555555 - 2026-11-02T00-00-00-000Z - All three pools.zip';
async function preparePools(name = 'All three pools') {
  return ok('/manage/prepare', { body: { name, kind: 'base-game-pools' } });
}
async function publishPools(metadata) {
  const post = await preparePools(), bytes = new Uint8Array([2, 7, 1, 8]), upload = await start(post, bytes);
  await append(post, upload, bytes);
  const response = await finish(post, upload, metadata);
  assert.equal(response.status, 200);
  return { ...post, item: await response.json() };
}

test('health lets clients negotiate Base Game Pools sharing without losing live capabilities', async () => {
  storage(); const health = await ok('/health', { method: 'GET' });
  assert.equal(health.baseGamePools, true);
  assert.equal(health.campaignManagement, true); assert.equal(health.campaignMetadata, true);
  assert.equal(health.campaignCoverEdit, true); assert.equal(health.maxCampaignBytes, null);
});

test('pool listing is separate from legacy and explicit campaign listings across all storage pages', async () => {
  const db = storage();
  const legacy = db.put('/Old campaign.zip', new Uint8Array([1]));
  const managed = db.put('/managed/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee - New campaign.zip', new Uint8Array([2]));
  const pools = db.put(poolPath, new Uint8Array([3]));
  db.put('/managed-pools/not-a-reserved-name.zip', new Uint8Array([4]));
  db.put('/managed-pools/11111111-2222-3333-4444-555555555555 - Nested/private.zip', new Uint8Array([5]));
  db.put('/.campaign-metadata/private.zip', new Uint8Array([6]));
  const selected = await ok('/list?kind=base-game-pools', { method: 'GET' });
  assert.equal(selected.count, 1); assert.equal(selected.items[0].id, pools.id);
  assert.equal(selected.items[0].kind, 'base-game-pools'); assert.equal(selected.items[0].name, 'All three pools');
  for (const path of ['/list', '/list?kind=campaign']) {
    const listed = await ok(path, { method: 'GET' });
    assert.deepEqual(listed.items.map(item => item.id).sort(), [legacy.id, managed.id].sort());
    assert.equal(listed.count, 2); assert.ok(listed.items.every(item => item.kind === 'campaign'));
  }
});

test('prepare binds a pool code to its reserved pool path while old and explicit campaign requests stay campaigns', async () => {
  const db = storage();
  const pools = await preparePools(), legacy = await prepare(), campaign = await ok('/manage/prepare', { body: { name: 'New', kind: 'campaign' } });
  assert.match(pools.path, /^\/managed-pools\/[0-9a-f-]{36} - [^/]+\.zip$/);
  assert.equal(pools.kind, 'base-game-pools');
  for (const post of [legacy, campaign]) {
    assert.match(post.path, /^\/managed\/[0-9a-f-]{36} - [^/]+\.zip$/); assert.equal(post.kind, 'campaign');
  }
  assert.equal(JSON.parse(Buffer.from(pools.managementCode.split('.')[1], 'base64')).path, pools.path);
  assert.deepEqual(await ok('/manage/status', { code: pools.managementCode }), { ok: true, exists: false, path: pools.path });
  assert.equal(db.files.size, 0);
});

test('list and prepare reject unknown or malformed categories before reaching storage', async () => {
  storage(); let requests = 0; const request = globalThis.fetch;
  globalThis.fetch = async (...args) => { requests++; return request(...args); };
  for (const kind of ['', 'pools', 'BASE-GAME-POOLS', ' campaign']) {
    assert.equal((await call('/list?kind=' + encodeURIComponent(kind), { method: 'GET' })).status, 400, kind);
    assert.equal((await call('/manage/prepare', { body: { name: 'Invalid', kind } })).status, 400, kind);
  }
  for (const kind of [null, false, 0, [], {}])
    assert.equal((await call('/manage/prepare', { body: { kind } })).status, 400);
  assert.equal(requests, 0);
});

test('pool upload finish replacement and deletion retain pool category and exact public identity', async () => {
  const db = storage(), post = await publishPools({ displayName: 'Pool set - Author', description: 'Three selections', kind: 'campaign' });
  assert.match(post.item.path, /^\/managed-pools\//); assert.equal(post.item.path, post.path);
  assert.equal(post.item.kind, 'base-game-pools'); assert.equal(post.item.metadataSaved, true);
  assert.equal((await ok('/list', { method: 'GET' })).count, 0);
  assert.equal((await ok('/manage/status', { code: post.managementCode })).item.kind, 'base-game-pools');
  const bytes = new Uint8Array([8, 6, 7, 5]), upload = await start(post, bytes, 'replace', post.item.rev);
  assert.equal(upload.path, post.path); await append(post, upload, bytes);
  const replaced = await finish(post, upload); assert.equal(replaced.status, 200); const item = await replaced.json();
  assert.equal(item.id, post.item.id); assert.equal(item.path, post.path); assert.equal(item.kind, 'base-game-pools');
  assert.equal(item.name, 'Pool set - Author'); assert.equal(item.description, 'Three selections');
  assert.deepEqual(db.find(post.path).bytes, bytes);
  const deleted = await ok('/manage/delete', { code: post.managementCode, body: { rev: item.rev } });
  assert.equal(deleted.id, post.item.id); assert.equal(deleted.path, post.path); assert.equal(db.find(post.path), undefined);
});

test('campaign and pool codes cannot finish each others uploads or redirect their signed destinations', async () => {
  const db = storage(), campaign = await publish(), pools = await publishPools();
  assert.notEqual(campaign.item.kind, pools.item.kind);
  for (const [owner, other] of [[campaign, pools], [pools, campaign]]) {
    const bytes = new Uint8Array([9, 2, 6]), previous = structuredClone(db.find(other.path));
    const upload = await ok('/upload/start', { code: owner.managementCode,
      body: { size: bytes.length, operation: 'replace', rev: owner.item.rev, path: other.path } });
    assert.equal(upload.path, owner.path); await append(owner, upload, bytes);
    assert.equal((await finish(other, upload)).status, 403);
    const finished = await ok('/upload/finish', { code: owner.managementCode, ticket: upload.ticket,
      body: { path: other.path, metadata: { kind: other.item.kind } } });
    assert.equal(finished.path, owner.path); assert.equal(finished.id, owner.item.id); assert.equal(finished.kind, owner.item.kind);
    assert.deepEqual(db.find(other.path), previous);
  }
});

test('upload category claims cannot reinterpret an authorized destination or turn anonymous uploads into pools', async () => {
  const db = storage(), pools = await publishPools(), campaign = await publish();
  for (const [post, kind] of [[pools, 'campaign'], [campaign, 'base-game-pools'], [pools, 'invalid']]) {
    const before = structuredClone([...db.files]);
    assert.equal((await call('/upload/start', { code: post.managementCode,
      body: { size: 1, operation: 'replace', rev: post.item.rev, kind } })).status, 400);
    assert.deepEqual([...db.files], before);
  }
  for (const kind of ['base-game-pools', 'invalid', null])
    assert.equal((await call('/upload/start', { body: { name: 'Anonymous', size: 1, kind } })).status, 400);
  const bytes = new Uint8Array([3]), upload = await ok('/upload/start', { code: pools.managementCode,
    body: { size: 1, operation: 'replace', rev: pools.item.rev, kind: 'base-game-pools' } });
  await append(pools, upload, bytes);
  for (const kind of ['campaign', 'invalid', null])
    assert.equal((await call('/upload/finish', { code: pools.managementCode, ticket: upload.ticket, body: { kind } })).status, 400);
  assert.deepEqual(db.find(pools.path).bytes, new Uint8Array([2, 7, 1, 8]));
  const completed = await ok('/upload/finish', { code: pools.managementCode, ticket: upload.ticket, body: { kind: 'base-game-pools' } });
  assert.equal(completed.kind, 'base-game-pools'); assert.equal(completed.path, pools.path);
});

test('tampering a pool capability or upload ticket to the campaign namespace is rejected', async () => {
  storage(); const post = await preparePools(), bytes = new Uint8Array([1]), upload = await start(post, bytes);
  const cap = post.managementCode.split('.'), claims = JSON.parse(Buffer.from(cap[1], 'base64'));
  claims.path = claims.path.replace('/managed-pools/', '/managed/'); cap[1] = Buffer.from(JSON.stringify(claims)).toString('base64');
  assert.notEqual(cap.join('.'), post.managementCode);
  assert.equal((await call('/manage/status', { code: cap.join('.') })).status, 403);
  const ticket = upload.ticket.split('.'), payload = JSON.parse(Buffer.from(ticket[0], 'base64'));
  payload.path = payload.path.replace('/managed-pools/', '/managed/');
  payload.publicPath = payload.publicPath.replace('/managed-pools/', '/managed/'); ticket[0] = Buffer.from(JSON.stringify(payload)).toString('base64');
  assert.notEqual(ticket.join('.'), upload.ticket);
  assert.equal((await call('/upload/chunk?offset=0', { ticket: ticket.join('.'), body: bytes })).status, 403);
  assert.equal((await call('/upload/finish', { code: post.managementCode, ticket: ticket.join('.') })).status, 403);
});

test('pool downloads return the selected ZIP and readable presentation while rejecting unsafe paths', async () => {
  const db = storage(), file = db.put(poolPath, new Uint8Array([2, 7, 1, 8]));
  const hash = Buffer.from(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(file.id))).toString('hex');
  db.put('/.campaign-metadata/' + hash + '.json', new TextEncoder().encode(JSON.stringify({ v: 1, id: file.id,
    metadata: { campaignName: 'My pool selection' } })));
  const response = await call('/download?path=' + encodeURIComponent(poolPath), { method: 'GET' });
  assert.equal(response.status, 200); assert.equal(downloadedName(response), 'My pool selection.zip');
  assert.deepEqual(new Uint8Array(await response.arrayBuffer()), file.bytes);
  for (const path of ['/managed-pools/no-uuid.zip', poolPath + '/child.zip', '/managed-pools/../private.zip',
    poolPath.replace('All three pools', 'bad\\path'), poolPath.replace('All three pools', 'bad\u0000path')])
    assert.equal((await call('/download?path=' + encodeURIComponent(path), { method: 'GET' })).status, 400, path);
});

test('pool cover editing preserves the ZIP and rejects a campaign code for the pool identity', async () => {
  const db = storage(), pools = await publishPools({ displayName: 'Pools', description: 'Keep this', icon: coverPng() }), campaign = await publish();
  const previous = structuredClone(db.find(pools.path)), body = coverBody(pools);
  assert.equal(pools.item.kind, 'base-game-pools');
  assert.equal((await call('/manage/icon', { code: campaign.managementCode, body })).status, 403);
  const edited = await ok('/manage/icon', { code: pools.managementCode, body });
  assert.equal(edited.kind, 'base-game-pools'); assert.equal(edited.id, pools.item.id); assert.equal(edited.rev, pools.item.rev);
  assert.equal(edited.path, pools.path); assert.equal(edited.icon, body.icon); assert.equal(edited.description, 'Keep this');
  assert.deepEqual(db.find(pools.path), previous);
  assert.equal((await ok('/list?kind=base-game-pools', { method: 'GET' })).items[0].icon, body.icon);
});

test('legacy claims remain campaigns and cannot acquire or reinterpret pool posts', async () => {
  const db = storage(), root = db.put('/Legacy campaign.zip', new Uint8Array([1]));
  assert.equal((await call('/manage/claim', { admin: env.CAMPAIGN_ADMIN_SECRET, body: { path: root.path_display, kind: 'base-game-pools' } })).status, 400);
  const claimed = await ok('/manage/claim', { admin: env.CAMPAIGN_ADMIN_SECRET, body: { path: root.path_display, kind: 'campaign' } });
  assert.equal(claimed.kind, 'campaign');
  const pool = await publishPools();
  assert.equal((await call('/manage/claim', { admin: env.CAMPAIGN_ADMIN_SECRET, body: { path: pool.path } })).status, 400);
  db.files.delete(root.path_lower); db.put(poolPath, root.bytes, root);
  assert.equal((await call('/manage/status', { code: claimed.managementCode })).status, 403);
  assert.equal((await call('/upload/start', { code: claimed.managementCode,
    body: { size: 1, operation: 'replace', rev: db.find(poolPath).rev } })).status, 403);
});

test('missing corrupt and unavailable pool metadata keep the trusted category and readable fallback', async () => {
  for (const mode of ['missing', 'corrupt', 'unavailable', 'poisoned']) {
    const db = storage(), file = db.put(poolPath, new Uint8Array([4, 2]));
    const hash = Buffer.from(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(file.id))).toString('hex');
    const path = '/.campaign-metadata/' + hash + '.json';
    if (mode !== 'missing') db.put(path, new TextEncoder().encode(mode === 'corrupt' ? '{' : JSON.stringify({ v: 1, id: file.id,
      metadata: { displayName: 'Visible pools', kind: 'campaign', path: '/other.zip', managementCode: 'private-value' } })));
    if (mode === 'unavailable') db.failMetadataRead();
    const listed = await ok('/list?kind=base-game-pools', { method: 'GET' });
    assert.equal(listed.count, 1, mode); const item = listed.items[0];
    assert.equal(item.kind, 'base-game-pools'); assert.equal(item.id, file.id); assert.equal(item.path, poolPath);
    assert.equal(item.name, mode === 'poisoned' ? 'Visible pools' : 'All three pools');
    assert.ok(!JSON.stringify(item).includes('private-value'));
    assert.equal((await ok('/list', { method: 'GET' })).count, 0);
    const download = await call('/download?path=' + encodeURIComponent(poolPath), { method: 'GET' });
    assert.equal(download.status, 200); assert.deepEqual(new Uint8Array(await download.arrayBuffer()), file.bytes);
  }
});

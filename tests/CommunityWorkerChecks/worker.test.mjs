import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { webcrypto } from 'node:crypto';

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
  assert.equal((await call('/upload/start', { body: { name: 'too big', size: 512 * 1024 * 1024 + 1 } })).status, 413);
  assert.equal((await call('/upload/finish', { ticket: upload.ticket })).status, 200); assert.equal(db.files.size, 1);
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

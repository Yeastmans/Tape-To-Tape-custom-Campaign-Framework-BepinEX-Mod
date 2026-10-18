// Manual administrator tool. Never package this with Studio or campaign ZIPs.
import { access, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

export async function issueLegacyCode({ workerUrl, campaignPath, adminSecret, outputPath }, request = fetch) {
  const server = new URL(workerUrl);
  if (server.protocol !== 'https:' || server.username || server.password || server.search || server.hash || server.pathname !== '/')
    throw new Error('Use the HTTPS Worker origin with no credentials, path, query or fragment.');
  if (typeof campaignPath !== 'string' || !/^\/[^/\\\x00-\x1f]+\.zip$/i.test(campaignPath))
    throw new Error('Select one legacy campaign ZIP in the public root.');
  if (typeof adminSecret !== 'string' || !adminSecret) throw new Error('CAMPAIGN_ADMIN_SECRET must be set for this administrator session.');
  if (!outputPath) throw new Error('Choose a private output file outside campaign and export folders.');
  try { await access(outputPath); throw new Error('Output file already exists; choose a new private file.'); }
  catch (e) { if (e.code !== 'ENOENT') throw e; }
  let response;
  try { response = await request(server.origin + '/manage/claim', { method: 'POST', redirect: 'error',
    headers: { 'Content-Type': 'application/json', 'X-Campaign-Admin': adminSecret }, body: JSON.stringify({ path: campaignPath }) }); }
  catch { throw new Error('Assignment request failed. No private code was printed.'); }
  if (!response.ok) throw new Error('Assignment was not accepted (HTTP ' + response.status + '). Check administrator access and the selected public ZIP.');
  const value = await response.json();
  if (!value.ok || typeof value.managementCode !== 'string' || !value.managementCode.startsWith('cm1.') ||
      typeof value.id !== 'string' || !value.id.startsWith('id:') || value.path?.toLowerCase() !== campaignPath.toLowerCase())
    throw new Error('The server returned an unexpected assignment response.');
  await writeFile(outputPath, JSON.stringify({ managementCode: value.managementCode, id: value.id, path: value.path, name: value.name }, null, 2) + '\n',
    { flag: 'wx', mode: 0o600 });
  return { path: value.path, outputPath: resolve(outputPath) };
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  try {
    const args = process.argv.slice(2), options = {};
    for (let i = 0; i < args.length; i += 2) {
      if (!['--worker', '--path', '--output'].includes(args[i]) || !args[i + 1]) throw new Error('Usage: node assign-legacy-code.mjs --worker https://WORKER --path "/Legacy.zip" --output PRIVATE_FILE.json');
      options[args[i]] = args[i + 1];
    }
    const result = await issueLegacyCode({ workerUrl: options['--worker'], campaignPath: options['--path'], outputPath: options['--output'], adminSecret: process.env.CAMPAIGN_ADMIN_SECRET });
    console.log('Private management code saved to: ' + result.outputPath);
  } catch (e) { console.error(e.message); process.exitCode = 1; }
}

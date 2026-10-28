# Revision 31 GM recruitment and free-agent names

Release scope is limited to the GM placement gate, small authored-pool handling, free-agent name collision scope, guidance and regression sources. Unrelated Linux/Proton and cover-editing work is excluded.

## Automated checks

- `dotnet run --project tests/TwoLineContractChecks -c Release`: 1,960 source-linked recruitment checks covering all ten destinations, contracts on/off, cancellation, stale callbacks, rollback, single/empty pools, native eligibility and signing limits.
- `dotnet run --project tests/FreeAgentNameChecks -c Release -- <fixture-output>`: 17 real-file import, rename, selection and identity checks. This standalone runner uses the same tested regression body as the local Studio harness.
- Inspect the fresh published Studio and extracted installer: embedded/staged runtime equality, approved assets, pinned loader files, paired update manifest and SHA-256 digests.

## Observed native behavior

In a disposable campaign on Steam build 26188487, with two solid starters, eight scrubs, two full lines and contracts off: Brent Burns was signed into Line 2 LW, Cale Makar into Line 1 LW, and Nathan MacKinnon into Line 1 RW at a second GM. The first node retained its singleton offer after cancellation. All three names, player identities, line assignments, stats and talents survived closing the game and Continue, with no limited contracts. The resumed Run Stats screen was inspected. Native logs contained no errors after the repair. The test used the same changed feature sources in a local candidate; the release is rebuilt on the published Windows baseline.

The immediate snapshot after the first hire preceded the next save, so persisted line assertions use the completed-node snapshot. Six inherited skate appearance fields normalized to team defaults on reload; no complete byte-for-byte player-record claim is made. All 1,852 original native files, active campaign and original installation were restored after testing.

The original reporter's campaign was unavailable. Existing saved-run names are not rewritten. Own-squad rename IDs are file-based; shared-pool rename IDs keep their pre-existing name-based behavior. Installer extraction is payload verification, not an elevated installation claim.

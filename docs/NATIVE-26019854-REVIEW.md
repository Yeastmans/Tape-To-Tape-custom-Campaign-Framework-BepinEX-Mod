# Native compatibility audit for Steam build 26019854

The reviewed source update, combined runtime and minimal Studio compatibility
package pass the technical checks below. The strict verification rules remain
unchanged. Native gameplay, event delivery and performance need human validation
on the installed build; this audit does not establish those results.

## Exact inputs and repairs

- GameAssembly SHA-256: `64E1F43F4E83CCBAE5F02CC301C6018E2756D1B5D2E0C337A382F06AF3D980FB`.
- Metadata SHA-256: `60FDE2CF9C40F9E5F0E2AA2066089ADD62F90C520DE493838BFF92CBD99455AB`.
- Previous reviewed build: 26019242, GameAssembly `3E34D99C3DAE9F7F3B38A4BA52FD78D2F4FCD0217B7B6E87C94BFD0DCAD5AEA4`.
- Reviewed baseline ID: `C12E5856689533EFB87EB38E2E809869356ED7F42C21154D2DE7E811ED956B1B`.
- Combined runtime: `DF2C26165605BB4B345520C50BE371A5C6AABDEEF02BC140D5584562BAD33FA2`.

The old baseline correctly rejects BreakoutWithoutPuckState.OnFixedUpdate.
The new baseline was generated only after the independent method and raw-entry
reviews, then independently verified. Its profile and 142-site manifest were
promoted together after all previous source hashes matched retained snapshots.
Five raw reward bridges now pin the exact current game hash. GoalScoredArg's
constructor moved from `0x36004D0` to `0x3600B00`; ten generic entry addresses
are unchanged. All prologue guards, MethodInfo checks and callback logic remain.
The starting-flow location is `0x3B91D02`; the surface entry is `0x39A9350`.

## Audit coverage

| Area | Result |
| --- | --- |
| Protected contracts | All 269 signatures match; 237 body hashes match, 32 differences reviewed |
| Broader hooks | 508 references resolve to 450 native targets; all signatures match, 31 body differences reviewed |
| Combined native bodies | 583 unique bodies; 537 exact canonical matches, 46 reviewed differences |
| Control flow | All 16,339 local branch targets preserve their instruction indices |
| Squad routing | All 142 sites preserve owner/query/kind and instruction index; 110-call inventory unchanged |
| Raw reward entries | All 11 signatures, calling conventions, prefixes and normalized bodies match |
| Generic method families | All 248 MethodInfo identities retained; nine runtime helpers unchanged after relocation normalization |
| Native payloads | All 13 inspected event/value layouts unchanged |
| Dynamic targets and interop | 12 overloads, unique reward-weight closure, 69 export properties and ten native fields verified |
| Final compiled patches | 233 Harmony classes, 317 explicit target references, 531 callback bindings; zero issues |

Counts overlap and must not be added. The protected set retains the existing
qualified/unqualified duplicate starting-flow record. Broader source inspection
includes explicit/dynamic installs, attribute patches, display getters, opponent
consumers and reflected lookup sites. Two known Relic display getter aliases
remain safe; there are no unexpected shared addresses, unresolved targets or
overlapping detours/routed-site prologues.

Forty-four of the 46 changed body hashes differ only in runtime RIP-relative
displacement fingerprints. Seventeen fingerprint pairs across 109 occurrences
were checked byte by byte. Two remaining changes are literal labels colliding
with native initialization-byte addresses in BreakoutWithoutPuckState.OnFixedUpdate
and SkaterCustomizationAbilityList.Init. Their compare/write operations, branch
targets and all remaining instructions are unchanged. No native semantic change
was found in this reviewed scope. Production normalization was not relaxed.

## Combined runtime and Studio package

The NHL reward chat performed the single combined runtime build after source
clearance. All 190 source hashes match its recorded inputs. The final compiled
metadata audit uses the freshly regenerated current interop assemblies; all eight
input hashes remain stable. Build result: zero errors and one existing unused
exception-variable warning. The coordinated neutral reward asset creation and
GameStats.OnHit bridge are included. Current saves are not altered by these changes.

The minimal Studio overlay contains exactly five files: ModCompatibility.dll and
the runtime DLL, runtime hash, native baseline and replacement allowlist under
mod-runtime. It preserves the published Studio UI, executable, settings and upload
flow. The new Dropbox management UI remains a separate candidate awaiting worker
deployment. The installed CampaignCore and parser dependencies are retained.
See `studio-api-review/` for the exact binary API comparison and dependency checks.

All 53 compatibility checks pass, including wrong identities, altered native
contracts, the actual old baseline and missing/broken package rejection. Eight
updater integration checks pass with copied game binaries and the exact installed
Studio dependencies: known revision-10 upgrade, recoverable backup, matching
profile, idempotence, unknown-runtime preservation and unrelated-file preservation.
The check also reproduces the previous replacement-list refusal before applying
the already-reviewed revision-10 hash entry. No live installation occurs in this
fixture; its cleanup is restricted to its unique evidence directory.

## Evidence and remaining acceptance

Evidence root: `artifacts/game-update-26019854-20261017`.

- `method-review/REVIEW.md` and detailed JSON tables: native bodies, branches and sites.
- `reward-review/REVIEW.md`: all raw entries, native ABI, MethodInfo families and helpers.
- `coverage-review/`: final interop metadata, compiled callback bindings and source inventory.
- `source-promotion.json`: exact before/after hashes for the eight promoted source files.
- `candidate-verification.log`, `compatibility-checks.log`, `updater-check.log`: passing checks.
- `studio-api-review/`, `minimal-package-inputs.json`: overlay API and dependency evidence.
- `FINAL-RECEIPT.json`: hashes and verification results for the delivered candidate.

The NHL chat owns the coordinated installation and records its own backup and
preservation receipt. Neither compilation nor static/native audits prove managed
Harmony dispatch, visuals, frame rate or every reward's in-game effect. Human
acceptance should check starting/continuing the intended campaign, roster and
preview accuracy, custom reward names/icons and effects, check-trigger behavior,
match completion and return to the map, and any reported performance issue.
The existing current save remains untouched. Publication and Dropbox worker
deployment are separate from this compatibility update.

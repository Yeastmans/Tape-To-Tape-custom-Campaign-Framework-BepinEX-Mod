# Native compatibility and callback ABI audit for Steam build 26020247

Revision 13 includes this audited runtime and the coordinated crash repair.
The technical checks below pass; human confirmation of the Winnipeg transition
and gameplay remains pending. Revision 12 predates this repair.

## Crash finding

The preserved fatal log enters `System.Buffer.Memmove` from the managed
`ForwardStats.GetOverall` trampoline while `GetChecking` initializes a team.
Both the prior 26019854 and current 26020247 native signatures pass
`Nullable<BasicStats>` as an eight-byte value. The caller loads that value into
RDX; the callee reads its hasValue byte and four-byte value directly. Its ABI and
instruction logic did not change in this update.

The bundled HarmonySupport conversion path treats the generated Nullable reference
wrapper as a pointer and copies from that address. Its x64 `Ldarg` and subsequent
`Buffer.MemoryCopy` explain the observed fatal stack. The coordinated repair removes
the `GetOverall` interception while retaining scalar caller scopes for random-shot
rules. Separate repairs guard recovery queries for teamless actors and construct an
empty native Nullable wrapper for boosts.

The earlier callback audit compared managed wrapper signatures and missed this
native ABI hazard. The expanded audit now classifies the complete native argument
and return signatures, including parameters not used by a patch callback. The
preserved before-fix inventory is detected as unsafe by that audit.

## Exact inputs and candidate

- Steam branch/build: `roguelite`, **26020247**.
- GameAssembly SHA-256: `3E8BF7879BF160C0DBCDDE2E12069E1E12689D3D8B327BFAE165DCE607FE294E`.
- Metadata SHA-256: `9C2B05393B2B0E603F7F55751AA3638937F31F1D4A21D0AFE36FB5328F7A979A`.
- Native baseline ID: `7C18016D8A4FC55279AC44FF0F0836F5753D9428E15B1003D90D80818FBB62F6`.
- Combined runtime SHA-256: `CAA2E980E2750509D210FAE8A85DB6E136F60D4917BA452651E450E6D99CF658`.
- Profile SHA-256: `80F841051055E3BE22C7037C794659306365B8013C195281EE94E5815F16AD0C`.

Exact previous and current native images were retained. Current interop references
were generated into an isolated artifact directory using copied installed Cpp2IL,
InteropGenerator and Unity libraries, with the same AttributeInjector/AsmResolver
pipeline as the installed loader. All 105 generator inputs and 223 output files
were hashed. No live interop directory or game process was used for generation.
The missing live interop directory was observed after the revision 12 installation;
the crash-session interop binaries were therefore unavailable for a fresh hash check.

## Coverage and repairs

| Area | Verified result |
| --- | --- |
| Protected contracts | 269 signatures; 235 exact body hashes, 34 reviewed differences |
| Broader historical hook union | 450 signatures; 418 exact body hashes, 32 reviewed differences |
| Combined bodies | 583 signatures; 535 exact, 47 relocated runtime fingerprints, one byte-flag lookup label collision |
| Control flow | 16,339 local branch destinations preserve instruction indices |
| Squad routing | 142 sites and 110 native queries; owner/query/kind and instruction indices unchanged |
| Raw reward ABI | 11 entry bodies, signatures and guarded prefixes; 248 MethodInfo identities; 13 full helper bodies and 14 payload layouts compatible |
| Current post-fix hooks | 507 source references resolve to 449 native targets; no unresolved targets or unexpected aliases |
| Aggregate ABI | 35 target signatures classified; no active unsafe small reference-wrapper argument/return |
| Dynamic and reflected access | 13 event/value layouts, 12 overloads, unique reward-weight closure, 69 properties and ten fields verified |
| Final compiled callbacks | 232 Harmony classes, 316 target references, 530 callback bindings; zero binding issues |

Counts overlap and must not be added. The historical method union intentionally
retains GetOverall for before/after evidence; its unsafe managed interception is
absent from the final DLL. The compiled embedded profile equals the source and
candidate baseline profile exactly. All 187 build source inputs remain unchanged.

The aggregate audit checks fresh native declarations against 16,554 generated
types across eleven interop assemblies. Active small aggregates use actual CLR
value types. The eight-byte CancellationToken wrapper on the world ApplySkinAsync
overload remains excluded by all three existing SkaterData filters. Other generated
wrappers are 16/24/40/56 bytes and use the Win64 indirect argument/return-buffer path.

Ten generic raw entry addresses moved by `+0x1000`; the goal constructor moved
`0x3600B00 -> 0x3607960`. All five exact game hash pins were updated only after
their body, helper, MethodInfo and ABI review. Starting flow is now `0x3B98B62`
at instruction index 770; surface entry is `0x39B01B0`. Existing prefix checks,
callback logic, MethodInfo guards and strict compatibility normalization remain.

## Verification and installation boundary

The old baseline rejects the new binary without producing a profile. The new
baseline passes strict verification and all **53 compatibility checks**. Eight
isolated updater checks demonstrate rejection with the old replacement list,
upgrade from the exact published revision 11/12 runtime, recoverable backup,
matching profile, idempotence and preservation of unknown runtimes/unrelated files.
The known published runtime hash was added to the replacement allowlist.

The single coordinated runtime build succeeded with zero errors and one existing
unused-variable warning. Its source and interop inputs were reverified after
compilation. The minimal Studio overlay contains only ModCompatibility.dll and
four mod-runtime files. ModCompatibility.dll is byte-identical to the installed
revision 12 copy; Studio UI, executable, CampaignCore and build identity remain.

Evidence root: `artifacts/game-update-26020247-20261017`.

- `method-review/REVIEW.md`: native bodies, branches, sites and targeted fatal/recovery disassembly.
- `reward-review/REVIEW.md`: raw entries, helpers, MethodInfo and layout comparisons.
- `coverage-review/FATAL-GETOVERALL.md`, `REVIEW.md`, `FINAL-RECEIPT.json`: root-cause evidence, aggregate ABI and final compiled callback checks.
- `interop-generation/receipt.json`: exact isolated generator provenance.
- `source-promotion.json`: eight reviewed native source changes; old replacement list retained under source-before.
- `candidate-verification.log`, `compatibility-checks.log`, `updater-check-receipt.json`: verification outcomes.
- `minimal-package-inputs.json`: overlay and preserved Studio component hashes.
- `FINAL-RECEIPT.json`: consolidated technical handoff.

Crash-fix regressions and the installation receipt belong to
`artifacts/unexpected-parameters-20261017`. No current run should be restored or
reset for this repair. Human acceptance must repeat entry into Winnipeg from the
existing run, then check match completion and return to the map. Recovery and boost
effects also need observed gameplay. Static ABI review, compilation and copied-file
update checks do not establish performance or every reward's runtime behavior.

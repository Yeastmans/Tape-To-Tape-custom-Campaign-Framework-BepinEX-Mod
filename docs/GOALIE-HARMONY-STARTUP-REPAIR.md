# Goalie finalizer patch-generation repair

The E2D startup repair exposed a second independent activation failure in the shipped HarmonyX provider. This change is ready for integration with the separate native match-loading repair. No final combined package is identified by this report.

## Proven cause

The preserved `startup-installed-E2D-LogOutput.log` shows `Parameter __runOriginal does not contain a valid index` from `HarmonyManipulator.WriteFinalizers`, followed by rollback failure. Decompiling the exact installed `BepInEx/core/0Harmony.dll` establishes that `WritePrefixes` allocates and initializes the `__runOriginal` local whenever prefixes exist; `WritePostfixes` also creates it when necessary. `WriteFinalizers` consumes existing variables but never creates that local itself. The finalizer parameter is therefore supported when a prefix/postfix exists; it fails for a finalizer-only patch plan.

Six completion targets had finalizers without any prefix or postfix: normal faceoff, puck drop, shootout play, normal restart, shootout restart and end-of-game flow initialization. The other eight completion targets already had prefixes and compiled successfully.

## Change and preserved behavior

The six entries now explicitly include `ExecutionPrefix`, which returns true. Shipped HarmonyX combines bool-prefix results using AND, so this cannot revive a body skipped by another prefix. Existing finalizers, exception checks, completion provenance, identity checks, contract retention and cleanup are unchanged. No hook is disabled and no exception is suppressed.

The approved managed contracts encode these six prefix names. The compatibility baseline is rehashed to `89C2BF9BC714D598CF5A82FB112D34D191C7C4DE66F03ECB69CE5DF5D2E13627`. A structural equality check proves the six Prefix values and derived IDs are the only semantic compatibility changes: native addresses, hashes, bodies, signatures, target counts and inventories remain unchanged. The installed E2D runtime is added to the predecessor allowlist.

## Actual-provider regression

`tests/GoalieHarmonyChecks` targets .NET 6 and uses the installed HarmonyX, MonoMod and Cecil binaries. It verifies their loaded SHA-256 values against the installation. It asks each of the four production goalie components for its actual Resolve plan, runs the same `HarmonyManipulator` entry point used by the IL2CPP provider against the actual installed wrapper method bodies, and generates the resulting managed methods. It never executes a native wrapper or installs any detour.

The first run against E2D reproduces all six exact compiler failures; the remaining 29 goalie plans generate. The corrected production DLL passes 54 assertions: three provider identities, approved embedded completion-plan equality, all 35 production patch generations, and 15 managed execution observations for normal return, body exception, earlier/later foreign skips and a throwing prefix. Those execution probes call the production pass-through prefix and a test finalizer that records the provider's actual `__runOriginal` and exception. The original exception is retained and a foreign skip remains false. The existing source-linked completion tests continue to cover production finalizer decisions, including failed initialization/play/update and foreign departure skips.

A separate integration-red run proves strict contract matching rejects the new callbacks while the DLL still contains the old embedded contract metadata. The integrated diagnostic passes. Related checks pass: wrapper startup 14, activation 19 plus coordinator 82, completion 404, goalie menu 130, and compatibility 131. The only runtime build warning is the pre-existing unused `ex` variable. These checks prove managed patch generation and relevant managed decisions, not IL2CPP trampoline correctness or human gameplay success.

## Evidence and reproduction

Evidence directory: `C:/Users/kiran/Desktop/tape to tape mods/artifacts/feature-test-crash-20261022/`.

- `shipped-HarmonyManipulator.cs` and `shipped-PatchInfo.cs`: decompiled installed provider.
- `harmony-generation-red.log`: six reproduced compiler failures plus the then-missing production-prefix probe.
- `harmony-contract-integration-red.log`: callback/embedded-contract mismatch rejection.
- `harmony-generation-integrated-green.log`: all 54 assertions passing.
- `harmony-contract-refresh.log`: exact six-prefix/derived-ID-only metadata assertion.
- `harmony-GoalieActivationChecks.log`, `harmony-GoalieRosterRuntimeChecks.log`, `harmony-GoalieRosterMenuChecks.log`, `harmony-wrapper-startup.log`, `harmony-compatibility.log`: regression evidence.

From the isolated checkout, replace the runtime path below with the final candidate after the native repair is integrated:

```powershell
& "$env:ProgramFiles/dotnet/dotnet.exe" run --project tests/GoalieHarmonyChecks -- build/FeatureTestHarmonyRepair26141904/diagnostic-integrated/CustomCampaignFramework.dll 'C:/Steam/steamapps/common/Tape to Tape'
```

Human validation remains pending. On the final combined candidate, verify the startup receipt says all 35 goalie targets installed, without an activation or rollback exception. Use the prepared Goalies GM node to inspect offers, recruit, inspect Active/Reserve, save/Continue, and report the result with fresh logs. Match loading and the native access violation require the separate native fix and its own verification.

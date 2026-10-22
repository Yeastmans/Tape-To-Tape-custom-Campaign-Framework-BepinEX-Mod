# Feature-test startup repair for game 26141904

This isolated candidate repairs two reproduced startup problems reported on 22 October 2026. It does not establish a fix for the separate native access violation during match loading (`Exception Processing Message 0xc0000005 - Unexpected parameters`). The native compatibility profile, protected targets, and match adapters are unchanged.

## Root causes and changes

- The installed generated ES3File wrapper declares both `Il2CppSystem.Object Load(System.String)` and `T Load<T>(System.String)`. `AccessTools.Method` with only argument types throws AmbiguousMatchException before goalie lifecycle installation. The checkpoint adapter calls the non-generic overload. Preflight now requires exactly one declared, non-generic method with the expected static/instance flag, exact return type and exact parameter types. Missing, changed or ambiguous contracts still throw and leave activation disabled.
- `Plugin.LoadPlayerTeamsFolders` previously fell through from `player_teams/goalie_free_agents` into custom-squad loading. It now reserves that directory using the shared goalie-pool name and ordinal case-insensitive comparison. Actual goalie recruitment selection and skater free-agent loading are unchanged.
- Studio's predecessor allowlist adds the installed `582617F45530CB9937E6A72117F22D8E76AFE6446AF91CDBACA615A9EC6042B1` runtime. Existing entries remain.

## Verification and limitations

The new regression ran first against the unchanged candidate and reproduced both failures. The final compiled candidate passes 14 checks: full production wrapper preflight against the actual installed interop, exact-overload selection, negative generic-only/staticness/return/parameter/argument-count/inheritance/ambiguity cases, and the actual extracted folder-loader body with real campaign pool files. No game/native method is invoked by this reflection check. Loader configuration/native object boundaries are doubles; folder enumeration and goalie file validation use production code.

Related checks pass: activation 19 plus coordinator 82, goalie recruitment 40, goalie roster/store 92, goalie completion 404, goalie menu 130, jersey runtime 33, Play Now persistence 30 and Play Now roster runtime 24. The first menu build identified a missing source-link for the new resolver; both source-linked test projects now include it and the menu checks pass. Runtime compilation succeeds with the existing unused `ex` warning at Plugin.cs:8432. Studio self-contained publish and checks of the actual published game-art package pass. The native compatibility checks pass 131 cases against build 26141904, including rejection of the previous baseline. An initial compatibility invocation used a nonexistent old-baseline filename; the corrected invocation and its result are preserved separately.

These are managed, reflection, package and compatibility diagnostics. They do not establish actual goalie lifecycle activation, offers, persistence, match completion, or resolution of the separate native access violation. Human validation remains pending. This implementation did not install files, launch the game, modify real saves, publish a release, or promote canonical source.

## Candidate and evidence

Candidate: `build/FeatureTestRepair26141904/runtime` and `build/FeatureTestRepair26141904/studio-publish` in this isolated checkout. Runtime SHA-256: `E2D73CF04BDD1F6DE2A9D2E6429924637021FDE66122A763F6BEC8D8A9B3DF50`.

Evidence: `C:/Users/kiran/Desktop/tape to tape mods/artifacts/feature-test-crash-20261022/`. The preserved user logs are `LogOutput.log` and `Player.log`. Regression evidence includes `startup-red.log`, `startup-final-green.log`, the named regression logs, `final-runtime-build.log`, `final-studio-publish.log`, `published-package-checks.log`, and `compatibility-checks-green.log`. `STARTUP-REPAIR-RECEIPT.json` records final source/payload/input identities.

Run the actual-interop check from the checkout with the .NET executable:

```powershell
& "$env:ProgramFiles/dotnet/dotnet.exe" run --project tests/GoalieStartupChecks -- build/FeatureTestRepair26141904/runtime/CustomCampaignFramework.dll 'C:/Steam/steamapps/common/Tape to Tape/BepInEx/interop' 'C:/Users/kiran/Desktop/tape to tape mods/artifacts/feature-test-crash-20261022/startup-fixtures'
```

## Pending human checks

After the parent task has completed its separate crash investigation and authorizes using this exact candidate, use the already prepared feature-test campaign with recruitment enabled, reserves enabled, a Goalies GM node and two valid authored goalie files. Confirm the startup log has an installed goalie lifecycle receipt and no ambiguous wrapper exception. Confirm the goalie-pool directory is absent from the custom squad list and no longer produces the five-skaters warning. Enter the authored Goalies GM node and confirm offers appear from the two goalie files. Choose one, inspect Active/Reserve, then save/Continue and inspect it again. Check ordinary custom squads and skater recruitment still work. Report the observed result and any fresh logs; a match-loading access violation remains a separate unresolved failure until independently established otherwise.

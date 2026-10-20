# Task4b — recoverable Play Now persistence

Implementation based on reviewed Task4a commit 08ef91e. Work stayed in the isolated publish-checkout. No game launch, live-save write, installation, push, release or canonical source synchronization. The controller authorized the additional narrow GameCustomContent reimport consumer correction. This report covers implementation and managed/disk verification; independent review and final native compatibility integration remain required.

## Result and authority

Added PlayNowSaveJournal, PlayNowEditAuthority, PlayNowRosterSave and PlayNowPlayerSave. Explicit native player Save and team Save remain independent transactions. Team Save writes only its native team/companion pair, normalizes native fallback to absolute LW,RW,C,LD,RD Line1 plus goalie regardless of CurrentLine, and persists both absolute lines/stamina in the companion. It never serializes all eleven painted player objects. A separately committed player edit survives later team cancellation. Missing custom replacement records reject before publication; existing custom records are validated by stable role-qualified identity, and built-ins must resolve uniquely through the native role repository. Replacement IDs retain their own metadata; old records are preserved.

Player authority is established from an exact native/companion pair accepted at raw load, then associated with the precise player instance and explicit modern/legacy editor session. Expected fingerprints are retained across editor entry and checked again at writer publication. A Studio update or separate native edit cannot be legitimized by rereading hashes immediately before saving. Team raw models likewise retain their original immutable bytes/fingerprints before translation can alias and mutate their palettes. Strong object references plus instance/staged-owner checks prevent pointer reuse or delayed callbacks from borrowing a new owner's intent. Warm-cache traversal does not replace the original revision. Nested unrelated saves mask an outer transaction and finalizers restore it.

Native editor constructors/reset paths copy effective painted fields. Therefore appearance ownership cannot be inferred solely from final writer values or compare-only deltas. Confirmed head/stick handlers record deliberate choices, including choices equal to the painted entry value. Confirmed personal color Item/Index records exact float RGBA plus conventional authoring RGB. Preview and Back-from-color-panel callbacks do not record intent; Hide retires the session. No/Cancel never writes the pair. Reviewed legacy next/previous face/stick handlers provide the same authority. Other writer skin fields are restored from the previously committed personal model; native final names, stats, ability/talents (including empty selections) are retained. Native-omitted fields, pass-read, personal color data, independently authored away equipment and explicit own/inherit flags survive. Newer native files also reconcile the load document, preventing old Export Model/reward replay.

Team-owned exact home/away forward and goalie palettes are saved independently in the companion; raw native palette/relic/logo state wins over stale authored replay for representable fields. Successful commits invalidate relevant document/native model caches, and explicit player Save refreshes exact persistent references on either line without changing generated match-clone identities.

GameCustomContent now merges current native fields and live supplemental documents when reimporting team and standalone exports, preserves omitted fields and ownership, and uses correct LW,RW,C position order. Historical library snapshots are not rewritten by reimport. Built-in replacement documents come from the same verified Base Game Players catalog used by ExportPlayerResolver, with exact VanillaCopyData.PlayerId and role matching; missing/ambiguous records reject incomplete imports. A pending runtime journal rejects reimport until runtime recovery, so Studio does not consume an interrupted pair.

## Journal and recovery

Each journal contains prior/new bytes and SHA256 fingerprints, stable role/ID, exact validated destination paths, UUID transaction and incremented companion generation. A shared exclusive lock serializes these runtime writers. Files are flushed to unique staging files then atomically renamed. Prepared means roll forward both files. A caught failure first durably changes the decision to rollback, then restores both exact previous files. Load/Preload hooks recover before native cache population. Unknown third-party bytes are preserved and recovery refuses to overwrite them; the journal remains for diagnosis. Reference checks precede team publication. Linked save paths and unsafe IDs are rejected.

This is process-interruption recovery, not a claim of filesystem power-loss guarantees or an OS compare-and-swap against external programs. Fingerprints are rechecked at durable boundaries; an external writer that ignores the runtime lock can still race between a check and rename. The implementation rejects observed competing revisions rather than guessing ownership.

## Native evidence and dependencies

Read canonical native-persistence-research.md and play-now-editor-save-research.md. New review: `C:/Users/kiran/Desktop/tape to tape mods/artifacts/followup-four-features-20261020/task4b-native-review.md`. Exact signatures, RVAs and aliases: adjacent `task4b-native-evidence/index.json`; 88 captured method entries. Capture command: `python C:/Users/kiran/Desktop/tape to tape mods/artifacts/followup-four-features-20261020/task4b-capture.py` (quote the path in PowerShell). The script pins GameAssembly SHA256 92E73E6D0BE549DD8C695302B7AAE6A77CBBF4DE27F27D26ED01F4B3DABC5B8D and metadata SHA256 1009F63178E260153CBBF224338BDC9124F9468D0F462F7A58327F638188914A and uses the fresh dump index.

New hook families: outer Team/Forward/GoalieRepository Save; concrete persistent writers; all three concrete LoadCustom/Preload methods; modern CustomizeForwardData/CustomizeGoalieData constructors; legacy LoadForward/LoadGoalie; confirmed legacy face/stick navigation; modern OnPanelHeadClick/OnPanelStickClick/OnColorPicked and Hide. Existing role DeserializeAssetReferences hooks now consume concrete repository roots and raw model revision evidence. The native review lists each target and exact signature/RVA. Setter hooks were rejected because confirmed handlers inline assignments and several setters alias unrelated bodies.

UnityEngine.JSONSerializeModule is referenced from fresh interop with Private=false. Deployment remains a single runtime DLL. No native hash/profile/bridge gate was bypassed or approved; final integration must include these new contracts before installation. Constructor interception and all native callback dispatch still need native verification.

## Commands and observed results

All commands ran in this checkout with `C:/Program Files/dotnet/dotnet.exe` (PowerShell invocation `&` and quoted executable path).

| Command arguments | Observed final result |
|---|---|
| run --project tests/PlayNowPersistenceChecks/PlayNowPersistenceChecks.csproj -c Release | 27 passed, 0 failed |
| run --project tests/PlayNowUpdateChecks/PlayNowUpdateChecks.csproj -c Release | 54 checks passed; existing TeamEditorLogos CS8619 warnings |
| run --project tests/PlayNowRosterChecks/PlayNowRosterChecks.csproj -c Release | 33 assertions, 0 failed cases |
| run --project tests/PlayNowRosterRuntimeChecks/PlayNowRosterRuntimeChecks.csproj -c Release | 23 passed, 0 failed |
| run --project tests/TwoLineContractChecks/TwoLineContractChecks.csproj -c Release | 1063 checks passed |
| run --project tests/ExportTeamAppearanceChecks/ExportTeamAppearanceChecks.csproj -c Release | 31 checks passed |
| run --project tests/AppearanceTranslationChecks/AppearanceTranslationChecks.csproj -c Release | Existing failure at Program.cs:17: Current complete asset rejected: Faces/Custom/Helmet_Reference. Not weakened or called passing. |

Production command:

```powershell
& 'C:/Program Files/dotnet/dotnet.exe' build src/CustomCampaignFramework/CustomCampaignFramework.csproj -c Release '-p:InteropLive=C:/Users/kiran/Desktop/tape to tape mods/artifacts/game-update-26085704-20261020/interop-generation/interop' '-p:InteropDir=C:/Users/kiran/Desktop/tape to tape mods/artifacts/game-update-26085704-20261020/interop-generation/interop' --no-restore
```

Result: 0 errors, one existing Plugin.cs:8430 CS0168 warning. Candidate SHA256 A542395FCF5AB018DDD70FFCA678048AC2A18A2989BB8FD6470BB71BFE9AAC38, local `src/CustomCampaignFramework/bin/Release/net6.0/CustomCampaignFramework.dll`. This is not an installed/release receipt.

Red/green evidence: initial NativeSavePersistsEditedBothLines and SaveFailureRecoversPreviousPair failed with recoverable writer absent before implementation. Added reimport order test then observed the existing 48 checks pass followed by `Reimport maps native LW,RW,C ordering exactly` failing; consumer correction produced 53/53, then pending-journal rejection produced 54/54. Runtime old doubles initially failed to compile after source integration; adding only the explicit persistence boundary stub/import and TryCast support restored all 23 existing tests. New native capture helper expansion briefly failed due shell interpolation/regex syntax, was corrected, and final capture returned 88.

## What the tests establish

The disk tests exercise actual journal source in temporary directories: caught failures at prepared/native/companion/verified boundaries restore exact prior bytes; four separate child-process exits (88) at those boundaries finish the prepared pair after reopening; a separate child-process exit (89) after durable rollback decision restores exact previous bytes. Same-native Line2-only edits survive restart. Stale revisions, inconsistent native fallback, missing references, and unknown native/companion third-party writes are rejected/preserved. Recovery is idempotent.

The integration fixtures source-link actual roster runtime, save hooks, edit authority and journal. Native-shaped doubles invoke the real prefixes/writers/finalizers against fixture files. They cover active-Line2 normalization/reload, second-line player save, equal painted head choice, explicit reward clears, omitted/personal fields, no/cancel, exact personal color channels, team palettes/relic clears without any player rewrite, stale player session, replacement provenance, aliased raw-model palette lifetime, warm-cache stale team rejection, newer-native load reconciliation, nested unrelated saves, goalie schema/skin/stat authority and session removal from roster not escaping revision checks. Goalie doubles model a distinct schema; they do not emulate every native property. Existing runtime tests stub persistence and cover unchanged context/stamina mechanics. Studio checks invoke actual GameCustomContent through the real Creator assembly.

The new fixture stubs full PlayNowTranslation and Unity/IL2CPP/Harmony. Thus its checks prove managed transaction/authority behavior, not real native interception, resource/reward resolution, every UI callback sequence, renderer behavior, Unity lifetime or native cache behavior. Full original Task4 gameplay acceptance remains human/native work: both sides/both lines, same team twice, input/faceoff switching, stamina/HUD, explicit editor save/cancel, real restart and reimport. In particular existing PlayerExport still deliberately emits empty goalie headSkin on a later export; this task preserves the saved native head and imported Face metadata but does not redesign that separate exporter convention.

## Self-review and handoff

Reviewed changed production consumers and new persistence sources; fixed native position ordering, raw model aliasing, stale-session fingerprint lifetime, nested unrelated masking, competing companion preservation and post-membership-change session escape. Reviewed exact staged files and diff whitespace. No generated bin/obj, fixtures or native-input binaries are intended for commit. Native review artifacts remain in the permitted canonical evidence folder; source/tests/report are committed in the isolated checkout. Commit identity is reported in the agent handoff (avoids a self-referential hash in this file).

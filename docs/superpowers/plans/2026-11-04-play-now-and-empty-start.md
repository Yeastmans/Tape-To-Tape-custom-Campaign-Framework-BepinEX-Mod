# Play Now and recruit-first follow-up

R40 was published and anonymously downloaded/verified at commit `12197ab4936c09c35eba4f91acce094cc1f4e87c`, tag `beta-1.0-r40-build-1793838044980`. This follow-up preserves that release and all saved content.

## Confirmed requests

- Allow Breakaway HC Signature Series to start empty and recruit its roster. The user explicitly asked to change the five-starter rule. Preserve the check for partially configured first lines; do not add donor players.
- Check compatibility: installed Steam build 26372689, game/metadata hashes and R40 runtime all matched; all 1,507 protected methods and 137 comparison sites verified.
- Goal audio: the user confirmed playback in campaign and Play Now with a campaign active, then specifically asked about Base game selected. R40's default-mode early return skips the four audio hooks. Install those hooks only in default mode; retain existing campaign PatchAll.
- Play Now export: the user chose a warning with both **Skip unsupported** and **Export anyway** for mode-incompatible custom talents/rules. Preserve the original files, validate structural correctness, and do not write until the choice is made. Keep strict behavior for existing callers unless explicitly opted in.
- Show authored custom talent names in custom-team roster previews. Preserve native catalog separation and the existing campaign/embedded-definition precedence.
- Diagnose repeated exceptions: live Player.log identified Rogue.PuckRepellentComponent.FixedUpdate. Native initialization requires the forward's Rigidbody GameObject, not the root GameObject. Match native active/non-benched behavior and preserve cleanup.

## Work and evidence

- [x] Reproduced empty-roster rejection from the active campaign and native game log.
- [x] Changed admission to allow no authored skaters across either line; partial nonempty first lines remain rejected. 34 roster checks and 1,960 existing two-line recruitment checks passed.
- [x] Reproduced missing default goal-audio hooks using real Harmony and controlled Unity/FMOD boundaries; candidate passed 12 default and 12 campaign checks without audible playback.
- [x] Integrated the default-only goal-audio installation route.
- [x] Implemented roster custom-name lookup; 11 actual UI checks and 15 catalog-separation checks passed.
- [x] Finish and verify unsupported-reward export choices and explicit runtime opt-in: 97 focused checks, 1,135 custom-reward regressions and 68 update checks passed.
- [x] Finish puck-repellent initialization and native ownership handoff: 19 source-linked checks passed.
- [x] Match editor saving to the empty-start exception and keep removed players absent after line switches: 27 editor checks passed; label regression remains 11/11.
- [x] Build current runtime and Studio together; verify compiled integration and native profile, and independently review the final changes.
- [x] User explicitly selected Publish full R41 release, authorizing the production installer, update metadata, commit/push and public release. Local installation was not selected.

## Ownership during implementation

Main: Plugin.cs, CustomStartingRoster.cs, audio bootstrap integration, build/profile/installation coordination, test allowlists.
Export implementer: custom-reward export preparation, warning choices, companion policy and the two runtime ingestion points.
Label implementer: GameData display helper, TeamEditorRoster labels and focused tests.
Puck investigator: CustomRewardEquipmentEffects and focused lifecycle tests.

Game/profile files are diagnostic inputs only. No run reset, roster injection, game termination or new public release has been performed for this follow-up. Test artifacts are under `artifacts/empty-squad-start-20261104`, `goal-audio-investigation-20261104`, `custom-talent-labels-20261104`, and `play-now-export-choice-20261104`.

Final runtime: `artifacts/play-now-followup-20261104/runtime-final/CustomCampaignFramework.dll`, SHA-256 `8AD2CC60279236ADC7BC73AF4549C12EDFE4CB23093C2C07D96D8B73B2CF2327`. Current game profile remains `ECE606B3ADBEF69ACDDE4C7F7907A923F4B5E1D074BFF7BE46DC2BD16BCA5ED1`. Runtime build: 294 input hashes, zero drift, zero errors and one existing unused-variable warning. Twelve compiled integration checks passed against the actual DLL, including default/campaign bootstrap branches and both export ingestion sites.

Matching Studio: `artifacts/play-now-followup-20261104/Studio/`, managed assembly SHA-256 `92FE0E0912CF25EB5C92959D109253635EE588DC65334747010E0F05D14C134D`. Studio build: 815 input hashes, zero drift, no build diagnostics; package audit passed. Local repository release payload/hash were promoted to the tested runtime, with the old R40 runtime retained in the updater allowlist and backups under `source-runtime-before/`. Installed game/Studio are not yet changed. Independent final review found no remaining Critical or Important issues; live gameplay/audible acceptance remains pending.

# Four-feature implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement these plans task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the four approved additions while preserving existing campaigns, exports, player appearance and unfinished source work.

**Architecture:** Three plans extend existing flows with narrowly scoped, versioned data. Play Now lines and campaign reserve goalies require separate persistence adapters; jersey artwork uses per-kit resolution without changing team identity.

**Tech Stack:** C#, CampaignCore netstandard2.1, Avalonia Studio net8.0, BepInEx/Harmony IL2CPP runtime, Windows PowerShell.

**Spec:** [Approved design](../../FOUR-FEATURE-DESIGN-2026-10-20.md).

## Global constraints

- Goalie recruitment is per campaign. Reserve goalie slot is per playable squad. Both default off.
- Goalies is shown/selectable on a node only with campaign recruitment on; a reminder tooltip explains the requirement.
- Play Now carries team stamina and saves in-game lineup edits across restarts.
- Reserve goalie swapping is campaign-only and between matches.
- Contracts default to four played matches when enabled, respect departure prevention, promote the reserve on expiry, and retain an expired active goalie until a replacement exists.
- Revision 23 is published. Its runtime, receipts and public assets remain the reference baseline, not output locations for intermediate builds.
- Preserve authored content and the six dirty files in the Git mirror. Canonical Desktop source is not a Git repository. Do not run a blanket mirror sync or reset that checkout.
- No installation, real-save mutation, game launch, or new release publication is part of these implementation plans. Gameplay acceptance remains pending until observed.
- Use one build at a time per checkout. Reuse unchanged passing checks; no unrelated full-suite runs.
- Do not retry the policy-blocked baseline-copy deletion through another mechanism.

## Review focus

1. A native editor save changes only one row: both lines and shared player ownership must survive (Play Now tasks 2–4).
2. Two campaigns contain identically named logos: the selected campaign and correct kit must win (jersey tasks 1–3).
3. The campaign toggle changes with unsaved map edits: hidden controls cannot leave an undetected invalid saved node (goalie task 1).
4. A native save succeeds but the companion promotion fails: Continue must select a matching recoverable checkpoint (goalie task 2).
5. Contract expiry and a reserve swap happen around one match-completion callback: no double debit, loss, or resurrection (goalie tasks 3–4).

## Implementation order

1. [Two-line Play Now](2026-10-20-play-now-lines.md): codec/export, ownership-safe updates, native loading, editing/stamina/save.
2. [Home/Away jersey logos](2026-10-20-jersey-logos.md): per-kit contract, Studio/portable exports, both world renderers.
3. [Campaign goalie features](2026-10-20-goalie-features.md): settings, checkpoint persistence, recruitment/swap UI, contracts.

These are separate review units, not permission to leave one half-integrated. The goalie plan depends on the completed jersey appearance contract for full-state snapshots. Shared files such as ExportTranslation.cs, Plugin.cs and compatibility inputs must have one writer at a time.

## Verification commands

Use `C:\Program Files\dotnet\dotnet.exe` for every dotnet command in the component plans. Run from the canonical workspace, or the chosen implementation checkout with its required local dependencies. Existing tests are small executable projects: exit 0 and all assertions passing is success. A build by itself is not a passing behavior check.

Stage all new native evidence below `artifacts/followup-four-features-20261020`, and candidate outputs below `build/FourFeatureCandidate`. Do not overwrite revision-23 audit receipts.

## Final integration task

**Files:** `src/ModCompatibility/Assets/native-baseline.json`, `src/CustomCampaignFramework/Assets/native-compatibility.json`, `src/CustomCampaignFramework/Assets/squad-behavior-sites.tsv`, `docs/NATIVE-26085704-REVIEW.md`, component guides and `SESSION_HANDOFF.md`.

**Interfaces:** consumes each component's tested codecs, runtime adapters and native evidence; produces a candidate with matching runtime/profile/baseline and an exact verification receipt.

- [ ] Verify the installed native game hash still matches the reviewed build. If changed, stop using old native addresses and review the changed inputs before building new hooks.
- [ ] Review newly used native methods and add their complete protected contracts. Generate baseline/profile into separate staging paths using `tools/CompatibilityTool`; preserve the previous pair until both outputs validate. Reuse the documented `baseline`/`verify` commands, with build `26085704` only if still correct.
- [ ] Run `dotnet run --project tests/ModCompatibilityChecks -c Release`; require all assertions to pass. Re-run changed component tests and directly affected existing appearance, export, skater-contract and roster checks.
- [ ] Build `src/CustomCampaignFramework/CustomCampaignFramework.csproj` and `src/CampaignCreator/CampaignCreator.csproj` in Release to candidate output folders. Review build warnings and errors; no shipping runtime file is replaced here.
- [ ] Adapt the existing source/compiled hook audits from `artifacts/goalie-recruitment-20261020/compatibility-audit` into a new evidence directory, with paths bound to the new candidate. Require zero unresolved targets/binding issues and exact embedded-profile equality. Do not accept the old 299-method count as proof of new-hook coverage.
- [ ] Publish a self-contained Studio candidate using the newly audited runtime as its bundle input. Run `tests/GameArtPackageChecks` against that actual publish directory and runtime. Record hashes of all payload files and source inputs.
- [ ] Have an independent reviewer inspect persistence failure cases, native hook scope and appearance precedence. Resolve findings, rerunning only affected checks.
- [ ] Save an implementation receipt and update the handoff with completed steps, commands/results and remaining human gameplay checks. Commit only intended source, tests and docs in an isolated Git checkout after comparison with canonical source; no push/release action in this plan.

## Plan review and execution

The user approved the revised written design. These implementation plans await review and execution-method selection. Recommended method: **subagent-driven**, with one implementer at a time and a fresh reviewer at each component boundary, because save recovery and native hooks warrant independent review. The alternative is implementation by the main agent with one independent final review.

This planning pass changes documentation only. The canonical workspace has no Git metadata, so the documents are not yet committed; include them with the isolated implementation history without modifying the dirty mirror.

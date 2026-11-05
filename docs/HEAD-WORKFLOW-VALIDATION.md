# Head workflow candidate, 2026-11-04

Based on R39 (`024a1924532f7f8d2c6e6dfdb8925ed1d4c94d38`), with local source changes. No release, installation, live gameplay, or Photoshop UI acceptance is claimed.

The user confirmed that Photoshop Save As PNG retains the needed canvas, while Export As changed it. Full-canvas imports now preserve pixels exactly. Native-size inputs use their per-view native canvas mapping; known trim-only exports recover the matching saved/native offset. Other cropped images use the existing runtime collar alignment, with clear instructions to preserve the full canvas for intentionally moved artwork.

## Checks

| Suite | Passed |
| --- | ---: |
| HeadWorkflowChecks | 117 |
| BlankHeadTemplateChecks | 151 |
| HeadPreviewChecks | 35 |
| LayeredHeadFileChecks | 34 |
| Independent psd-tools parser | 141 |
| StuHeadEditorChecks | 6 |

The workflow suite exercises actual PNG imports, project backups, source preservation, native and trim-only per-view pixel round trips, Head/Helmet/Portrait and extra-layer filenames, persisted head visibility, actual picker/remove-button behavior, unchanged runtime assets and existing assignments, legacy fitted aliases, and directional starter anatomy. Each new behavior was observed failing before its fix. All outputs use separate disposable fixture directories.

Rendered UI and starter previews were inspected. Existing PSD edits survive refresh/install; the new starter artwork is generated only for new projects. The updated faces remain blank.

Independent read-only review identified legacy fitted alias resolution and a stale selected-head exception in an open picker. Both received failing regression checks and verified fixes; the follow-up review found no remaining important issues.

## Evidence and candidate

Evidence root: `C:\Users\kiran\Desktop\tape to tape mods\artifacts\head-workflow-20261104`.
Final workflow log: adjacent `head-workflow-review-green.log`; broader logs and PSD reports are under `verification/`.
Standalone Windows candidate: `candidate/Studio/` with usage notes in `candidate/README.txt`.
The game runtime remains SHA-256 `C125066493173D992889562E5C167EFC4415AC2F5F290E51A4315927CE91A045`.

Windows publish completed without warnings or errors. `T2T_Campaign_Studio_Head_Workflow_Candidate.zip` contains 250 files, each read back and hash-matched against the built candidate. ZIP SHA-256: `A96F644886A751C10778E969FECFDF6460ECD9ABCF328E88AEBEA39DA6E0EEA4`. The candidate was not installed or published.

# R41 validation

Release `beta-1.0-r41-build-1793846359003`, build `1793846359003`. Source baseline: R40 `12197ab4936c09c35eba4f91acce094cc1f4e87c`.

## Verified behavior

- 34 starting-roster checks and 1,960 two-line recruitment checks passed. Zero authored skaters are permitted without inserting donor players; partial nonempty first lines retain admission checks.
- 27 actual editor checks passed for empty/goalie-only No Effects saving, malformed/partial/duplicate rejection and no ghost players after saved removals and line switches.
- 24 goal-audio checks passed using real Harmony and controlled Unity/FMOD boundaries: default-only installation, campaign exclusivity, one playback, native-event replacement, volume changes and cleanup. No audible output was produced.
- 19 puck-repellent checks passed after reproducing the wrong Rigidbody-host failure. Native inspection confirmed the expected host and ownership fields. Tests include bench/removal, missing-body deferral, native acquisition/handoff/removal and custom reactivation.
- 97 focused export-choice checks, 1,135 custom-reward regressions and 68 Play Now update checks passed. Choices apply to an immutable prepared payload; cancellation writes nothing, Skip removes nested model/companion references, and explicit opt-in is honored by both runtime ingestion sites while structural validation remains enforced.
- 11 roster-label checks and 15 native/custom catalog-separation checks passed. Rendered roster and export-warning layouts were inspected.
- Total: 3,390 behavioral/regression assertions, plus 12 compiled integration checks against the exact candidate runtime. Passing results were reused where feature inputs remained unchanged; final editor evidence was recaptured for this release.

## Build and compatibility

Runtime SHA-256: `8AD2CC60279236ADC7BC73AF4549C12EDFE4CB23093C2C07D96D8B73B2CF2327`. Its 294 recorded input hashes were unchanged across compilation. The build had zero errors and one pre-existing unused-variable warning. The reviewed native profile is unchanged: `ECE606B3ADBEF69ACDDE4C7F7907A923F4B5E1D074BFF7BE46DC2BD16BCA5ED1`.

Installed Steam build 26372689 matched the reviewed game and metadata hashes; all 1,507 protected native methods and 137 comparison sites verified. The new compiled DLL was audited for actual bootstrap branches, roster policy usage, both marker-aware reward ingestion sites, Rigidbody access, native ownership transfer and the exact embedded profile.

The release uses a fresh production Studio build with this runtime, package auditing and exact installer extraction. Upload files must match the build/extraction receipts and allocated identity; public downloads are verified by SHA-256. Publication does not execute the installer or modify installed game files or user saves.

Independent review found no remaining Critical or Important issues after the authoring parity and stale-preview fixes. Complete live recruitment gameplay, audible playback with Base game selected and the corrected puck physics remain human acceptance checks. Export anyway preserves unsupported rules; it does not promise campaign-dependent effects in Play Now.

# R40 validation

Release `beta-1.0-r40-build-1793838044980`, build `1793838044980`.

- 484 focused checks passed in the current task: 117 head-workflow checks, 151 blank-template UI checks, 35 renderer checks, 34 PSD writer checks, 141 independent PSD-parser checks and 6 Studio head load/preview/save/export checks. Passing results were reused because the feature code did not change during release preparation.
- Actual imports preserve full-canvas pixels, per-view native offsets and known trim-only positions. Filename checks cover normal/fitted/portrait, extra-layer PSD names and older fitted aliases. Invalid input preserves existing artwork.
- Head removal keeps project/source files, runtime assets, existing player assignments and portraits intact. Reopening and changing the current selection respects hidden choices. Add restores the option.
- Updated plain starters have distinct directions, anatomical outlines and blank faces. Existing PSD edits survive refresh/install. Rendered layouts and starter sheets were inspected.
- Independent review found two issues, each reproduced and fixed with a focused regression; follow-up review found no remaining important issues.
- A fresh Windows Studio build, package audit, exact installer extraction and anonymous download hashes are retained in the release receipts. The installer is not executed during verification.
- This editor-only release reuses the R39 game runtime byte-for-byte (`C125066493173D992889562E5C167EFC4415AC2F5F290E51A4315927CE91A045`), with unchanged compatibility profile `ECE606B3ADBEF69ACDDE4C7F7907A923F4B5E1D074BFF7BE46DC2BD16BCA5ED1`. No live gameplay or Photoshop UI acceptance is claimed; saved user content and the installed application are not modified by publishing.

Keep the original canvas when saving PNGs for exact alignment. Cropping with a changed outline can discard its original offset; those images align to the saved view's native reference and require preview inspection.

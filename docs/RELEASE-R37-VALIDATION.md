# R37 validation

Release `beta-1.0-r37-build-1793673003060`, build `1793673003060`.

- 98 pool package, restore and actual Avalonia dialog checks passed, including 20 checks for direct saved-pool updates and 9 for the team-mode helmet selector.
- 3223 community client checks passed, covering replacement, revision/ownership/category guards and chunked uploads.
- The UI checks use isolated content and an in-memory HTTP transport. They verify the transmitted ZIP contains the latest saved players and embedded custom talent, cancellation sends no upload, and temporary packages are removed on success/failure.
- 130 appearance translation checks and 106 head/runtime policy checks passed. These include explicit no helmet under team inheritance, blank-field inheritance and existing native skin-output preservation.
- No game was launched, no public pool test listing was created, and no user saves/content were changed. Tests produced no audio.
- The runtime was freshly compiled from frozen inputs with zero input drift: `C125066493173D992889562E5C167EFC4415AC2F5F290E51A4315927CE91A045`. Compatibility profile remains `ECE606B3ADBEF69ACDDE4C7F7907A923F4B5E1D074BFF7BE46DC2BD16BCA5ED1`; Steam build 26372689. Native in-game behavior of the new option remains unverified; policy tests use native-boundary doubles.
- Release packaging checks, installer extraction, remote commit/tag checks and anonymous public-download hashes are recorded beside this release's build receipts. The installer is not executed during verification.

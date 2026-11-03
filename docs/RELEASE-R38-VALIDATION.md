# R38 validation

Release `beta-1.0-r38-build-1793681526224`, build `1793681526224`.

- 135 pool/package/editor checks passed, including actual confirmation, cancellation and removal through Base Game Pools, campaign squads, campaign/squad free agents, native lists and custom starting slots.
- 76 existing Base Game Pool checks passed.
- Review regressions reproduced and passed: stale last-team removal, a locked selection manifest after partial slot save, valid names starting with dots/underscores, and a real team named like the archive.
- Archives remain outside native team discovery and are omitted from imports and campaign packages. Original library items remain intact.
- Fresh Studio build, exact installer extraction and anonymous public-download hashes are recorded in the release receipts. The installer was not executed.
- Editor-only update: R37 runtime reused byte-for-byte (`C125066493173D992889562E5C167EFC4415AC2F5F290E51A4315927CE91A045`), same compatibility profile `ECE606B3ADBEF69ACDDE4C7F7907A923F4B5E1D074BFF7BE46DC2BD16BCA5ED1`. No new game test, native behavior claim, public pool test upload or user-profile change. Tests produce no audio.

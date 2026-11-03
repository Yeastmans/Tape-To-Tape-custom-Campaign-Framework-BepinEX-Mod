# R39 validation

Release `beta-1.0-r39-build-1793742083648`, build `1793742083648`.

- 151 actual Avalonia/data checks passed: all three blank options, visible/transparent artwork, separate guides, layered files, preview toggle/direction, unchanged saved files and equipment flags, refresh, edited-PSD installation, duplicate-name protection, missing artwork cache and goalie-mask regression.
- 35 combined renderer checks passed: all eight directions, native equipped-helmet overlay, extra-layer head draw order, older-cache extraction, padding alignment and blank artwork. Native helmet extraction validates the source skeleton identity and writes only its optional preview cache.
- 34 PSD writer checks, 141 independent psd-tools checks and 48 checks against the actual starter PSDs passed. They verify transparency, hidden guides, layer names, compositing and independent save/reopen.
- Three check-runner safety checks confirmed nonempty output folders are rejected without changing their contents.
- Independently reviewed with no remaining blocking findings. Rendered editor layouts were inspected at 1100 x 850 and 800 x 680. Guides do not appear in gameplay PNGs or merged PSD previews.
- Fresh Windows Studio build and exact installer extraction/public-download hashes are recorded in release receipts. The installer is not executed during verification.
- Editor-only update: the R37/R38 runtime is reused byte-for-byte (`C125066493173D992889562E5C167EFC4415AC2F5F290E51A4315927CE91A045`), with unchanged compatibility profile `ECE606B3ADBEF69ACDDE4C7F7907A923F4B5E1D074BFF7BE46DC2BD16BCA5ED1`. All checks are silent. No game launch, live community upload, user-profile modification, or external drawing-app UI acceptance is claimed.

The helmet preview is a static fit view using neutral reference colours. It does not simulate team colours, match lighting, animation or helmet gameplay; the game retains normal equipped-helmet behaviour.

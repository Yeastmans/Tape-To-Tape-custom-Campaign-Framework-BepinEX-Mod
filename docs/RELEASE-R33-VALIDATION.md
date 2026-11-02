# Revision 33 validation — unlimited campaign uploads

This update removes the application ZIP-size policy from current Studio uploads and the chunked Worker routes. It does not promise infinite provider storage: Dropbox upload sessions currently allow 2,199,019,061,248 bytes and last seven days, subject to account storage. The legacy multipart route retains its 20 MiB bound; current Studio uses chunked uploads.

## Focused regressions

- Worker: 42 tests pass, covering large declared sizes, offsets beyond 4 GiB, long sessions, invalid sizes/offsets, bounded requests, authorization, revision conflicts, and legacy compatibility.
- Client: 3,098 assertions pass. The production client streams a real 512 MiB + 17-byte file for both create and replacement, with bounded chunks and exact offsets, size and first/tail bytes. Empty files remain rejected.
- These tests first failed on the former cap and one-hour lifetime, then passed after the change. External HTTP is substituted; these checks are not full real-network uploads.

## Compatibility and package

The release retains the exact reviewed R32 runtime (SHA-256 56A02DF0BAF14A037975BC79A84E00BC99C4B67E1B42FACE7C665CA6FC443BCF) for Steam public build 26289478. Fresh verification against the installed game passed all 1,487 protected methods, 138 compatibility checks, and 57 native-frame checks. No native gameplay code is changed by this release.

The Windows Studio publish and installer are generated from this release's source. The paired update manifest identifies their exact hashes and build number. Installer extraction verifies the Studio payload, both runtime destinations, pinned loader files and allowed contents before publication. Extraction does not claim an elevated installer run or live gameplay acceptance.

## Production upload service

Worker version `f75ed607-3d9d-4c98-81a6-a4f6ce4e539e` is deployed at 100%. Its downloaded source matches the reviewed candidate, and health reports `maxCampaignBytes: null`. The narrow change preserves existing cover editing, all runtime settings, secret binding metadata, migration aliases and subdomain settings. Wrangler added only the expected version-message annotation.

The public campaign catalog was unchanged. A live session declared at 536,870,913 bytes acknowledged a four-byte chunk. Two unfinished probe sessions were started; neither was committed. No diagnostic public campaign was published and no existing campaign was replaced or deleted. This verifies live start/chunk acceptance, not completion of an entire large upload.

Detailed local receipts are in `artifacts/unlimited-campaign-uploads-20261101/` and `artifacts/release-r33-unlimited-uploads-20261101/`. They are excluded from the source repository. Full large-file transfer across a user's connection remains unverified.

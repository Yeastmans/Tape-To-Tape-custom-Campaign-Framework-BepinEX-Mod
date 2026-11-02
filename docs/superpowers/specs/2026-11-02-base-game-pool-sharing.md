# Base Game Pools sharing and backup restore

## Requested behavior

Campaign Studio needs a separate Base Game Pools section for uploading and downloading pool sets. A set contains the bench/draft, free-agent and superstar selections. Downloading replaces all three pools only after confirmation and preserves a backup. The user also needs a way to load a backup.

## Design

Home gains a Base Game Pools card with Edit pools, Upload, Download and Restore backup. Upload shares the current saved selections with a title, author, optional description and optional cover. Download shows only pool sets, offers file import too, previews the three candidate counts, and asks before replacing the current configuration. The downloaded set is enabled for None (base game); existing runs and campaign selection remain unchanged. The UI explains that changes apply on the next game launch.

Pool packages use their own versioned marker and include selected candidate files, referenced custom heads, logos and embedded custom talents. Native identity markers and custom filenames survive unchanged. Empty or malformed pools and unsafe archives are rejected before replacing current data. Unchecked players, management credentials, saves, backups and unrelated libraries are not shared.

Backups retain the whole prior pool directory, including unchecked player files and its enabled state. Each successful replacement creates a dated backup. Restore backup lists dated snapshots and candidate counts, confirms the replacement and first backs up the current setup. Backups are retained after restoration and can be selected again. A first installation can also be undone to no saved overrides.

The existing chunked community upload transport and private management codes remain in use. Pool sets occupy a distinct server namespace and are excluded from the legacy campaign list, including for older Studio versions. Campaign ownership, covers, download URLs and unlimited chunked uploads keep their existing behavior. Pool uploads have their own management list.

## Added user reports during implementation

The same update must fix Stu's missing/misidentified head (Bench Stumple previews Boring Dude) and stop forcing helmets onto unchanged native skaters when using edited base-game pools. Resolve head identity from current native game assets, preserve native bare/helmeted appearance, and retain explicit authored appearance choices. Verify both in the editor/native campaign with audio muted and Steam F12 screenshots for game captures. These additional reports require a newly built runtime.

## Validation and delivery

Exercise actual archive creation, replacement and restoration against disposable roots, including shared assets, collisions, corrupt archives and cancelled UI confirmation. Test server listing/ownership/category boundaries and client wire requests with simulated remote storage. Exercise the Studio controls in an isolated profile. Build and verify the new runtime against the installed game's existing compatibility contract. Review, deploy the service preserving its existing bindings/capabilities, and publish a new GitHub update with verified downloads.

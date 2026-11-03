# Pool and Campaign Entry Removal Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans inline, followed by one independent review.

**Goal:** Let people remove unwanted pool candidates and campaign player teams rather than only disabling them.

**Spec:** The user explicitly chose removal controls and independent imported copies. No automatic synchronization is wanted. Preserve original library players/teams and unrelated campaign copies. Existing Base Game Pools still require at least one selected candidate per section.

**Architecture:** Reuse a named confirmation with Cancel as default. Remove only the owned local entry, moving it into an excluded `_removed_entries` archive, and persist the corresponding selection. Validate direct-child boundaries and reject linked paths. Roll back the move if selection persistence fails. Exclude removed content from import catalogs and uploaded campaigns.

**Tech stack:** Existing C# .NET 8 and Avalonia editor; no game-runtime/service change.

## Tasks

- [ ] Add fail-first checks for direct-child removal, rollback, preserving originals, package exclusions, Base Game Pool minimums and actual confirmation dialogs.
- [ ] Add confirmed Remove controls to Base Game Pools, native candidate lists, campaign free agents and campaign player teams; make the existing custom starting-slot Remove remove its local copy too.
- [ ] Guard the last campaign team when Custom teams only is enabled. Preserve per-team settings/maps in the archived team directory.
- [ ] Explain independent copies and removal in the relevant help. Verify active/inactive entries, cancel, save/reopen, unchanged library sources and native defaults.
- [ ] Run affected editor/package checks, review, build and publish the next unused GitHub revision using the verified R37 runtime unchanged.

## Review focus

Removal must not follow junctions/symlinks, delete originals or shared assets, publish archived copies, leave selected references pointing to missing files, or silently change campaign-only-team rules. Native copied candidates must not reappear merely because an older hidden copy existed. Current pool selections are saved as part of an explicitly confirmed removal.

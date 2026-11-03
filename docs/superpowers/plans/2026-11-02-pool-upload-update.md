# Saved Base Game Pools Upload Update Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement this small follow-up inline, then obtain one independent review.

**Goal:** Complete the existing pool-sharing workflow with an Update from saved pools action for an owned listing.

**Architecture:** Reuse the existing package exporter and managed replacement protocol. Package the saved local selections, validate, show the target listing and three candidate counts for confirmation, then replace the selected upload using its existing ownership and revision. Keep its public details and cover. Always remove the temporary package.

**Tech Stack:** C# .NET 8, Avalonia, existing community HTTP client.

**Spec:** The user approved finishing the missing direct update workflow after being told that R36 required a prepared replacement ZIP. They then requested a No helmet option with Team equipment enabled. Existing authorization includes testing and GitHub publication. The helmet correction requires a runtime update; the upload service stays unchanged.

## Constraints and approach

- Add the direct action to Base Game Pools > Manage uploads; retain Replace ZIP for existing portable packages and campaigns.
- Saved selected candidates and their referenced talents, heads and logos travel together; no shared library dump or private codes.
- Preserve the upload identity, description, author and cover through the existing replace protocol.
- Require confirmation; cancel, missing saves, invalid packages and failures must not modify local pools.
- Run silent editor/client tests only; no game launch or changes to the restored user profile.
- Preserve the two unrelated ffmpeg text changes. Reuse the existing isolated worktree and current-game compatibility profile, and compile the corrected runtime for a new installer release.

## Review focus

Cancellation must send no upload requests; pool and campaign categories must remain isolated; stale listing revisions must stay protected; failed packaging must not begin a replacement; temporary packages must be removed on success and failure.

## Task 1: Direct saved-pool replacement

- [x] Add actual Avalonia dialog checks with a fake HTTP transport and isolated content: absent action fails, then cancellation, replacement payload/talents/identity, invalid pools, failure cleanup and campaign behavior pass.
- [x] Update `HomeScreen.CommunityManagement.cs` to accept its community client, expose the pool-only action and confirmation, and update the two callers.
- [x] Update the Home tooltip/help to state that talents travel and describe the update action.
- [x] Run `BaseGamePoolSharingChecks` and `CommunityClientChecks` with build servers disabled; review the complete change.

## Task 2: Publish the current Studio update

- [x] Inspect current GitHub main, tags and releases; allocate the next unused revision/build identity.
- [ ] Build the corrected runtime and Studio, package and extract the installer, and verify package identity and hashes.
- [ ] Commit only intended files, push main and the new tag normally, publish the installer/manifest/checksums, and verify the public downloads.

## Added scope: No helmet with team equipment

- [x] Add fail-first UI, appearance-resolution and native-adapter checks for explicit No helmet while retaining the team's other equipment.
- [x] Use the existing Helmet = none representation. Blank fields still inherit. Team-mode No helmet applies to both kits; independent equipment and native talent/reward equipment rules remain intact.
- [x] Expose Team equipment / No helmet choices while the toggle stays on. Verify save/reopen, switching back, and Play Now appearance export.
- [x] Make both appearance resolution and runtime application honor the explicit choice before the inherited-equipment early return. Review, build and include the result in R37.

# Base Game Pools

## Intent and confirmed behavior

Add a **Base Game Pools** button to Campaign Studio's home screen. It opens three sections: **Bench / Draft candidates**, **Free agents**, and **Superstars**. Each begins with all candidates in the installed game's standard `vanilla` pool enabled. Users can add, edit or replace candidates using their existing player library and the normal player editor. Adding Charlotte Chapstick must not require removing Boring Dude.

The profile applies only when **None (base game)** is selected. Its bench candidate list is shared by every native team, including All Angus. Preserve the team's existing screen eligibility, bench capacity, selection rules and other behavior: a team without a bench must remain without a bench. **Every saved pool must contain at least one candidate**, as confirmed by the user. A nonempty authored pool may still yield zero eligible choices at runtime; handle that without a crash or a stuck menu. The user explicitly chose to **continue without a superstar while keeping all other rewards** when every selected superstar is ineligible. Skip only the empty superstar stage, retaining native relic/powerup stages and rewards.

The user requested updating compatibility, publishing the completed work to GitHub, and releasing unlimited uploads first. R33 has now been published and verified. This feature follows R33 and must not republish unrelated canonical-workspace changes.

## Approach

A standalone user profile is the implementation choice. Copying an entire vanilla campaign would require selecting a custom campaign and fails the requested default-team workflow. A shared candidate profile, consumed only by narrowly installed default-mode hooks, fits the request. Keep normal custom-campaign code and its settings separate.

## Storage and editing

Store this profile below the normal content root in `base_game_pools/`. Three candidate folders are `player_teams/draft_pool`, `player_teams/free_agents`, and `player_teams/superstars`. A single atomically saved `settings.txt` stores schema, enabled state and the three ordered filename lists. Native candidates are independent text copies from the player's own extracted `base-campaigns/catalog.json` standard `vanilla` entry; extracted game assets are never shipped.

Missing settings mean disabled/no override. Opening an editor may prepare private candidate copies, but must not activate a profile. Saving validates all three nonempty selections and all referenced files before replacing settings. The editor offers an enable switch and restore-defaults action. Imports copy players and remove native binding identity so they acquire independent stable custom IDs. Editing a native candidate changes only the private profile copy. Candidate identity must survive reordering and content-root relocation.

Reuse the existing player editor, import sources, theme and scrolling patterns. Include clear Save, Back/Home and inline validation feedback. Candidate lists have no fixed nine/three slot cap. Keep unsaved selection changes distinct from saved player edits. Returning from a player edit must return to the pool editor.

## Runtime

Install a dedicated pool feature in default mode, without enabling the full campaign patch set. Preserve current native profile checks. Clone exact native candidate variants using stored native ID plus asset name; apply authored player settings to private clones. Custom candidates get stable profile-specific IDs and narrowly scoped unlock support.

Use separate sources for bench, free-agent and superstar consumers. A temporary bench `m_CustomSwapPool` must be restored after the bench menu so native GM selection cannot consume it. Native superstar and GM routines index three and two offers respectively; provide bounded behavior for small or exhausted eligible pools using the current native contract, retaining native unlock/squad/roster exclusions and normal offer counts where possible. Do not mutate original assets or native save formats. Current native deserialization already restores full player models.

## Verification and constraints

Tests must cover first-open defaults, add/replace/reorder, nonempty enforcement, atomic failure, path validation, persistence, UI navigation, disabled/default/custom-campaign isolation, separate consumer pools, native-team no-bench behavior, small/exhausted eligible pools, restoration after exceptions, and stable identity.

Use current Steam build 26289478 and preserve native compatibility checks. Inspect real signatures/native behavior for newly relied-on hooks. Compile the actual mod and Studio; test focused affected behavior and inspect the UI. Gameplay tests must be **muted**, preserve existing saves/content, and be reported separately from simulated tests. Live FC investigation is independent and must not introduce a speculative patch.

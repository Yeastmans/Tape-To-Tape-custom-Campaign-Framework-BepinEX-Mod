# Squads, recruitment and rewards

Campaign Studio Beta 1.0 targets the Windows roguelite game, build 25899138. Select a campaign on Home, enable **Use player teams**, then edit a team under **Custom teams**. **Custom teams only** restricts that campaign's squad selection. Select **None (base game)** on Home to return to normal play.

## Templates and behavior switches

Each player team has its own template and behavior settings. **Inherit** follows that template; **On** or **Off** overrides the named rule. Switching templates preserves explicit overrides. These switches route the game's existing implementation rather than creating another version of the same rule.

A template also supplies data: starting identities, designated lead and extra Angus identities, roster slot locks, supported modes, and presentation assets. A behavior switch does not import all of another squad's data. Use a copied native squad when those identities and assets are important. Starting relics are separately editable in **Team roster → Team relics**; talents and abilities belong to each player. For example, Slapshot's weak ordinary shots come from **Slapshot Plushie**, not the **Slapshot AI** switch.

Studio exposes the following 52 rules. Their in-editor descriptions show the effect and inherited state.

| Family | Controls | Requirements and boundaries |
|---|---|---|
| Starting choices | Superstar selection; Bench draft; Oldschool starting choice; Automatic starting bench; Skip squad-menu exit transition | Candidate pools and template data still matter. The exit-transition switch does not enable contracts or starting choices. |
| Active roster | Four active skaters; Three active skaters; Trio draft requirement; Gauntlet draft requirement | Trio needs three active and at least three bench skaters; Gauntlet needs five active and at least two bench skaters. Bench capacity must fit. |
| Lines and bench | Two-line roster flow; Line stamina; Disable bench quests; Disable bench snapshots; Disable intermission lineup changes | Two-line flow requires ten skaters plus a goalie. It forces starting superstar/bench/Oldschool choices off and prevents intermission swaps; both lines remain visible as previews. |
| Contract recruitment | Limited free-agent contracts; Refresh rewards after recruitment | Uses native contract generation and signing behavior. Starting contracts need a new run. GM nodes and match reward counts are separate settings. |
| Match reward rules | GM match reward mix; No-relic reward rules; Attribute rewards; Slapshot talent weighting; Disable new ability offers | GM and No-relic mixes cannot both be on. They override saved normal match counts; bonus-reward relics and native restrictions still apply. Attribute rewards also affect matching Coach/Fan events. |
| Match formats | First-period deadline; Eliminator match rules; Goal-target mode only | Speedrun and Eliminator require goal-target mode. Eliminator also requires four active skaters. These formats cannot both be on. |
| Solo | Control only the lead skater; Solo camera; Keep the lead skater active; Target the lead skater | Uses the selected squad's designated lead identity for control, roster restrictions and applicable Nurse/Blademaster targets. |
| On-ice rules | Disable injuries; Manual pokecheck; Tchel offside rules; SNES skating; Violence collision rules; Slapshot AI; Send goal scorer to penalty box | Tchel offside uses native Knockout and takes precedence over the ordinary campaign offside penalty. Slapshot AI changes AI decisions; relic effects are separate. |
| Gauntlet | Gauntlet stack scaling; Gauntlet opponent growth; Gauntlet end-of-match flow | Uses native positive-stack scaling, act-based opponent growth and end-of-match rules. These do not automatically supply Gauntlet's starting roster. |
| Map generation | GM map recruitment rules; Omit generated GM nodes; Oldschool node replacements; Training-node replacements; Solo map rules | Applies to inherited/generated maps. Authored map nodes remain as saved. GM placement and omission cannot both be on. |
| Opponents and curses | Referee opponent exception; All Angus curse exemption; Oldschool curse exemption; Eliminator curse exemption | Uses the native opponent filtering or curse exclusion at its own trigger. A normal match without that trigger does not exercise the rule. |
| Presentation and identity | SNES audio; SNES match presentation; Treat starting skaters as Angus; All Angus outro; Referee uniforms | Uses native presentation or identity checks. Recognizing starters as Angus can also exempt them from native starting contracts. |

Extra Angus identities, active-skater marker prefabs, advertising-board assets and squad ice-tint data have no independent controls in this beta; they remain inherited from the selected native template. Native mode, unlock, role and event prerequisites still apply. Studio does not claim arbitrary combinations of native squad data are interchangeable.

## Starting players and bench space

Starting superstar and bench pools are shared by the campaign's player teams. Each team's screen switches decide whether to use them. An explicitly empty starting pool skips that screen; an unconfigured pool uses native defaults. Empty starting bench candidates do not, by themselves, remove later recruitment capacity.

For **No Effects**, set **Bench draft = On** and **Bench Size** above zero to allow later bench recruits. You may leave its starting candidate pool empty to skip the draft. Turning Bench draft off on No Effects sets its bench capacity to zero. Other templates retain their native automatic bench behavior and capacity unless overridden.

The ordinary authored roster has five editable skater positions plus a goalie; two-line flow exposes ten plus a goalie. A copied native starting roster may contain 13 skaters, including reserves: these native reserves are retained even when only the active positions are editable. Three/four-skater rules and locked slots still constrain the active lineup. Changing startup, roster or contract settings requires a new run; it does not replace an existing run's players.

## Recruitment and contracts

The **Free agents** page controls recruitable players and their independent random-talent pool. Starting superstar and bench selections are separate. Add General Manager nodes to authored maps wherever recruitment is needed. Turning on contracts does not add these nodes.

With **Limited free-agent contracts** enabled, native startup assigns four-match contracts to starting skaters except those recognized as the squad's Angus/lead; the goalie is excluded. Later contracts, countdown, expiry and replacement eligibility use the native game. The **GM match reward mix**, recruitment-node rules and reward refresh switch are independent choices.

Player selection and talent selection have different empty-list behavior:

| Setting | Empty selection means |
|---|---|
| Recruitable players | Native free-agent fallback when no selected recruit files remain. |
| Starting superstar or bench candidates | Skip the corresponding starting screen. |
| Free-agent random talents in Custom mode | Grant no random extra talents. Authored fixed talents remain. |

A recruit keeps its authored fixed talents, ability and stats. The random-talent controls govern newly generated extras and random upgrades; they do not strip authored fixed talents or rewrite already generated offers, signed players or pregenerated contracts. A fixed talent may therefore appear even when its checkbox is unchecked. Studio no longer exposes the old Random Talents template editor; legacy runtime random-template grants use the recruitment allow-list when that path is loaded.

## Independent free-agent talent pool

Start with the base-game selection, then add or uncheck exact levels. This pool is independent of **Regular** and **Chaos** rewards.

- Each checkbox allows that exact level. Unchecked levels cannot be newly rolled as recruitment extras or returned by a random recruitment upgrade.
- Checked base talents keep native upgrade chances, but only checked upgrade outcomes are allowed. If an upgrade has no allowed outcome, the checked original level remains.
- A checked higher level can also be selected directly. Native unlocks, role, squad exclusions and conditional eligibility still apply; checking a talent does not make every recruit eligible.
- Multipliers change direct-roll weighting. Zero prevents a direct roll; it does not disable an otherwise checked upgrade result. Uncheck the exact level to prohibit both paths.
- The **Base Game** mode with default weights delegates to the native picker. **Restore Base** restores the native checked outcomes while retaining multipliers.

Changes affect later generation. Restart or start a new run when evaluating starting or already pregenerated choices.

## Match and event rewards

**Regular talents**, **Chaos talents**, **relics**, **goalie talents** and **abilities** each have their own pool. Showing Regular choices in the Chaos catalog, or Chaos choices in Regular, changes the visible catalog; it does not change the other saved pool. Free-agent talent settings do not consume the Regular reward reserve.

Base Game keeps the native picker. Custom selections constrain that pool while the caller still applies ownership, role, unlock and event eligibility. Selecting an ability does not force a new-ability offer when the squad disables those offers. Blademaster upgrades abilities; it does not upgrade talents. Goalie rewards have a distinct recipient and pool.

Saved match reward counts apply unless a squad rule overrides them. **GM match reward mix** supplies zero normal talent selections and one relic selection. **No-relic reward rules** supply two talent selections for Elite/Boss, one for Challenge and zero normal relic selections. Native bonus-reward relics can still add rewards. The editor's reserve calculation considers the longest route and spare choices; General Manager recruitment no longer increases the Regular talent minimum.

## Focused checks in a fresh campaign

Shared picker and serialization paths do not need a match for every talent or player. Use representative examples and check distinct triggers:

1. Configure No Effects with bench space and an empty starting bench pool. Start a new run: the draft is skipped, and a later recruit can use the bench. Save and reopen the team once to confirm the settings remain.
2. Offer an authored recruit with one recognizable fixed talent and a small exact-level random pool. Sign the recruit and inspect fixed versus random talents; an unchecked upgrade level must not appear as an extra. Then inspect one Regular and one Chaos offer with deliberately different selections.
3. Save, close, reopen and Continue after signing. Confirm the player's identity, loadout, roster position and contract state persist. Following a contract departure, confirm the next eligible replacement/recruit can be selected.

Contract duration countdown and expiry/removal were already reported working in the current session; those observations remain valid for that setup. Replacement eligibility and Continue with a newly signed contract remain separate checks. Two-line changes, Solo control, Eliminator/Speedrun endings, inherited-map replacements and special curses are distinct native paths; recruitment alone does not confirm them. Source auditing and successful builds provide technical evidence, not an unreported gameplay pass.

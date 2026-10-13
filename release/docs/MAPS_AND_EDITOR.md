# Maps and editor reference — Beta 1.0

Campaign Studio edits campaigns for the bundled mod. Use **Save** for the campaign, teams, players and maps you change, select the campaign on Home, and restart Tape to Tape to use it. **Export to Play Now** creates separate game custom content; it does not add that content to a campaign.

## Maps

Each layer supports **up to two nodes**, except the final layer, which contains one Boss. Connect each non-final node to the next layer and make every node reachable from the entry. The editor checks routes, opponents, rewards, source-map availability and placement before saving.

| Control | What it changes |
|---|---|
| Map tabs / Add map | Maps in campaign play order. Each map retains its own layout and settings. |
| Import base-game map | Copies a native layout. Importing another campaign's map clears its team assignments so you can choose this campaign's opponents. |
| Generation | Native-template random nodes retain their source event pools unless you author a replacement. Saving a layout does not mean every Random event has already been selected. |
| Map name | The opening map title. Blank keeps the game's act name. ACT numbering follows campaign order. |
| Map look | Act 1, 2 or 3 world-map scenery, independently of campaign order. It does not change encounter requirements. |
| Add node / Insert layer | Adds an encounter or a new route layer. A crowded layer is spaced as one undoable edit. Connect the resulting routes before saving. |
| Drag / Fit node placement | Moves nodes within a layer or repairs crowded/out-of-range placement. Fit preserves IDs and connections. Existing valid layouts keep their coordinates. |
| Node settings | Encounter type, opponent, map stadium, rewards, recruitment count and timed-match overrides where applicable. |
| Game preview | An estimate using game artwork. Native arena bounds, animations, badges and decorations resolve in the game. |
| Save maps / Save campaign | Validates and writes layouts, with backups of previous maps. Unsaved edits remain a draft. |

Beta 1.0 supports two rows at native artwork spacing. Existing valid two-row layouts retain their authored coordinates. In game, the camera stays within the original background height. Arena sprites keep their native size; an unusually tall pair can be staggered horizontally to keep both choices visible. Scrolling retains the native map edges instead of stretching border pixels. Older three- or four-choice files remain readable for compatibility, but Studio requires explicitly reducing or moving the extra choices before saving. Opening a map and using Fit never removes choices or silently changes the route.

An opponent assignment selects the team for an Elite or Boss node. An unassigned match uses its native opponent. The node's **stadium** selects world-map artwork; the opponent determines the match arena. An effect may require an Elite or Boss node even when its checkbox is available in opponent settings. Read that effect's requirement text.

**Periods** and **Period length** independently override timed matches. Zero / Use base-game setting inherits the native value. They do not replace goal-target rules. Random-event weights are relative: zero excludes an event, and positive weights compete. An explicitly authored random pool can repeat event types and bypass the native placement restrictions described in its help. Unedited native random nodes retain native restrictions. Challenge is a separate node type, not a random-pool entry.

Save edits for future runs. Continue uses the exact applied layout recorded with that saved map, so editing a campaign file does not rewrite a map already in progress. Native save timing still determines which progress is recoverable.

## Imports and inherited behavior

**Import player** and **Import team** copy editable fields. Matching a native name, head or outfit does not grant native player identity, reserve players, transformations or identity-based event conditions. Use **Copy vanilla campaign** on Home when you need a campaign based on the native squad and opponents. That workflow preserves native identities and inheritance; its draft becomes active only when saved and selected.

Copied native rosters can contain more players than the editor's normal slots. For example, the Golfers copy retains all 13 native players, while five starters or ten players in two-line flow are editable. Editing those slots preserves the remaining native reserve players. A new custom roster does not create those reserves or transformation targets automatically.

After a game update, use **Read game artwork** to refresh the installed game's rosters, equipment, heads, stadiums and map sources. Unavailable imported talents or relics remain visible so the source data is not silently lost; their presence does not prove the current game can resolve them. Refreshing artwork does not restore deleted custom libraries.

## Player and team appearance

| Setting | Inheritance and native behavior |
|---|---|
| Team equipment | Uses the team's styles. Turn it off for that player's own supported equipment styles. Team kit colors and personal stick/skate colors have their own controls. |
| Number Style | **Inherit team** follows the team default. An explicit player style takes priority, even with Team equipment enabled. Imported native goalies can intentionally differ from their skaters. |
| Home Helmet: Team colors | Equips the ordinary native helmet without adding a talent or stat bonus. Native equipment rules control loss, drops and restoration. |
| Home Helmet: Wing | Equips native Wing equipment without adding a talent or stat bonus, using Wing's native drop rules. |
| Home Helmet: None | Omits the ordinary starting helmet. It does not suppress helmets supplied by a talent, transformation or reward. |
| Helmet Away | Does not independently equip, remove or replace the physical helmet. Live native equipment determines that model. |
| Other special helmet artwork | Requires its native equipment source. Internal reference art and effect-only styles are not ordinary equipment choices. |
| Heads without a fitted helmet variant | The style picker is unavailable for that head. Native equipment, talents and transformations still follow their own rules. |
| Premade pieces and logos | Fixed artwork may ignore colors or lack a compatible logo slot. The flat preview cannot establish 3D fit, animation or every material channel. |
| Goalie Pass Read | Preserves native fractions in the 0–1 range. Zero uses normal reading; a positive value enables quick reading only when the current difficulty permits it. Larger positive values do not continuously increase strength. |
| Stat Scale | Multiplies skater speed, shot power, accuracy and checking, and the goalie's numeric attributes except Pass Read. Blank or 1 preserves the configured values. |

The native save format omits some appearance data. The mod preserves supported authored appearance on Continue and writes companion data for Play Now exports. Keep the relevant campaign/player source files and export companion data. Continue restoration is restricted to appearance so earned stats, equipment and talents are not reset. Ambiguous player matches are left unchanged.

Play Now exports contain one player per standard position. Extra lines cannot be exported in that format. Save a team's equipment/settings and its roster players before exporting; re-export after edits and restart the game to load the new custom content. Companion data requires the bundled mod.

## Finding requirements in Studio

Use the page **Help** button or **F1** for the current controls. The searchable guide includes related topics, detailed field references and the same opponent-effect, interaction and squad-behavior descriptions used by their editors. Control tooltips explain local inheritance and restrictions. Help navigation does not save, import, export, play audio or change the draft.

Shared fields use shared application paths, so every value does not need a separate match. Distinct native boundaries still matter: a goalie differs from a skater; equipment loss differs from a preview; fresh-run creation differs from Continue; ordinary imports differ from native copies. A representative branching route, one own/inherited appearance comparison and a save/reopen check cover those editor boundaries more usefully than repeating every checkbox.

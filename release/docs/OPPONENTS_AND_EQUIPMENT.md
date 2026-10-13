# Opponents, AI and native equipment

Campaign Studio enables the game's existing opponent behavior. An effect checkbox changes the relevant native team check; the game still decides whether its node type, player, ability, target, match state and timing requirements are met. Selecting an effect does not equip an ability, grant a relic, create a specialist roster or make a match the campaign finale.

This reference describes Tape to Tape build **25899138**, audited on 13 October 2026. The native code and asset findings are technical evidence. They are not a claim that every option has been observed in a live match.

## Native copies and new custom teams

- **Copy base campaign** preserves native team and player identities, source assets, lines and behavior that the editor does not replace. An unchanged native copy retains its native behavior. Editing a supported setting overrides that setting; changing an effect does not replace the whole team's identity.
- **New custom opponents** use the selected effect set. None means no selected opponent effects. Conflicting choices are identified in Studio; invalid conflicting choices in hand-edited files are disabled together rather than silently selecting a winner.
- **Copied Golfers keep all thirteen native forwards.** Studio edits five starters, or ten skaters when two lines are enabled. Unrepresented native reserves retain their identities, talents and native flags. Explicit whole-team stat scaling still affects the full roster. A newly authored ten-slot team does not automatically gain those native reserves or transformation targets.
- Native Golfer transformations require the original Cornelius/Pam asset identities and a Cultist transformation talent with its native target. Renaming a custom player's display name does not create that relationship. Keep these native relationships intact when copying the boss.
- **Golfer defense roster preparation** prepares native defense pairs. The current game does not enable its automatic goal/tier swap path. Offensive and defensive formations are separate settings; enabling roster preparation does not clear the defensive formation.
- The **Counts as Prince** player option supplies identity only to the Crusader bodycheck brawl trigger. It does not grant other Prince-specific sounds, stats, equipment or behavior.

## Helmets and appearance

Skater helmets are native equipment with a lifecycle, not a permanent overlay.

| Choice | Native result |
|---|---|
| Team colors / ordinary helmet | Equips the native standard helmet without granting a talent or stat bonus. |
| Wing | Equips the native Wing helmet. This is ordinary equipment without status-effect bonuses, with its own native drop settings. |
| No helmet | Omits the ordinary starting helmet. Equipment granted by a talent, transformation or reward still follows its native rules. |
| Empty helmet field in an imported native player | Imports as explicit No helmet. All thirteen native Golfers have empty home and away helmet fields. |
| Helmet Away | Does not independently equip, remove or replace the physical helmet. The home Helmet field supplies the starting equipment choice. |
| Frog, Golden, Enraged, Heavy or Propeller artwork | Requires its native equipment/talent/status source. These are not offered as persistent cosmetic helmet choices. |
| Reference, face-support and shader-only helmet layers | Internal artwork; unavailable as standalone equipment choices. |

The game grants starting equipment before rendering, chooses a fitted or bare head from the live equipment state, and controls drops, world props and restoration. A helmet dropping after a hit is subject to native eligibility and chance; a single hit is not guaranteed to drop it. Rendering must not immediately restore a legitimately lost helmet.

Older **native-copy** configurations with blank source helmet fields preserve the native no-helmet baseline when an edited copy is applied. An explicit standard or Wing choice still overrides that baseline. Old ordinary custom imports have no reliable native-copy identity: choose No helmet explicitly or import the native player again if a blank value was intended to mean bareheaded. Newly authored custom defaults remain unchanged.

Goalie masks are a separate native appearance system. This skater equipment explanation does not promise detachable goalie masks. Premade uniforms, team colors, jersey-logo compatibility and custom head support continue to follow their corresponding appearance rules.

## Match settings

| Setting | Requirements and scope |
|---|---|
| Offense formation | Selects a native offensive AI asset. Standard leaves the native fallback. The golfer-offense effect selects the native golfer system; normal roster and movement checks still apply. |
| Defense formation | Selects a native defensive AI asset. Standard leaves the native fallback. Native short-roster and Total Attack rules retain precedence. Independent of Golfer defense roster preparation. |
| Goalie Pass Read | Native range 0–1. Zero uses normal pass reaction; any positive value enables quick pass reading when the current difficulty permits it. Higher values do not scale this effect. |
| Puck / ball | Selects the hockey puck, soccer ball or golf ball separately from surface and team rules. Selecting a ball does not also grant the corresponding opponent effects. |
| Playing surface | Selects the initial native ice/field setup. It does not change node type or grant terrain-related talents. |
| Golf Fairway | Requires Golf ice. Adds/removes the native Fairway relic on the configured opponent; its native controller owns terrain layout, physics and cleanup. An omitted choice follows Golf ice; explicit Off remains Off. |
| Tycoons sponsors | Requires Tycoons ice. Controls the native sponsor menu for this opponent. Does not grant the breakaway-goal brawl or two lines. |
| Canada curses | Enables the configured opponent's native Canada picker flow. Native offer timing, repeats, available handicaps and squad exemptions remain. |
| Two lines | Requires five starters and a complete five-player second line. Native line-change timing remains. It does not create a player-squad recruitment bench. Unchanged native copies preserve source lines. |
| Referee Hands Auto / On / Off | Uses the equipped native relic controller. Auto and On permit this opponent's stick-removal immunity; Off permits the normal removal. The setting does not grant the relic. |
| Match periods | Authored period count/length applies only to a valid timed match. Goal-target matches keep their native win condition. The original timer is restored after the match. |
| Campaign offside | Enables the native offside system and selected transient penalty. Native Tchel/Linesman sources retain their own precedence; persistent game settings are not rewritten. |

## Effect requirements

All seventy options below use resolved consumers in the audited native build. They retain native eligibility and timing. Brawl offers also respect the game's active/pending offer, end-of-game, fight, goal-state, timer and Eliminator restrictions. One matching action may be ineligible because an offer is already pending or was already made.

### Brawls

| Option | Behavior and requirements |
|---|---|
| Gas-hit brawl · Princess (`princess-gas-brawl`) | Hit this CPU opponent with Gas from the human side in an Elite or Boss campaign match. Native offer timing and once-offered limits apply; equip Gas separately. |
| Grapple-hit brawl · Mountaineers (`mountaineer-grapple-brawl`) | Hit this CPU opponent with Grappling Hook from the human side in an Elite or Boss campaign match. Native offer timing and once-offered limits apply; equip Grappling Hook separately. |
| Kazoo-hit brawl · Referees (`referee-kazoo-brawl`) | Hit this CPU opponent with Kazoo from the human side in an Elite or Boss campaign match. The native offer delay is randomly 1 to 2 seconds; once-offered limits apply. Equip Kazoo separately. |
| Breakaway-goal brawl · Tycoons (`tycoons-breakaway-brawl`) | Score a human-side breakaway goal against this CPU opponent in an Elite or Boss campaign match. Arms an offer for the next faceoff; the game also accepts its recent breakaway-shot record for up to 5 seconds. Native offer limits apply. |
| Power-use brawl · Shooting Stars (`stars-power-brawl`) | Use Regular Jump or start Tornado on the human side against this CPU opponent in an Elite or Boss campaign match. Does not react to every ability. Equip the qualifying ability separately; native offer limits apply. |
| Hat-trick brawl · Lettuce (`hat-trick-brawl`) | Score a hat trick with one human-side player against this CPU opponent on an Elite campaign node. Arms the native brawl offer for the next faceoff. Boss and Challenge nodes do not qualify. |
| Knockout brawl · Calaveras (`calaveras-brawl`) | On an Elite campaign node, use the controlled player to knock out this CPU opponent during Faceoff or GoalScored celebrations. Arms a native offer for the next faceoff. Ordinary live-play knockouts do not satisfy this trigger. |
| Knockout brawl · Meatballs (`meatballs-brawl`) | On an Elite campaign node, use the controlled player to knock out this CPU opponent's current breakaway skater. A normal knockout away from a breakaway does not qualify. Native offer gates remain active. |
| Bodycheck brawl · Crusaders (`bodycheck-brawl`) | On an Elite campaign node, bodycheck the CPU opponent's Prince, or a player marked Counts as Prince for Crusader brawls, during charged-shot windup using your controlled player. Native offer delay is 2 seconds; other offer limits remain active. |
| Period-opening brawl · Princess (`princess-brawl`) | Arms the native period-opening brawl against a CPU visitor on an Elite campaign node. The offer waits for the native faceoff/offer flow. Boss and Challenge nodes do not qualify. |
| Injury brawl · Hockey FC (`soccer-brawl`) | Offers the native injury brawl when a skater belonging to this CPU opponent receives an injury on an Elite campaign node. A knockout without injury does not qualify; Boss and Challenge nodes do not qualify. |

### Special rules

| Option | Behavior and requirements |
|---|---|
| Golfer boss showdown (`golfer-showdown`) | Enables the native golfer showdown for a CPU visitor on a campaign Boss node. Elite and Challenge nodes do not qualify. Preserves native presentation and offer timing; does not mark the match as the campaign finale. |
| Preserve Chaos relic duration · Spartans (`spartan-chaos`) | Prevents the player's existing Chaos relic from losing a remaining match when this opponent match ends. Requires the active Chaos relic; does not grant it or change its other rules. |
| Player goalie bonus against Cultists (`cultist-goalie`) | Adds the native temporary +10 size-stat bonus to the HOME goalkeeper in a campaign match against this opponent. It does not increase this opponent's goalkeeper or permanently edit goalie stats. |
| Difficulty-scaled goalie size · Spartans (`spartan-goalie`) | Applies the native campaign difficulty-dependent goalkeeper equipment stats to this team. Native difficulty rules can override authored goalie equipment sizes; other game modes retain their normal sizing. |
| Golfer defense roster preparation (`golfer-rotation`) | Prepares native golfer defense pairs using GolferDefenseConfig and compatible roster identities. Can replace the defense pair with native configured players. Keeps the defensive formation separate. The current base game does not enable automatic goal or tier swaps. |
| Shooting Stars breakout decisions (`stars-breakout`) | Enables the Shooting Stars branch while the AI puck carrier evaluates breakout decisions. The native movement state, targets and ability checks still apply; no ability is granted. |
| Referee-team official selection (`referee-selection`) | Chooses the native alternate official used for Referee teams at match setup. Does not give players referee talents, rigged faceoffs or goal denial. |
| Spartan official selection (`spartan-selection`) | Chooses the native official used against Spartans when the game selects a referee. Existing referee-selection priorities and roster/relic checks remain in effect. |
| Crusader puck collision response (`crusader-puck`) | Uses the Crusader branch when the puck collides with the affected skater. Native collider, shot and collision-state checks still determine the response; puck selection is separate. |
| Soccer corner faceoffs (`soccer-corner`) | Enables the native soccer corner-faceoff choice when either team has this rule. The game still decides when and where a faceoff is needed; ball and surface choices are separate. |
| Referee-team faceoff handling (`referee-faceoff`) | Applies the Referee team's native chance check to an already active Rigged Faceoff relic at faceoff setup. Does not grant the relic or make every faceoff rigged. |
| Referee-team goal-line handling (`referee-goal`) | Uses the Referee exception in native campaign goal validation, including bypassing the No Goal talent-denial branch for this defending team. Other goal-denial relics and ordinary scoring checks remain separate. |
| Soccer goalkeeper saves (`soccer-goalie-saves`) | Uses native Hockey FC desperate-save and standing glove/blocker save animations and handling. The native shot direction, goalkeeper stance and save timing still apply; does not guarantee a save. |
| Golfer goalkeeper passing (`golfer-goalie-pass`) | Uses the native golfer chip-pass choice when this team's goalkeeper passes. Normal possession, pass timing and goalkeeper animation rules still apply. |
| Prisoner boss ball and chain (`prisoner-chain`) | On a campaign Boss node, native setup keeps one Ball and Chain on this team, adding it to the first eligible skater if needed, and assigns a chain to the other team's golden-helmet scorer. Native scoring updates and cleanup remain active. Elite and Challenge nodes do not run this setup. |
| Prisoner ball-hit immunity (`prisoner-ball-immunity`) | Uses native prisoner protection from ball hits during GoalScored and celebration movement. Requires an actual Ball and Chain hazard; does not make every skater permanently immune to all collisions. |
| Prisoner rope-hit immunity (`prisoner-rope-immunity`) | Uses native prisoner rope-hit eligibility, including protection during GoalScored/celebrations and the prisoner-specific late hit gate. Requires a rope hazard; other collision rules remain native. |
| Prisoner rope knockback (`prisoner-rope-knock`) | Uses the prisoner-specific force calculation when the rope hit handler actually applies knockback. Rope immunity can prevent the handler reaching this calculation; no hazard is created by this option. |
| Referee Hands interaction (`referee-hands`) | When Referee Hands is equipped and active, this opponent keeps its sticks. Auto and On permit the native immunity; Off permits normal stick removal. Does not grant the relic. |
| Suppress Middle Management bonus · Spartans (`spartan-middle-management`) | Uses the native Spartan exception: a player's Middle Management talent receives no opponent-based bonus against this team. Requires the player to have Middle Management; does not grant it. |
| Suppress elite bench bonus · Spartans (`spartan-elite-bench`) | Uses the native Spartan exception that prevents the player's elite-match bench bonus from applying against this opponent. Requires that bench power; Boss matches already suppress the same bonus. |
| Golfer boss tiers (`golfer-tiers`) | Uses native golfer tier progression and transformations. Transforming skaters need the native Cornelius/Pam asset identity plus a Cultist transformation talent with an evil-twin target. A generic roster or renamed display name does not supply those requirements. Does not make this match the campaign finale. |
| Golfer puck pickup rules (`golfer-pickup`) | Uses golfer pickup height and puck-motion allowances inside the native pickup rule. Pickup locks, movement-state checks and recent goalkeeper saves still apply; does not force possession. |
| Golfer stick-break immunity (`golfer-stick-break`) | Uses the native golfer exemption from breaking a skater's stick. The option prevents the normal break-stick coroutine for qualifying skaters; it does not grant a stick if one is already absent. |
| Golfer sand-shot immunity (`golfer-sand`) | Uses the native golfer exemption from sand-shot slowdown. A shot must reach the native sand calculation; enable Golf ice and Golf Fairway separately to create the terrain. |
| Golfer fairway-shot handling (`golfer-fairway`) | Applies the native golfer shot-speed boost when Golf Fairway is active and the puck is on fairway terrain. Does not create the terrain or boost shots taken elsewhere. |
| Soccer knockout handling (`soccer-knockout`) | Uses native Hockey FC knockout entry, movement and recovery handling. The player must actually be knocked out; injury, immunity and recovery timing remain native. |
| Soccer fight health (`soccer-fight-hp`) | Applies the native SoccerTeamHpMultiplier when calculating this team's maximum fight health. Other fighter scaling and showdown multipliers remain active; does not start a brawl. |
| Soccer collision handling (`soccer-hits`) | Uses the native Hockey FC random collision-response branch on a qualifying bodycheck. Normal hit eligibility, checking stats, movement state and immunity still apply; the response is not guaranteed on every hit. |
| Golfer offensive AI (`golfer-offense`) | Selects native golfer offense and its breakout/puck-carrier decisions. Replaces the selected normal offensive formation. Native AI state and ability requirements remain active. |
| Soccer breakout AI (`soccer-breakout`) | Uses native Hockey FC breakout and breakaway shooting decisions. Requires the corresponding AI state and possession situation; does not change player abilities, the ball or the surface. |
| Golfer offside offense exception (`golfer-offside`) | Uses the native golfer exception when AI selects offside offense and updates the rush window. This changes AI decisions; it does not enable or disable the match offside rule. |
| Soccer offside offense exception (`soccer-offside`) | Uses the native Hockey FC exception when AI selects offside offense and updates the rush window. This changes AI decisions; it does not enable or disable the match offside rule. |
| Golfer transformation-AI eligibility (`golfer-transformation`) | Uses golfer Cultist transformation eligibility: tier-managed skaters stay under the tier system; other qualifying skaters wait until period 2. Requires the native Cultist transformation talent and target; does not grant them. |
| Prisoner Wet Towel AI (`prisoner-towel`) | Allows native prisoner Wet Towel AI when the ability is ready and a target is eligible. Requires Wet Towel on the player; puck-for-grabs and other native state gates still apply. |
| Golfer skater passing (`golfer-pass`) | Uses the native golfer trajectory adjustment for skater passes. A skater with the cannot-lift-the-puck talent retains that restriction. Goalkeeper passing has its own option. |

### Appearance and sound

| Option | Behavior and requirements |
|---|---|
| Challenge timeline · Spartans (`spartan-timeline`) | Uses the Spartans challenge classification for timeline timestamps, including remaining-time formatting. This is a timeline display rule; match length, scoring and the map node stay unchanged. |
| Hockey FC home / away colours (`soccer-kit`) | Uses Hockey FC home/away uniform and number-colour rules when this team and its skaters initialize. Roster appearance remains separately authored; this does not supply a soccer ball or field. |
| Hide skater jersey numbers · Referees / Mountaineers (`hide-jersey-number`) | Omits the skater jersey number in the native Referee/Mountaineer skin-building branch. Applies when skins are built; goalie numbers and other uniform markings are separate. |
| Golfer skater body style (`golfer-kit`) | Uses the golfer body style when the native skater skin is built. Player head, helmet and equipment settings remain separate. |
| Hide skater logos and numbers · Shooting Stars / Crusaders (`hide-kit-markings`) | Omits skater team logos and jersey numbers in the native Shooting Stars/Crusader uniform initialization branch. Does not remove the goalkeeper name or skater names restored after a knockout. |
| Crusader shot sounds (`crusader-shot-sound`) | Uses the Crusader shot sound branch when this team shoots. Native shot type and Prince-specific sound distinctions are retained; does not grant Prince identity. |
| Crusader slapshot charging sound (`crusader-charge-sound`) | Uses the Crusader sound branch when a skater starts charging a slapshot. Native Prince-specific sound distinctions remain separate from the brawl-only Prince checkbox. |
| Shooting Stars jump visuals (`stars-jump-vfx`) | Plays the Shooting Stars jump effect when a player actually launches a jump. Requires a jump-capable player; does not grant Jump. |
| Hockey FC goal celebrations (`soccer-celebration`) | Uses native Hockey FC celebration animation and AI movement after a goal. Requires the actual celebration state; does not affect scoring or grant abilities. |
| Shooting Stars celebration movement (`stars-celebration`) | Uses Shooting Stars AI movement during native goal celebrations. Does not change the score or select the team's goal sound. |
| Referee celebration movement (`referee-celebration`) | Uses Referee AI movement during native goal celebrations. Does not change the score, referee selection or goal sound. |
| Hockey FC knockout sounds (`soccer-ko-sound`) | Uses Hockey FC knockout audio when a qualifying skater knockout occurs. Other native sound priorities and player-specific sounds still apply. |
| Golfer match and presentation sounds (`golfer-audio`) | Uses golfer audio branches at match setup, team presentation, between periods and at match end. Does not change the selected goal sound or make the match a finale. |
| Hockey FC goalkeeper passing animation (`soccer-goalie-animation`) | Uses Hockey FC goalkeeper passing animation when the native pass animation is requested. Possession and pass physics remain separate. |
| Spartan rink cone setup (`spartan-cones`) | Applies the native Spartans setup to cones already present in the rink. Does not create a cone relic or new obstacles; the effect depends on the selected rink objects. |
| Spartan crowd sounds (`spartan-crowd`) | Chooses the native Spartans crowd-audio arena mode. Requires the normal crowd sound system and its timing; does not select a different rink. |
| Golfer rink advertisements (`golf-ads`) | Uses the native golfer advertisement selection at rink setup. Requires the rink's advertisement objects and assets; surface selection is separate. |
| Hockey FC rink advertisements (`soccer-ads`) | Uses native Hockey FC advertisement selection at rink setup. Requires the rink's advertisement objects and assets; surface selection is separate. |
| Hide rink logo · Golfers (`golf-logo`) | Hides the rink logo through the native golfer logo branch. Does not change the team's stored logo or player uniform logo settings. |
| Hide rink logo · Hockey FC (`soccer-logo`) | Hides the rink logo through the native Hockey FC logo branch. Does not change the team's stored logo or player uniform logo settings. |
| Hide skater jersey names after knockouts · Shooting Stars (`stars-ko-skin`) | Omits skater jersey-name lettering when the native knockout coroutine restores the uniform. Initial uniform logos/numbers are controlled separately. |
| Hide goalkeeper jersey name · Shooting Stars (`stars-goalie-skin`) | Omits goalkeeper jersey-name lettering when the native goalkeeper skin coroutine initializes. Does not remove skater names or change goalie stats. |
| Disable anthem skip prompt · Golfers (`golf-anthem`) | Disables the usual anthem skip prompt and input listener through the golfer branch. Does not automatically skip the anthem or change its audio. |

## CPU abilities

The opponent picker exposes twenty assets in twelve ability families with established normal-hockey AI consumers in this build. Upgraded and team-specific variants use their native parameters. Assigning an ability enables its controller; AI possession, distance, movement, available uses, cooldowns, target validity and tactical decisions still determine whether it activates.

| Family | Native use |
|---|---|
| Gas | Defensive AI evaluates the carrier or nearby shot/pass and spends a use when the native state and distance checks pass. |
| Grappling Hook | Requires the native CPU/locked-control, target, faceoff-grace, movement, distance and timing conditions. |
| Tomahawk / Mjolnir+ | Requires the throwing-stick ability, a stick, an opposing carrier and eligible movement. Native prediction, range and chance determine the throw. |
| Jump | Breakout AI reacts to an incoming hit after its possession-time, motion and controller checks. |
| Spin-O-Rama | Breakout AI chooses a spin within its native possession and target decisions. |
| Mega-Sauce | Breakout AI selects the lob in its normal possession and shooting logic. |
| Yo-Yo | Carrier AI checks possession time, offensive-zone positions, timing, chance and a suitable teammate/goal situation. |
| Prod | Defensive AI can use the controller instead of an eligible bodycheck. |
| Tornado | Requires the equipped controller, CPU control and valid native match/movement states. |
| Wet Towel | General defensive hit decisions can use it. The separate towel-chasing behavior additionally needs the prisoner-towel effect and its native target/state conditions. |
| Headshot Redirect | Normal breakout/shooting decisions check the equipped redirect ability and native aiming rules. |
| Mega Pokecheck | A passive native pokecheck/prediction effect, not a separately scheduled cast. |

The accepted native assets are Headshot Redirect and LVL2; yoyo and YoYo LVL2; tornado; probe; spinorama and spinorama Shooting Stars; Grappling Hook, LVL2 and Mountaineer; throwingStick and LVL2; Gas and LVL2; jump and Jump LVL2; wet_towel; megaLob; and megaPokecheck.

Other abilities may work for a human player or in the separate brawl AI without having a normal-hockey CPU activation path. The picker therefore hides unsupported new assignments. A previously saved unsupported assignment remains visible and preserved until explicitly changed. Ordinary CPU deking does not prove use of the active Deke ability, and the StickThief talent's AI does not establish support for the active Stickpocket ability.

## Verification scope

The release audit resolved all seventy effect settings across ninety-one distinct native consumer hooks, checked the current native prerequisites, and reviewed the twelve supported CPU ability families. Technical checks covered actual editor persistence, scoped Calaveras identity routing, copied reserves, native empty helmets and helmet selection policy. These establish source/adapter behavior; they do not simulate a human gameplay confirmation.

The existing human report confirms Crusader normal-shot and charge sounds. That report applies to the audio routes; it does not establish the separate Prince/bodycheck brawl requirements.

There is no need to replay every checkbox, every talent level or every roster slot merely because its name differs. Shared adapter, serialization and scope checks apply across options using that same code. A useful final human sample focuses on distinct changed paths:

1. **Helmet lifecycle:** compare an imported native bareheaded Golfer with a skater explicitly given Team colors or Wing; confirm the starting appearance, and that a native drop/restoration changes the head and world equipment correctly when it occurs.
2. **Crusader brawl:** on an Elite node with that effect enabled, use the controlled player to bodycheck the marked CPU Prince during charged-shot windup; expect the native delayed offer when its offer gates permit it. The same mark should not change unrelated Prince behavior.
3. **Queued offer and copied Off:** use one qualifying next-faceoff trigger, then confirm a copied Calaveras team with the effect explicitly Off does not use its alternate native campaign identity to bypass Off.
4. **Special native setup:** if included in a campaign, sample a copied Golfer boss's native tier/terrain path or the prisoner Boss hazard setup and cleanup. These systems have separate roster/equipment prerequisites from ordinary effect routing.
5. **Integration:** confirm a configured second line once, one relic interaction with its actual relic, and a return to a normal next match. Save/Continue and installation are separate product integration checks.

These are representative checks, not seventy required matches or guarantees that a stochastic action will occur immediately. Unobserved native paths remain documented as native-code-derived behavior. Re-audit the native integration when Tape to Tape updates.

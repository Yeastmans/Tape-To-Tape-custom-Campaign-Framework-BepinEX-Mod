using Avalonia.Controls;
using Avalonia.VisualTree;
using CampaignCore;

namespace CampaignCreator;

public sealed partial class MainWindow
{
    private PageHelpPopup? pageHelp;

    private void InitializePageHelp()
    {
        KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.F1) { ShowPageHelp(); e.Handled = true; } };
    }

    internal void ShowPageHelp()
    {

        pageHelp?.Pause();
        pageHelp = PageHelpButton.Show(this, PageHelpSteps());
    }

    // Targets are tried in order and the first one on screen gets the popup, so later
    // targets are fallbacks (for example Save player team / Save opponent).
    private IReadOnlyList<PageHelpPopup.Step> PageHelpSteps()
    {
        var steps = new List<PageHelpPopup.Step>();
        void Add(string title, string explanation, params string[] targets) =>
            steps.Add(new PageHelpPopup.Step(title, explanation, targets));
        string SelectedTab(Control view) => view.GetVisualDescendants().OfType<TabItem>()
            .FirstOrDefault(t => t.IsSelected)?.Header?.ToString() ?? "";
        bool Has(Control view, string name) => view.GetVisualDescendants().OfType<Control>().Any(c => c.Name == name);

        if (Content is CampaignWorkspace campaign)
        {
            if (!campaign.IsNew) Add("Delete campaign", "Permanently removes this campaign from this computer after confirmation. Cancel is the default. Close Tape to Tape first. Shared libraries and custom heads are kept. If this is the selected campaign, Home switches to None (base game).", "DeleteCampaign");
            switch (campaign.CurrentSection)
            {
                case 0:
                    Add("Campaign name", "Name the new campaign here. The name becomes its folder when you save.", "Name your campaign");
                    Add("Campaign overview", "Each card sums up one part of this campaign and opens it. Back returns to Home; Save campaign keeps the changes made in any section.", "Save campaign");
                    Add("Needs attention", "Lists opponents that are on no map node and nodes whose team no longer exists. Each link jumps to the fix.", "CampaignAttention");
                    Add("Opponents", "The teams this campaign plays against. Each one shows its map node, or No map node in red. + New opponent creates one.", "CampaignOverviewSection1");
                    Add("Custom teams", "Click a team to edit it, or use + Add player team. Open superstar/bench options opens the shared starting pools. Each team controls its own starting screens in Squad behaviors. Opening or adding a player team saves the campaign's working changes first.", "CampaignOverviewSection3");
                    Add("Maps", "The campaign route. Each map shows how many match nodes have a campaign team; empty match nodes use the game's own opponents.", "CampaignOverviewSection2");
                    Add("Free agents", "Shared players who can be recruited during a run. An explicitly saved empty pool offers no recruits. A squad can use separate choices from its Player pools tab.", "CampaignOverviewSection4");
                    Add("Reward pools", "Which relics, talents and abilities random rewards can offer. All means the full base-game pool.", "CampaignOverviewSection5");
                    Add("Game options", "Offside in every match turns on the game's own offside rule for the whole run without changing your saved settings. Offside penalty picks Whistle, Lose Puck or Knockout, or keeps the player's own setting. Tchel squad rules and Linesman retain their native Knockout precedence. Squad rules are in Custom teams → Squad behaviors; opponent and arena rules are in Opponent settings. Save campaign keeps it.", "CampaignOffside");
                    break;
                case 1:
                    Add("Opponent teams", "New opponent team creates a team in this campaign. Edit team opens its settings, look, roster and match behavior.", "New opponent team");
                    Add("Map assignment", "Green teams play on at least one map node; red teams are on none. Pick a node to assign this team, replacing the node's previous team. × beside a node clears its draft assignment; save maps or campaign to keep it. × at the right removes the team immediately, keeping its folder in _removed_opponents. Reassign any nodes that used it.", "OpponentList");
                    Add("Import a team", "Import opponent team copies a base-game, in-game or library team into this campaign. This copies editable fields, not native identity checks. Copy vanilla campaign on Home preserves native identities. The source is not changed.", "Import opponent team");
                    break;
                case 2:
                    Add("Map selection", "The tabs at the top are the campaign's maps in play order. Map look chooses the act artwork for the selected map.", "MapLook");
                    Add("Generation", "Both modes use this editable layout. Vanilla random starts from the imported map's event pools and opponents; each random node rolls from its own choices on a new run. Open Random event options on that node to customize its pool.", "MapGeneration");
                    Add("Map intro", "Map name appears on the opening wipe. A blank name keeps the game's act name. ACT numbering follows campaign order, so map four shows ACT 4 with any scenery. The opening pan follows the resized map.", "MapName");
                    Add("Add a map", "+ Add map adds another stage; × on a map tab removes it.", "+ Add map");
                    Add("Import a map", "Replaces the selected map with a base-game layout or a map from another campaign. Undo reverts it.", "Import base-game map");
                    Add("Node builder", "Click a node to set its type, team, stadium, rewards or random events; changes go into the map right away. Connect links a node to the next layer and layers support additional choices, with closer rows for three or more nodes. Every route must reach the final Boss.", "NodeBuilderViewport", "Node builder");
                    Add("Game preview", "Estimates the map using game artwork, team logos and stadiums. Native artwork bounds, decorations and animations resolve in game; this projection does not run the campaign.", "Game preview");
                    Add("Undo, redo and view", "Undo and Redo step through this map's edits, including team, stadium and node-type changes. Fit and zoom only change the view.", "Undo");
                    Add("Save maps", "Checks every route and writes the maps (Ctrl+S). Save campaign saves them too. Saved map changes apply to new runs.", "Save maps");
                    break;
                case 3:
                    Add("Team presets and behaviors", "Each player team has its own template preset. Open the team to change its preset, behavior overrides and editable starting relics.", "CampaignPlayerTeamList");
                    Add("Player teams", "Click a team to edit its settings, look and roster. Add player team creates one; Import player team from library copies a saved library team into this campaign.", "CampaignPlayerTeamList");
                    Add("Use player teams", "Makes these teams playable; turning it off keeps them saved but unused. Save campaign keeps the switch.", "UsePlayerTeams");
                    Add("Custom teams only", "Hides the base-game squads for this campaign. Save campaign keeps the switch.", "CustomSquadsOnly");
                    Add("Campaign team details", "Expand to read the selected template's setup, editable roster, starting choices and default maps. Each team has independent Superstar selection and Bench draft controls.", "Campaign team details");
                    Add("Starting bench", PoolChoiceGuidance.Bench + " Use base-game bench switches between the game's own nine and your custom cards; only the visible set is used. Save campaign, then reopen the game's bench selection to load those choices.", "StartingBenchPool");
                    Add("Starting superstars", PoolChoiceGuidance.Superstars + " Create or import a player into each card; Remove clears a card without deleting the player. Save campaign keeps both pools.", "StartingSuperstarPool");
                    break;
                case 4:
                    Add("Disable extra recruit talents", "Stops random extra talents on all new free-agent offers, including extras already cached in this run and random lists in imported recruits. Fixed player talents, abilities and already signed players remain unchanged. Your checked talent choices and weights stay saved and apply again when this is Off. Save campaign keeps the setting, including in exports.", "DisableExtraRecruitTalents");
                    Add("Keep authored stats", "Applies to all new free-agent offers, including squad-pool and base-game recruits. On preserves the source player's four base stats; Off keeps native GM scaling. Talent choices remain independent, and already recruited players keep their current stats. Save campaign keeps this setting, including in campaign exports.", "KeepFreeAgentStats");
                    Add("Free-agent talent pool", "Each checkbox controls one talent level for random extras on new recruits and contracts. Unchecked levels cannot roll or appear through upgrades, including random talent lists in imported recruits. Fixed player talents stay intact. No checked talents gives no random extras; an explicitly empty recruit pool offers no players. This pool and its multipliers are independent of Regular and Chaos rewards. Restore base talent pool checks the game's normal base and upgrade levels without resetting multipliers.", "FreeAgentTalentWeightsPanel");
                    Add("Base-game free agents", "Check the base-game players who can be recruited during a run.", "BaseFreeAgents");
                    Add("Custom free agents", "Check each custom player who can be recruited. Unchecked players remain available to edit with Edit player. Import copies a player into this campaign; Make New creates one. Both start checked.", "CustomFreeAgents", "Import", "Make New");
                    Add("Save selection", "Save campaign keeps both base-game and custom selections. An explicitly saved empty pool offers no recruits. General Manager nodes on your maps offer recruits.", "Save campaign");
                    break;
                case 5:
                    if (SelectedTab(campaign) == "Creator") {
                        Add("Create an item", "Choose regular or Chaos talents or relics. Talents are owned by their reward recipient; relics by the team. Abilities and goalie talents are excluded from this creator.", "CustomRewardName", "CustomRewardKind");
                        Add("Icon", "Choose a PNG up to 2048 pixels per side and 4 MB, or use the rectangle containing the item's name. The image travels with the campaign copy.", "Choose PNG…");
                        Add("Rule", "Choose an existing gameplay event, the affected stat and recipient. Counts reset each match. Duration controls the triggered stat effect; the talent or relic stays owned. Chaos relics have a curse followed by a lasting payoff.", "CustomRewardTrigger", "CustomRewardRules");
                        Add("Save and offer", "Save item creates a reusable library entry and an independent campaign copy. Enable the item in its pool and Save campaign. Current native Wizard choices are Chaos talents; the Chaos relic pool does not add Wizard offers.", "SaveCustomReward");
                        break;
                    }
                    Add("Reward categories", "Relics, regular talents, goalie talents, abilities, Chaos talents and Chaos relics have separate tabs. The Creator tab makes custom talents and relics. Normal relic and talent offers have 3 choices; the Wizard offers up to 2 Chaos talents. The Chaos relic tab only controls native Chaos relic requests and adds no new Wizard choices.", "RewardSections");
                    Add("Base-game pool", "Checked: native eligibility for this reward type, including the wizard's native Chaos category. Unchecked: random offers draw only from your checked entries. Custom selections are retained when switching modes. Explicit upgrades of owned rewards use separate native upgrade rules.", "BaseGamePool");
                    Add("Include the other category", PoolChoiceGuidance.CatalogExpansion, "IncludeOtherRewardCategory");
                    Add("Pool size", "Choose enough different rewards for a normal offer: three relics or talents, two Chaos talents, or one ability. Upgrade levels of the same talent count together. Campaign length does not increase this minimum. Select all and Deselect all affect every entry in the current tab, including entries hidden by search.", "RewardPoolCount", "SelectAllRewards", "DeselectAllRewards");
                    Add("Reward choices", PoolChoiceGuidance.RewardEligibility + " Save campaign validates and keeps the choices.", "Save campaign");
                    break;
            }
        }
        else if (Content is PlayerEditorView player)
        {
            bool goalie = role == "goalie";
            Add("Player identity", "Name identifies this player in rosters and appears on the jersey. Number is the jersey number; a name or appearance does not grant a native player identity.", "Name");
            Add("Player number style", "Inherit team follows the team default. Regular, Roman numerals or Prison overrides it for this player, independently of Team equipment. Native imports preserve each player's format. The mod also preserves the resolved style in Play Now exports.", "PlayerNumberStyle");
            Add("Preview team", "Superstar choices can join any of the campaign's player teams. Pick which team's kit the preview uses.", "SuperstarPreviewTeam");
            Add("Team equipment", "On: the player wears the team's equipment styles. Off: choose this player's own styles. Kit colors always come from the team; a player with no team previews in your default look.", "InheritFromTeam");
            if (SelectedTab(player) == "Details & attributes")
            {
                Add("Details and attributes", goalie
                    ? "Skill and the goalie stats: catching, glove, blocker, five hole, standing and butterfly speed, control, recovery, pass power, shot power, poke check, depth and Pass Read. Position comes from the roster slot."
                    : "Size, size offset (body scale; blank is normal), handedness, speed, shot power, accuracy, checking and an ability. Position comes from the roster slot.", "Details & attributes");
                if (goalie) Add("Pass Read", "Native range 0–1. Zero uses normal pass reaction. Any positive value enables quick pass reading only when the current difficulty permits it; higher values do not scale this effect. Save changes keeps the value; Play Now exports retain it through companion data.", "Pass Read");
                Add("Talent choices", "Choose talents the game supports for this player's role.", "Talents", "Goalie Talents");
                if (!goalie) Add("Prince brawl target", "Qualifies this player only for the Crusader bodycheck brawl. The opponent needs Bodycheck brawl enabled on an Elite node; hit this player during charged-shot windup. Other Prince behaviour is unchanged.", "CrusaderBrawlPrince");
            }
            else
            {
                Add("Home and away", "Switch the kit you preview. With Team equipment off, this also chooses which kit's styles you are editing.", "PlayerKitTabs");
                Add("Front and back", "Changes only the preview angle.", "PlayerFacingTabs");
                if (goalie)
                    Add("Goalie equipment", "With Team equipment off, choose the mask, jersey, pants, pads, stick, catcher and blocker. Choose Team, Custom or Premade for each kit piece; Custom colors have separate home and away values. With it on, the team's goalie gear is used.", "PlayerEquipment");
                else
                    Add("Head and equipment", "Choose the head; some heads have no selectable helmet variant. Team colors equips the ordinary native helmet without a talent or stat bonus. None removes that ordinary starting helmet; talent, transformation and reward helmets keep their native rules. Home Helmet controls physical starting equipment; Helmet Away does not independently equip, remove or replace the physical helmet. Loss, drops and restoration use native equipment state. Selecting the native Wing style equips its matching equipment without adding a talent; native Wing drop rules apply. Other special helmet artwork requires its native equipment source. With Team equipment off, choose helmet, jersey, sleeves, gloves, pants and socks. Each piece offers Team, Custom colors or Premade artwork; Custom colors are saved separately for home and away. In Premade mode, Biceps / sleeves changes the sleeves separately; Same as jersey lets socks follow the jersey. The stick style is always the player's. Skates have no style.", "Face", "PlayerEquipment");
                Add("Player logo", "With Team equipment on, the player wears the team logo. With it off and team-color jerseys home and away, Choose player logo sets their own and Use team logo reverts. Premade jerseys have no player logo.", "PlayerLogo");
                if (goalie)
                {
                    Add("Stick colors", "The goalie's own stick colors, one set for home and away. Blank uses the default stick colors.", "GoaliestickPlayerColors");
                    Add("Blocker colors", "The goalie's own blocker colors, one set for home and away. Blank follows the team kit.", "BlockerPlayerColors");
                }
                else
                {
                    Add("Stick colors", "The player's own stick colors, one set for home and away. Blank uses the default stick colors.", "StickPlayerColors");
                    Add("Skate colors", "The player's own skate colors, one set for home and away. Blank follows the team kit.", "SkatesPlayerColors");
                }
            }
            Add("Export to Play Now", "Copies this player into the game's own custom content for Play Now with a separate player ID. Numbered copy names are retained. It uses the current fields but does not save them here, so save first. Restart the game to see it.", "Export to Play Now");
            Add("Save player", "Saves this player. Fields listed under Needs attention must be filled first. Home and Back do not save: unsaved edits are discarded.", "Save changes");
        }
        else if (Content is TeamEditorView team)
        {
            bool defaults = Has(team, "SaveDefaultLook");
            switch (SelectedTab(team))
            {
                case "Team settings":
                    Add("Team identity", "Team name, city, abbreviation and description. The description shows in the map preview. The logo is in Team look.", "Team Name");
                    Add("Squad template", "Sets only this team's preset. In Squad behaviors, Inherit uses its native defaults; On and Off override each rule independently.", "PlayerTeamSquadTemplate");
                    Add("Injuries", "Player teams only. On by default except for Oldschool. Selecting a different squad restores its native default; change the checkbox to override it and save the player team.", "PlayerTeamInjuries");
                    Add("Mascot", "Choose the base-game squad mascot (plushie) for this player team. Save the team to keep it.", "Mascot");
                    Add("Squad icon", "Choose a head or custom logo image for this player team's squad card. This is separate from its team logo and roster heads.", "Choose squad icon");
                    Add("Zamboni head", "Choose this player team's Zamboni presentation head. Save the team to keep it.", "Zamboni Head");
                    Add("Transition colors", "Colors of the screen transition shown for this team.", "Customise transition colors");
                    Add("Goal celebration", "Choose audio file turns a clip of up to 60 seconds into this team's goal sound. Preview and Stop play it here and the slider sets its volume. Use base game sound goes back to the default. Save the team to keep it.", "Choose audio file");
                    Add("Stat scale", "Multiplies skater speed, shot power, accuracy and checking, and the goalie's numeric attributes except Pass Read. 1 or blank leaves them unchanged.", "Stat Scale");
                    break;
                case "Team roster":
                    Add("Roster slots", "Each card is a position. The preset and Squad behaviors decide roster setup and lines. Stored players remain available when those rules change. + New player creates a player, Import player copies one in, and opening a card edits that player.", "+ New player", "Team roster");
                    Add("Prepare two-line roster", "With the team saved, check Prepare two-line roster to open all ten skater slots without changing gameplay or writing the team. Fill both lines and the goalie with uniquely named players, then enable Two-line roster flow in Squad behaviors and save. Missing players block a two-line team's save. Starting drafts are skipped and in-match lineup editing is locked.", "PrepareTwoLineRoster");
                    Add("Choose active starters", "Start active chooses which authored skaters begin on the ice. When a three- or four-skater limit is enabled, choose the active starters before saving. Unchecking keeps that player on the starting bench and preserves their file. Leave an active slot open if the starting superstar will fill it. Two-line roster flow requires every authored slot active and locks these controls on.", "StartingActive_LeftWing", "StartingActive_Center", "StartingActive_RightWing", "StartingActive_LeftDefense", "StartingActive_RightDefense");
                    Add("Team relics", "Relics this team carries in game. × removes one; save the team to keep changes.", "+ Add relic");
                    Add("Add template starting relics", "Copies the selected preset's base-team relics into this editable list. Repeated clicks add no duplicates; after removing a relic, clicking again restores it. Remove any effect you do not want and save the team. Behavior overrides and the relic list are separate settings.", "AddTemplateStartingRelics");
                    Add("Save roster", "Save the team after removing players or changing settings. Each player saves in their own editor. A team with unsaved changes can't open or add players.", "Save player team", "Save opponent");
                    break;
                case "Squad behaviors":
                    Add("Preset and overrides", "Inherit uses this team's template default. On and Off override it for this team, and stay set when its preset changes. The Template default and Effective labels show the resulting rule. Two-line roster flow locks its required starting-screen and pause-lineup values; turn it off before changing those choices. Search filters names and descriptions. Starting relics are edited separately in Team roster.", "SquadBehaviorSearch");
                    Add("Conflicting rules", "Resolve the listed conflicts before saving. Some rules require other controls, a particular roster size or starting-draft setup. No players are deleted when you change these settings.", "SquadBehaviorConflicts");
                    foreach (var behavior in SquadBehaviors.All.Where(b => b.Id != "no_injuries"))
                        Add(behavior.Name, behavior.Description + " Inherit follows this team's preset; On and Off explicitly override it. Save player team keeps the choice.", "Behavior_" + behavior.Id);
                    Add("Save behaviors", "Save player team validates dependencies and active starters. Save the campaign after adjusting its reward pools. Restart the game to load saved settings and start a new run to test roster and starting-screen changes.", "Save player team");
                    break;
                case "Player pools":
                    Add("Use own pools", "Off uses shared campaign choices. On uses this squad's separate bench, superstar and free-agent selections. New own pools start empty; turning the checkbox off keeps them for later.", "UseOwnPlayerPools");
                    Add("Choose players", "Choose a pool tab, then add or import players. Only selected candidates are offered. Empty starting pools skip their screen; an empty free-agent pool offers no recruits.", "SquadPoolKinds");
                    break;
                case "Opponent settings":
                    Add("Formations", "Offensive and defensive formations for this opponent.", "OpponentOffense");
                    Add("Puck and surface", "The puck or ball and the playing surface for matches against this opponent.", "Match Puck");
                    Add("Two-line behavior", "Uses the game's opponent line changes. Fill all five Line 2 positions in Team roster first.", "TycoonsTwoLines");
                    Add("Canada curses", "Uses Team Canada's native curse-selection sequence against this opponent. Native timing, repeat-pick limits and player-squad curse exemptions apply. Save the opponent and restart the game to load this choice.", "CanadaCurses");
                    Add("Special effects", "Tick effects to stack them; a conflicting alternative stays disabled until the current one is unchecked. Search filters names and requirements. Each effect's help lists its node, ability, identity and trigger requirements; enabling an effect does not equip abilities or bypass native gates. Effects happen in matches; nothing is simulated here.", "EffectSearch");
                    break;
                default:
                    Add("Team logo", defaults
                        ? "The logo new player teams and opponents start with."
                        : "Choose logo sets the team's crest. It shows on the jerseys, in menus and, for opponents, above their map nodes.", "TeamLogo");
                    Add("Fill look", "Copies another team's home and away equipment, colors, goalie gear and number style into this draft. Name, logo, roster and settings are untouched; save to keep the look.", "FillLook");
                    Add("Home and away", "Home and Away are separate kits with their own styles and colors.", "TeamKitTabs");
                    Add("Front and back", "Turns both previews.", "TeamFacingTabs");
                    Add("Skater equipment", "Helmet, jersey, pants and socks here, gloves on the left. Choosing a jersey sets matching sleeves; Biceps / sleeves changes them separately in Premade mode. Custom color uses kit colors; premade styles keep their artwork. Socks can follow the jersey. Skates are colors only" + (defaults ? "; Stick (new players) is the stick new players start with." : ", and each player sets their own stick."), "SkaterEquipment");
                    Add("Number style", "The default number format for players set to Inherit team: 27, XXVII or 00027. An explicit player style overrides it, including for goalies. Play Now export carries the resolved player style through the mod's companion data.", "NumberStyle");
                    Add("Goalie equipment", "Mask, jersey, pants, catcher, pads, blocker and stick for the team's goalies.", "GoalieEquipment");
                    Add("Colors", "Customise colors opens the color channels of a piece in the current kit. Home and Away colors are saved separately. Colour guide shows which channel tints which part.", "Customise colors");
                    break;
            }
            if (defaults)
                Add("Save default", "Keeps this look for new teams and players and returns Home. Existing teams and players are not changed. Cancel leaves the saved default as it was.", "SaveDefaultLook");
            else
                Add("Save team", "Saves the team's settings, look and roster changes. Import team opens a copy of another team instead. Export to Play Now copies the team into the game's own custom content; save first.", "Save player team", "Save opponent");
        }
        else if (Content is HeadEditorView { Project.IsGoalie: true })
        {
            Add("New goalie mask", "Name a mask and choose a native template or blank artwork. Save first to open all required views, layers and a separate portrait.", "HeadNewProject");
            Add("Import PNGs", "Name files Front, Back, Front left, Front right, Back left, Back right or Portrait. Imports fit the selected native layout and keep a backup. Importing Front without Portrait also updates the portrait. Use individual Replace PNG buttons for extra layers.", "ImportHeadPngs");
            Add("Native placement", "Inspect the original mask against the goalie's body in six views. Mirrored directions share artwork where the game does. These original references do not update with your edits.", "HeadNativeReference", "HeadExampleDirection");
            Add("Open or save a mask", "Open a saved goalie mask folder or save the current project metadata. Imports and replacements save immediately.", "OpenHeadProject", "SaveHead");
            Add("Refresh PNGs", "Reload artwork after editing it in your image app.", "RefreshHeadPngs");
            Add("Add to goalie mask options", "Installs the complete mask project. Return Home and choose Goalie mask > Premade in the goalie or team look editor. Restart the game after installing or changing artwork.", "InstallCustomHead");
            Add("PNG artwork", "Replace PNG accepts a 512 × 512 transparent PNG and keeps the old one as .bak. Native outline guides preserve placement; hide guides before saving finished artwork. Edit every required layer and portrait, then inspect front, back and diagonals in game.", "Replace PNG…");
            Add("Project files", "Open all artwork, outline guides and original attachment data for this mask.", "Open project folder");
        }
        else if (Content is HeadEditorView)
        {
            Add("New head", "Open setup to name a new head and choose a native template or From scratch. Scratch projects use blank 512 × 512 PNGs with a selected native layout's size, native outlines and collar guides. Save the project before its previews open. Cancel keeps this head open.", "HeadNewProject");
            Add("Native collar references", "Expand to inspect the original head or fitted artwork against the body's collar in eight directions. The same scale is used across templates. These references preserve original size and placement; they do not update with your edited PNGs.", "HeadNativeReference", "HeadHelmetNativeReference");
            Add("Placement examples", "Select a direction to see the original head against the body's collar at the same scale across templates. These reference examples do not update with your edited PNGs; inspect the changed head in game.", "HeadExampleDirection", "HeadHelmetExampleDirection");
            Add("Import PNGs", "Name files Front, Back, Side, Back diagonal, Front diagonal or Portrait; native projects also accept Left, Right, Back left, Back right, Front left, Front right and Alternate back. Any size is fitted to the selected template. Importing Front without Portrait also replaces the portrait with Front and keeps the old portrait as .bak. With a head open, imports update that head and save immediately.", "ImportHeadPngs");
            Add("Open a head", "Opens a saved head project from your custom-heads folder or anywhere else.", "OpenHeadProject");
            Add("Save", "Rewrites the head's project file. Imports, replaced PNGs and helmet settings already save as you make them.", "SaveHead");
            Add("Refresh PNGs", "Reloads the images after you edit them in another app.", "Refresh PNGs");
            Add("Add to player head options", "Checks and installs the head in the player head catalog. A head opened from outside your content folder is copied in. Return Home and reopen the player editor to see it, then restart the game.", "InstallCustomHead");
            Add("PNG artwork", "Replace PNG takes an exact 512 × 512 transparent image and keeps the old one as .bak. Open PNG edits it in your image app. Show native outline overlays the original head silhouette and neck edge at its canvas position. Outline PNG opens a separate transparent guide; hide that layer before exporting your artwork. The game fits visible head pixels to the collar automatically, ignoring transparent padding. Check all directions in game after changing artwork.", "Replace PNG…");
            Add("Supports a selectable helmet", "Whether this head has artwork for a selectable helmet. A native template needs a matching fitted variant to enable it. Actual helmet equipment and talents still follow native rules. Saves immediately.", "HeadSupportsHelmet");
            Add("Separate artwork under helmets", "Native projects retain separate fitted artwork beneath the game's helmet. Older projects can create fitted images by enabling this setting. Saves immediately.", "HeadSeparateHelmetArtwork");
            Add("Project files", "Opens the head's folder in Explorer. Open outline guides and Open attachment data open the placement guides and attachment data.", "Open project folder");
        }
        else if (Content is not null)
        {
            Add("Campaigns · Create new", "Open a campaign draft. Name it and Save campaign before selecting it for the game.", "HomeCreateCampaigns");
            Add("Copy vanilla campaign", "Copy a native squad's roster, opponents, candidate pools and settings with their native identities. Name and save the resulting draft, then select it on Home. Ordinary team/player imports copy editable fields and do not grant native identity checks.", "HomeCopyVanillaCampaign");
            Add("Campaigns · Edit existing", "Choose a saved campaign to edit its opponents, custom teams, maps, free agents and rewards.", "HomeEditCampaigns");
            Add("Players · Create new", "Choose Skater or Goalie, then create a player draft. Save changes stores new Home players directly in the player library.", "HomeCreatePlayers");
            Add("Players · Edit existing", "Choose a saved library or campaign player. Edits change that selected copy.", "HomeEditPlayers");
            Add("Players · Import", "Copy a player from Base game, In-game custom or Campaign custom, or import a file. Complete required fields and save the copy.", "HomeImportPlayers");
            Add("Teams · Create new", "Choose Player team or Campaign opponent, then create a draft. Teams made from Home save to the corresponding library; import them into a campaign to use them there.", "HomeCreateTeams");
            Add("Teams · Edit existing", "Choose a saved team to edit its settings, look and roster.", "HomeEditTeams");
            Add("Teams · Import", "Import an editable team copy. This does not grant native team/player identity checks; use Copy vanilla campaign for identity-preserving copies. Team imports also offer the Player team library tab.", "HomeImportTeams");
            Add("Import campaign", "Install a campaign zip from your computer or download one from the community list. If a name is taken, Keep both renames the new copy and Replace mine backs up the old one.", "HomeImportCampaigns");
            Add("Upload campaign", "Publish a saved campaign with its logos and custom heads to the public community folder. It cannot be removed from Studio. Review the upload dialog before sharing.", "HomeUploadCampaigns");
            Add("Campaign selection", "The campaign the game loads next launch; None (base game) turns campaigns off. It saves immediately. Restart the game after changing it.", "HomeSelectCampaign");
            Add("Default look", "The logo, kit colors, equipment, goalie gear and number style new teams start with. New players take its styles and stick, and players with no team preview in it.", "HomeSetDefault");
            Add("Head Editor", "Opens setup first. Name your head, select a native template or From scratch with a native layout, then Save head and show previews. You can also open an existing saved head. Cancel stays on Home.", "HomeHeadEditor");
            Add("Goalie Mask Editor", "Create a custom goalie mask from native artwork or blank PNGs. Edit its views, extra layers and portrait, then add it to goalie mask options. Saved masks also travel with shared campaigns.", "HomeGoalieMaskEditor");
            Add("Read game artwork", "Build or rebuild the local pictures and base-game roster snapshot from your installed Tape to Tape. The game can be closed. Choose its folder if Studio cannot find it.", "HomeReadGameArt");
            Add("Check for updates", "Studio checks GitHub on every startup, after the beta notice, and before launching the game. This button checks again. Beta releases and newer builds of the same beta version are included. Studio asks before downloading and running the installer; save edits and close Tape to Tape first. Studio reopens after installation. Offline checks leave editing available.", "HomeCheckForUpdates");
            Add("Uninstall", "Save edits and close Tape to Tape first. After confirmation, Studio closes and opens the Windows uninstaller for Studio and the bundled mod. Saved campaigns, players and custom heads are kept. Cancel leaves Studio open.", "HomeUninstall");
            Add("Update mod compatibility", "Checks the mod already installed against the game files. Close the game to refresh verified compatibility data. If a mod repair is required, Studio also checks GitHub for a newer installer. This button retries the local check.", "HomeUpdateModCompatibility");
            Add("Launch Tape to Tape", "Checks GitHub for updates, verifies the installed mod's compatibility, then opens the game through Steam with the campaign selected above. An unresolved compatibility error prevents this launch; you can keep editing.", "HomeLaunchGame");
            Add("Studio guide", "Searchable explanations of every feature: what saves when, how content reaches the game, and known limits.", "HomeGuide");
            Add("Join Discord for help", "Open the community Discord invite in your browser for help with installing the mod, creating content and troubleshooting.", "HomeDiscord");
            Add("GitHub", "Open the project's repository in your browser for release downloads, documentation and issues.", "HomeGitHub");
            Add("Report a bug", "Open the report form to describe the problem and optionally include setup details and recent log warnings. Open on GitHub prepares an issue in your browser; nothing is submitted until you submit it there.", "HomeReportBug");
        }
        return steps;
    }
}

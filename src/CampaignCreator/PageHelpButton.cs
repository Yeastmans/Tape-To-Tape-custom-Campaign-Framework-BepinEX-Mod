using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using CampaignCore;

namespace CampaignCreator;

internal static class PageHelpButton
{
    private sealed class Session { internal PageHelpPopup? Help; }
    private static readonly ConditionalWeakTable<Window, Session> sessions = new();
    private static bool installed;
    internal static PageHelpPopup? Active(Window owner) => sessions.TryGetValue(owner, out var session) ? session.Help : null;
    internal static void Install()
    {
        if (installed) return;
        installed = true;
        Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
        {
            if (window.Owner is Window parent) Active(parent)?.Pause();
            // Some platforms assign Owner after WindowOpened is raised.
            Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (window.Owner is Window owner) Active(owner)?.Pause(); });
            if (window is not MainWindow) window.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.F1) { Show(window); e.Handled = true; } };
            if (window is MainWindow || window.Content is not Control content || content.GetLogicalDescendants().OfType<Control>().Any(c => c.Name == "PageHelp")) return;
            var shell = new DockPanel();
            var help = Create(window); help.HorizontalAlignment = HorizontalAlignment.Right; help.Margin = new Thickness(8);
            DockPanel.SetDock(help, Dock.Top); shell.Children.Add(help);
            window.Content = null;
            shell.Children.Add(content is StackPanel ? new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } : content);
            window.Content = shell;
            window.Height += 48;
        });
    }
    internal static Button Create(Window owner)
    {
        var button = new Button { Name = "PageHelp", Content = "Help", Padding = new Thickness(12, 6) };
        ToolTip.SetTip(button, "Explain the controls on this page. Help never edits or saves.");
        button.Click += (_, _) => { if (owner is MainWindow main) main.ShowPageHelp(); else Show(owner); };
        return button;
    }
    internal static IEnumerable<Control> Visible(Window owner) => owner.GetVisualDescendants().OfType<Control>()
        .Where(c => c.IsEffectivelyVisible && !c.GetVisualAncestors().OfType<Control>().Any(a => a.Name == "PageHelpPopupSurface"));
    private static string Context(Window owner) => (owner.Content is CampaignWorkspace c ? c.CurrentSection.ToString() : "") + "|" +
        string.Join("|", Visible(owner).OfType<SelectingItemsControl>().Where(t => t is TabControl or TabStrip).Select(t => t.SelectedIndex));
    internal static PageHelpPopup Show(Window owner, IReadOnlyList<PageHelpPopup.Step>? authored = null)
    {
        if (owner is HeadProjectStartWindow setup) authored ??= setup.HelpSteps();
        var session = sessions.GetOrCreateValue(owner); session.Help?.Pause();
        var controls = Visible(owner).Where(c => c.Bounds.Width > 0 && c.Bounds.Height > 0).ToArray();
        var steps = new List<PageHelpPopup.Step>();
        var covered = new HashSet<Control>();
        var explainedLists = new HashSet<ScrollViewer>();
        // One popup per authored step: its targets are alternatives, first match on screen wins.
        foreach (var step in authored ?? [])
        {
            foreach (string target in step.Targets)
            {
                var control = controls.FirstOrDefault(c => PageHelpPopup.Matches(c, target));
                if (control == null || !covered.Add(control)) continue;
                steps.Add(step with { Target = control });
                break;
            }
        }
        // Enumerate public controls on the current page, excluding template-internal buttons and text boxes.
        foreach (var control in controls.Where(c => c is Button or TextBox or ComboBox or ToggleSwitch or Slider or NumericUpDown or TabStrip or TabControl or ListBox))
        {
            if (control.Name == "PageHelp" || covered.Contains(control) || control.Name?.StartsWith("PART_") == true) continue;
            if (control.GetVisualAncestors().OfType<Control>().Any(a => covered.Contains(a) && a.Tag is string)) continue;
            if (control.GetVisualAncestors().TakeWhile(a => a != owner).Any(a => a is Button or ComboBox or NumericUpDown or TabStrip)) continue;
            if (control is CheckBox && control.Name?.StartsWith("Effect_") != true && control.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault() is { } list
                && list.GetVisualDescendants().OfType<CheckBox>().Count() > 12)
            {
                if (explainedLists.Add(list)) steps.Add(control.Name == "FreeAgentTalentSelection"
                    ? new("Free-agent talent levels", Describe(owner, control, Label(control)), []) { Target = control }
                    : new("Choices in this list", "Check the entries you want included. Scroll or use the available search to find more choices. Entries share the same selection behavior; Help points to one example without changing it. Save the enclosing player, team or campaign to keep the selection. Native eligibility still applies.", []) { Target = control });
                continue;
            }
            string label = Label(control);
            if (string.IsNullOrWhiteSpace(label)) continue;
            // Repeated list rows share behavior; explain the first real row instead of hundreds of identical entries.
            string description = Describe(owner, control, label);
            if (steps.Any(s => s.Title == label && s.Instructions == description)) continue;
            steps.Add(new(label, description, []) { Target = control });
        }
        if (steps.Count == 0) steps.Add(new("Page help", "There are no editable controls on this page. Close Help to return.", []));
        object? content = owner.Content; string context = Context(owner);
        var help = new PageHelpPopup(owner, steps) { ContextValid = () => ReferenceEquals(content, owner.Content) && Context(owner) == context };
        session.Help = help; help.Show(); return help;
    }
    internal static string Label(Control c)
    {
        if (c.Name == "FreeAgentTalentWeight") return "Talent multiplier";
        if (c is TabControl tabs) return string.Join(" / ", tabs.Items.OfType<TabItem>().Select(t => t.Header));
        if (c is TabStrip strip) return string.Join(" / ", strip.Items.Cast<object>());
        if (c is ComboBox && c.GetVisualAncestors().OfType<EquipmentStylePicker>().FirstOrDefault() is { } equipment
            && equipment.GetVisualParent() is Control equipmentRow
            && equipmentRow.GetVisualChildren().OfType<TextBlock>().FirstOrDefault()?.Text is { } part)
        {
            if (Equals(c.Tag, "Bicep")) return "Biceps / sleeves";
            return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(part.ToLowerInvariant())
                + (c is ComboBox { PlaceholderText: "Choose base-game style" } ? " · Premade style" : " · Style");
        }
        if (c is ContentControl { Content: string text }) return text;
        if (c is ContentControl { Content: TextBlock contentText }) return contentText.Text ?? "";
        if (c is Button b && b.Content is Control child) return child.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault()?.Text ?? "";
        if (c is ComboBox { Tag: string assignment, PlaceholderText: { } assignmentLabel } && assignment.StartsWith("opponent-node:")) return assignmentLabel;
        if (c is NumericUpDown && c.GetVisualParent() is Control numericRow
            && numericRow.GetVisualChildren().TakeWhile(s => s != c).OfType<TextBlock>().FirstOrDefault() is { Text: { } numericLabel }) return numericLabel;
        // FieldControls tags its wrapper, not the nested input. Use that field name
        // rather than a template-part name or the required-field hint beneath it.
        static bool FieldTag(string tag) => tag.Length < 60 && !tag.Contains(':') && !tag.Contains('/') && !tag.Contains('\\') && !Guid.TryParse(tag, out _);
        if (c.Tag is string ownTag && FieldTag(ownTag)) return ownTag;
        var field = c.GetVisualAncestors().OfType<Control>().FirstOrDefault(a => a.Tag is string);
        if (field?.Tag is string fieldTag && FieldTag(fieldTag)) return fieldTag;
        if (c is TextBox { Watermark: { } watermark }) return watermark;
        var parent = c.GetVisualParent();
        var label = (parent as Control)?.GetVisualChildren().TakeWhile(s => s != c).OfType<TextBlock>().LastOrDefault()?.Text;
        if (label != null) return label;
        if (c is ComboBox { PlaceholderText: { } placeholder }) return placeholder;
        return label ?? (ToolTip.GetTip(c) as string) ?? (c.Name == null ? "" : System.Text.RegularExpressions.Regex.Replace(c.Name, "(?<=[a-z])(?=[A-Z])", " "));
    }
    private static string Describe(Window owner, Control c, string label)
    {
        if (c is ComboBox && c.GetVisualAncestors().OfType<EquipmentStylePicker>().Any())
            return label == "Biceps / sleeves"
                ? "Choose separate sleeves in Premade jersey mode. Choosing another jersey can set matching sleeves again. Save the player or team to keep the choice."
                : "Choose this piece's style for the displayed kit. Custom color / Team colors uses the colorable style; Premade offers fixed artwork. Same as jersey is available for socks and No helmet for supported skater heads. Disabled player controls follow Team equipment or the head's helmet restriction. Save the player or team to keep edits.";
        bool InTeamSettings() => owner is MainWindow { Content: TeamEditorView view } && view.GetVisualDescendants().OfType<TabItem>().Any(t => t.IsSelected && Equals(t.Header, "Team settings"));
        if (c.Name?.StartsWith("HomeType") == true) return "Select " + label + " as the content type for this card's Create new, Edit existing and Import actions. Selecting the type does not create or save anything.";
        if (c.Name == "CampaignOffsidePenalty") return "Choose Player's setting, Whistle, Lose Puck or Knockout. This selector is enabled only with Offside in every match. Save campaign keeps it; the player's own setting is restored after the run.";
        if (c.Name == "BackToCampaign") return owner is MainWindow { Content: CampaignWorkspace { CurrentSection: 0 } }
            ? "Return Home. Save campaign first to keep pending edits; this Back action discards unsaved campaign changes."
            : "Return to the campaign overview. Draft settings stay in this campaign until you save or leave it.";
        if (c.Tag is string assignment && assignment.StartsWith("opponent-node:")) return "Assign this opponent to a match node, replacing that node's previous team in the map draft. Save maps or Save campaign keeps the assignment.";
        if (label == "+ New opponent") return "Create and open a new opponent directly in this campaign. Save pending campaign changes first; the team has its own Save opponent button.";
        if (label is "+ Add player team" or "Add player team") return "Save the campaign's working changes, then create and open a player team directly in this campaign.";
        if (label == "Home" && owner is MainWindow { Content: HeadEditorView }) return "Return Home. Head imports, image replacements and helmet settings have already been saved.";
        if (label is "Home" or "Back to team" or "Back to opponents" or "Back to custom squads") return "Leave this editor using " + label + ". Save player or team edits first; navigation discards unsaved edits.";
        if (InTeamSettings() && label is "Preview" or "Stop" or "Use base game sound") return label switch {
            "Preview" => "Play this team's selected goal sound in Studio at the draft volume (Windows). Help never starts playback.",
            "Stop" => "Stop the goal-sound preview and remove its temporary playback file.",
            _ => "Clear this team's custom goal sound from the draft. Save the team to keep the base-game sound." };
        if (InTeamSettings() && c is Slider) return "Set this team's goal-sound volume from 0 to 100 percent. Save the team to keep it; game playback also uses master and effects volume.";
        if (label == "Choose squad icon") return "Choose a head or custom logo image for this player team's squad card. It is separate from the team crest and roster heads. Save the team to keep it.";
        if (label == "Colour guide") return "Open the equipment color-channel reference. This explains which part each channel tints and does not change colors.";
        if (label == "Replace PNG…") return "Replace this head-project image with a 512 × 512 PNG. It saves immediately and retains the previous image as .bak.";
        if (label is "Open outline guides" or "Open attachment data") return "Open this head project's " + (label == "Open outline guides" ? "placement-guide folder" : "attachment reference data") + " in the associated application.";
        if (c.Tag is string nodeId && Guid.TryParse(nodeId, out _) && c is Button && c.GetVisualAncestors().OfType<MapEditorView>().Any()) return "Open settings for this map node. While Connect is active, click a highlighted node in the next layer to complete a path instead. Node settings change the draft; Save maps keeps them.";
        if (c.Tag is string nodeChoice && nodeChoice.StartsWith("node-choice:")) return "Choose " + label + " for this map node. It enters the map draft; configure its settings and connections, then Save maps.";
        if (label == "+ Add node") return "Add a node to this layer and choose its type. Three or more choices use closer rows. Save maps keeps the change.";
        if (label.StartsWith("Map ") && c is Button) return "Open this map in the campaign's sequence. Switching maps keeps draft edits but does not save them.";
        if (label == "Stat Scale") return "Multiply skater speed, shot power, accuracy and checking, and the goalie's numeric attributes except Pass Read. Blank or 1 leaves them unchanged. Save the team to keep the scale.";
        if (label == "Size Offset") return "Adjust the skater's body scale in addition to Size. Blank uses normal scale. Save changes keeps it; check the result in game.";
        if (label == "Bicep") return "Biceps / sleeves selects separate sleeves in Premade jersey mode. Choosing another jersey can replace this selection with matching sleeves. Save the player or team to keep it.";
        if (label == "Description") return "Describe this team for its map and match preview. Save the team to keep the text.";
        if (c.Name == "BugIncludeInfo") return "Include setup information and recent plugin warnings/errors in the report. Review the draft before opening GitHub; nothing is submitted until you submit the issue there.";
        if (c.Name == "BugOpenGitHub") return "Open the prepared issue in your browser. A short summary is required. Review and submit it yourself on GitHub.";
        if (label == "Open log folders") return "Open folders holding available plugin and player logs. You can attach them yourself to the GitHub issue.";
        if (c.Name == "UploadCampaignConfirm") return "Upload the selected saved campaign with its referenced logos and custom head projects to the public community folder. Other users can download it; Studio cannot remove it afterwards.";
        if (c.Name == "UploadCampaignPicker") return "Choose the saved campaign to share. Selecting it does not upload anything; Upload performs the publish action.";
        if (label is "Keep both" or "Replace mine") return label == "Keep both" ? "Install the incoming campaign under a new name and keep your existing campaign." : "Move your existing campaign to campaign_backups, then install the incoming copy under its name.";
        if (c.Name?.StartsWith("Effect_") == true && c.Tag is string effectId && OpponentEffects.All.FirstOrDefault(e => e.Key == effectId) is { } effect)
            return effect.Help + " Save opponent keeps this effect. Conflicting alternatives must be unchecked first.";
        if (c.Tag is string settingKey && RelicOpponentInteractions.All.FirstOrDefault(i => i.SettingKey == settingKey) is { } interaction)
            return interaction.Help + " Save opponent keeps this setting.";
        if (c.Name == "CopyVanillaSquad") return "Choose the native squad whose roster, opponents, candidate pools and inherited settings will be copied. Copy campaign creates a draft; name and save it before selecting it on Home.";
        if (c.Name == "CreateVanillaCopy") return "Create an editable draft that retains native roster and opponent identities. This immediately writes the draft files; name and Save campaign afterward. Native event and player requirements still apply.";
        if (c.Name == "FreeAgentTalentPoolRestore") return "Restore native base and upgrade talent levels without changing multipliers or Regular/Chaos reward pools. Save campaign keeps it; generated offers and signed contracts are not rerolled.";
        if (c.Name == "FreeAgentTalentWeightSearch") return "Filter talent names or IDs without changing checked levels or multipliers. Clear the search to show the full list.";
        if (label == "Stored weights and imported IDs") return "Expand the advanced view of saved talent-ID:multiplier entries. Named controls edit the same values. Unknown imported IDs are retained, but may not resolve in this game; invalid entries must be fixed before saving.";
        if (c.Name == "FreeAgentTalentWeight") return "Multiply this talent level's native direct-roll weight. Zero prevents direct rolls; unchecking its level also prevents upgrade outcomes. This does not change extra-roll chances or fixed talents. Save campaign keeps it.";
        if (c.Name == "FreeAgentTalentSelection") return "Include this exact talent level in random recruit extras and imported random recruit lists. Unchecked levels cannot roll or be reached through upgrades. Fixed template talents stay intact. Save campaign keeps it.";
        if (label == "Reset to 1") return "Remove this talent's weight override so native weight applies again. Its selection checkbox is unchanged. Save campaign keeps the reset.";
        if (c.Name == "ImportSourceTabs") return "Player imports have Base game, In-game custom and Campaign custom tabs. Team imports also include Player team library. Base-game records come from Read game artwork; ordinary imports copy editable fields rather than native identity checks. Copy vanilla campaign on Home preserves native identities. Game custom records come from the game's save folder. Choose a source, search and select an entry, then Import selected.";
        if (owner is StudioGuideWindow && label is "Clear search" or "Back to previous topic" or "Close guide") return label switch {
            "Clear search" => "Clear the search words and show every guide topic.",
            "Back to previous topic" => "Return to the previous article in this guide's navigation history.",
            _ => "Close the guide and return to Studio. Reading the guide does not edit content." };
        if (c.Name == "NodeTeam" && owner is NodeSettingsWindow) return "Choose this node's campaign opponent. Apply accepts the team and stadium together; Cancel discards these pending choices. Save maps then writes the map. Apply needs an existing campaign team.";
        if (c.Name == "NodeStadium" && owner is NodeSettingsWindow) return "Choose world-map stadium artwork. Apply accepts this selection; Cancel discards it. Save maps writes it to disk. This does not choose the match arena.";
        if (label == "Node type") return "Change the event on this node immediately in the map draft. The final Boss is fixed. Done closes settings; Undo restores an edit and Save maps writes the map.";
        if (c.Name is "Talentrewards" or "Relicrewards") return "Choose 0–10 " + label.ToLowerInvariant() + " for this Match node. The change enters the map draft immediately. Save maps keeps it. Boss rewards follow the game.";
        if (c.Name == "Playerstorecruit") return "Choose 1–20 recruitment choices for this General Manager node. This changes the map draft; Save maps keeps it.";
        if (c.Name is "NodePeriods" or "NodePeriodMinutes") return "Override this node's timed match period count or period length. Use base-game setting retains the native value. Goal-target rules stay unchanged. The selection enters the map draft immediately; Save maps keeps it.";
        if (label == "Random event options...") return "Choose weights for 5 event types; each random node becomes 1 event. Saved custom pools override native event-placement limits and allow repeats. Apply accepts the weights into the draft; Cancel leaves them alone. Save maps uses them for newly generated maps.";
        if (label == "Remove node") return "Remove this non-Boss node and its connections from the draft, then close its settings. Undo restores it; Save maps keeps the removal.";
        if (label is "Undo" or "Redo") return label + " changes the selected map's draft history, including node settings and imported layouts. Save maps writes the result.";
        if (label == "Fit" || c.Name is "NodeZoomIn" or "NodeZoomOut") return "Adjust the node builder's view. Fit frames the layout and the plus/minus controls zoom. Node positions and saved content are unchanged.";
        if (c.Name == "MapLook") return "Choose Act 1, Act 2 or Act 3 artwork for the current map. Save maps keeps its act selection.";
        if (c.Name == "MapGeneration") return "Both modes use the editable layout. Vanilla random starts from imported event pools and opponents; each random node rolls from its own choices on a new run. Edit that node's Random event options to customize its pool.";
        if (c.Name == "MapName") return "Name this map for its opening wipe. Leave blank to keep the game's act name. The ACT number follows the map's position in the campaign, independently of Map look. Save maps keeps the name.";
        if (label == "Import base-game map") return "Replace the current map with a base-game layout or another campaign's map. Imported team assignments are cleared; Undo restores the previous layout. Save maps keeps the replacement.";
        if (label == "Save maps") return "Validate and save all maps and their act sequence. Fix route errors first. Previous layouts are backed up and saved changes apply to new runs. Ctrl+S performs the same save.";
        if (label == "+ Add map") return "Append a new map to the campaign sequence. Save maps keeps it. At least one map must remain when removing tabs.";
        if (label == "+ Insert layer") return "Insert a layer into this map draft. Add its choices and connect their routes, then save maps.";
        if (c.Name == "CommunityCampaigns") return "Select a shared campaign to download. Download and install installs it locally; selection alone does not download or activate it.";
        if (c.Name == "DownloadCommunityCampaign") return "Download and install the selected community campaign. Existing names offer Keep both or Replace mine; replacement preserves the old campaign in campaign_backups. Select the imported campaign on Home to play it.";
        if (label == "Use base-game bench") return "Switch between the native and custom bench cards. Both sets retain their selections; only the visible set is active. Save campaign keeps the choice.";
        if (c is ComboBox && label == "Choose base-game player") return "Choose a native bench player for this slot. Picking one already in another slot swaps them. Save campaign keeps the bench order.";
        if (label == "Outline PNG") return "Generate and open the original native silhouette as a separate transparent PNG. Use it as a drawing layer to align the head and neck, then hide it before exporting your artwork.";
        if (label == "Show native outline") return "Show or hide the original head silhouette and neck edge over this image. The outline is generated from the preserved native PNG and stays fixed as you edit. This view toggle does not change or save the artwork.";
        if (label == "Open PNG") return "Open this artwork PNG in the associated application. Save edits in that app, then use Refresh PNGs here. The portrait must be edited separately.";
        if (ToolTip.GetTip(c) is string tip && tip.Length > 15) return tip + " Help only explains this control; Continue does not activate it.";
        if (owner is RandomNodeSettingsWindow && c is NumericUpDown) return "This weight sets how often " + label + " comes up relative to the other events. Zero excludes it; at least one weight must be positive. The percentage is this node's roll chance. Custom pools allow repeats on connected nodes and within a layer, ignore Nurse and Solo Blademaster limits, and keep the selected event through template replacements. Apply puts the weights into the draft, then Save maps writes them for newly generated maps. Existing map events stay the same.";
        if (c.Name == "NodeTeam") return "Choose the campaign opponent played at this node. The choice goes into the map right away (Undo reverts it) and Save maps keeps it. A node left empty uses the game's own opponent; a missing team must be replaced before saving.";
        if (c.Name == "NodeStadium") return "Choose the stadium art this node shows on the world map; the preview below updates. The choice goes into the map right away and Save maps keeps it. It does not change the arena the match is played in.";
        if (label == "Done") return "Done closes this dialog. Choices made in it are already in the editor; use the page's save button to keep them.";
        if (c is TabStrip or TabControl) return "Switch between " + label + ". These selectors change the editor view; switching does not save. Open Help again after changing tabs for that view's controls.";
        if (c is ComboBox { PlaceholderText: "Choose base-game player" }) return "Choose a native bench player for this slot. Picking a player already in another slot swaps them. Save campaign keeps the bench order.";
        if (c is ListBox) return "Select an entry in " + label + " to inspect or choose it. Lists can be scrolled and may have separate source tabs. Selection alone does not press a dialog's Apply or save button.";
        if (label == "Apply") return "Apply accepts these dialog choices into the calling editor draft. Use that editor's Save action to keep them on disk. Help never applies the dialog.";
        if (label == "Cancel") return "Cancel closes this dialog without accepting its pending choices. Files already created by separate import actions may remain. Continue does not close or cancel anything.";
        if (label.Contains("Save", StringComparison.OrdinalIgnoreCase)) return "Use " + label + " when you want to keep your edits. Validation may require missing fields or map routes to be fixed first. Continue in Help does not press this button.";
        if (label.Equals("Export to Play Now", StringComparison.OrdinalIgnoreCase)) return "Export validates current editor fields and writes separate Play Now content. It does not save the Studio source. Save team equipment/setup and roster-player changes first. Restart the game to load the export; see the guide for native-format limitations.";
        if (label.Contains("Import", StringComparison.OrdinalIgnoreCase)) return "Choose a source for " + label + ". Imported campaign or library copies can be edited independently. Import actions can create files immediately; Help never opens the picker or imports anything.";
        if (label.Contains("color", StringComparison.OrdinalIgnoreCase)) return "Choose the color channels for this item. The preview is an approximation; check materials and lighting in game. Close Help before making edits.";
        if (c is CheckBox or ToggleSwitch) return "Toggle " + label + " to turn this option on or off. The Studio guide explains how each option is saved and used in game.";
        if (c is TextBox or NumericUpDown or Slider) return "Edit " + label + " here. Help leaves the current value untouched.";
        if (c is ComboBox) return "Choose " + label + " from the available options. An unavailable or empty choice may need attention before saving. The Home campaign selector saves immediately; editor fields use the page's save action.";
        if (label.Contains("Delete", StringComparison.OrdinalIgnoreCase) || label.Contains("Remove", StringComparison.OrdinalIgnoreCase) || label == "×") return "This action removes the selected item. Review the current item and any confirmation before using it. Continue in Help never performs removals.";
        return "Use " + label + " to open or perform the action shown on this page. Continue only advances this explanation; it never activates the control. The Studio guide explains the related feature, save timing and game limitations.";
    }
}

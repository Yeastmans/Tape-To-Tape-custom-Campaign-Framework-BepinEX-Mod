using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using static CampaignCreator.StudioGuideWindow;

namespace CampaignCreator;

internal sealed record GuideScreen(string File, string Title, string Explanation);

internal static class StudioGuideScreens
{
    internal static GuideScreen[] For(string topic) => topic switch
    {
        "start" => [Screen("home-help", "Home", "Choose a content type in the three cards. The campaign selector saves immediately. Read game artwork rebuilds previews from your installed game. Studio guide opens this reference; Report a bug opens a form with an Open on GitHub action.")],
        "campaign" => [Screen("home-help", "Active campaign", "Select campaign is in the upper right. Restart the game after changing it."), Screen("campaign-0", "Campaign overview", "Open one of the five sections. Save campaign is separate from the game's Continue/New Run actions.")],
        "sources" => [Screen("import-0", "Base-game import", "Base-game content is read from your Tape to Tape install; the game doesn't need to be running. Search and select an entry before importing; this picture cannot import anything."), Screen("import-1", "In-game custom import", "Custom teams and players the game has saved, made in its editor or exported from Studio. They are read from the game's save folder each time, and every copy is listed with its date."), Screen("import-2", "Campaign custom import", "Your campaign content and Studio library can be copied into a new destination. Studio exports are listed under In-game custom."), Screen("team-import-0", "Player team library import", "Team imports also include Player team library as their first tab, for reusable player teams and their rosters.")],
        "players" => [Screen("skater-1", "Skater details & attributes", "Name and Number stay above the tabs. This tab edits skater stats, ability and talents; use Save changes to keep edits."), Screen("goalie-1", "Goalie details & attributes", "Goalies have their own attribute and talent controls, rather than a skater ability selector.")],
        "appearance" => [Screen("skater-0", "Player appearance", "Team equipment on uses the team's styles; off lets you pick this player's own. Choose Team, Custom or Premade per kit piece. Custom colors are personal and saved separately for home and away. Home/Away switch the kit; Front/Back change only the preview angle."), Screen("goalie-0", "Goalie appearance", "Goalies use mask, jersey, pants, pads, catcher, blocker and goalie-stick controls. The team supplies default kit colors and, with Team equipment on, the gear. Custom kit colors override the defaults; stick and blocker colors are personal."), Screen("team-1", "Team look", "The team logo and Fill look are across the top. Skater equipment surrounds the skater preview and goalie gear sits beside the goalie preview; Name & numbers holds the number style.")],
        "defaults" => [Screen("default-look", "Default team look", "Save default affects newly created content. It does not rewrite existing players or teams.")],
        "teams" => [Screen("team-squad-0", "Player-team settings", "Identity, squad template, mascot, squad icon, Zamboni head, transition colors and goal celebration are set here; the logo is in Team look. Starting pools are on the campaign page."), Screen("team-0", "Opponent-team settings", "Opponent identity, transition colors, goal celebration and Stat Scale. Match behavior has its own tab.")],
        "rosters" => [Screen("team-2", "Opponent roster", "Each card is a position. Create/import a copy, open it to edit and save that player; save the team for roster removals."), Screen("team-squad-2", "Player-team roster", "The squad template controls the positions and lines. Slots marked Filled by squad setup come from the template; other empty slots need a saved player.")],
        "squads" => [Screen("campaign-3", "Choose campaign team", "Click a player team to edit its own preset and Squad behaviors, or Add player team to create one. Use player teams keeps saved teams when turned off; Custom teams only hides the base-game squads.")],
        "behaviors" => [Screen("squad-behaviors", "Squad behaviors and validation", "Each control has Inherit, On and Off, with its template default and effective setting below the description. This example deliberately enables conflicting three- and four-skater limits: the orange explanation identifies what must be changed before saving.")],
        "starting" => [Screen("campaign-3", "Campaign starting choices", "Bench and superstar choices are below the player-team list on the campaign page. All starting pools remain editable; each team decides which screens use them. Save campaign keeps the shared choices; hidden content is retained when switching templates.")],
        "opponents" => [Screen("team-3", "Opponent settings", "Formations, puck and surface, two-line behavior and special effects apply in matches against this opponent. Nothing is simulated in the editor.")],
        "maps" => [Screen("campaign-2-tab-0", "Node builder", "Map documents are across the top. Arrange nodes, connect routes and assign opponents; Save maps validates the result."), Screen("campaign-2-tab-1", "Game preview", "Inspect route presentation here. This is an editor preview, not a running game.")],
        "nodes" => [Screen("node-help", "Match node settings", "Team chooses the opponent and Stadium the node's art on the world map. Changes go into the map draft right away; Save maps writes them."), Screen("random-node-help", "Random node weights", "5 event types, 1 event per random node. Positive weights set the displayed roll odds; zero excludes an event. Custom pools allow repeats and override native event-placement and template-replacement limits. Apply updates the draft; Save maps writes the weights for newly generated maps.")],
        "agents" => [Screen("campaign-4", "Free agents", "Select recruitable base-game players or create/import campaign-specific agents. This pool is separate from the starting roster.")],
        "rewards" => [Screen("campaign-5-tab-0", "Relics", "Base game pool uses the native pool. Turn it off to edit allowed entries; the search filters the list."), Screen("campaign-5-tab-1", "Skater talents", "This category controls random skater talent rewards, not a player's assigned talents."), Screen("campaign-5-tab-2", "Goalie talents", "Goalies use a separate talent category."), Screen("campaign-5-tab-3", "Skater abilities", "Custom pools draw only from checked entries, including selected special rewards. No unchecked base-game rewards are added as fallback. Saving requires the whole-route reserve.")],
        "audio" => [Screen("audio", "Goal celebration", "Choose audio file converts a clip into the draft. Preview/Stop controls playback and the slider sets the volume. Save the team to keep the audio and volume.")],
        "heads" => [Screen("head-project-empty", "Head setup dialog", "Name the head, choose a native template or blank PNGs with a native layout, then Save head and show previews. Open saved head reopens a project; Cancel creates no files."), Screen("head-project-loaded", "Saved head artwork", "After saving, edit or import directional PNGs, extra layers and the portrait. Native outline guides show each original head and neck at its canvas position. Outline PNG opens a separate drawing layer; use Refresh PNGs after editing in another app.")],
        "export" => [Screen("skater-0", "Export to Play Now", "The export action is in the player footer. It reads current editor fields but does not save the Studio source. Team equipment and roster edits need saving before export.")],
        _ => []
    };
    private static GuideScreen Screen(string file, string title, string explanation) => new(file, title, explanation);
    private static Bitmap Load(GuideScreen screen) => new(AssetLoader.Open(new Uri("avares://T2T Campaign Studio/Assets/StudioGuide/" + screen.File + ".png")));
    internal static Control Card(GuideScreen screen)
    {
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(Text(screen.Title, 19));
        try
        {
            var bitmap = Load(screen);
            var image = new Image { Source = bitmap, Width = bitmap.Size.Width, Height = bitmap.Size.Height, Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Left };
            body.Children.Add(new ScrollViewer { Name = "GuideScreenScroll", Content = image, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
            image.DetachedFromVisualTree += (_, _) => { image.Source = null; bitmap.Dispose(); };
        }
        catch (FileNotFoundException) { body.Children.Add(Text("Screen illustration is unavailable in this build.")); }
        body.Children.Add(Text(screen.Explanation));
        body.Children.Add(new TextBlock { Text = "Illustration with example content · read-only", FontSize = 12, Foreground = StudioTheme.Muted });
        return new Border { Padding = new Thickness(14), Background = StudioTheme.Surface, BorderBrush = StudioTheme.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Child = body };
    }
}

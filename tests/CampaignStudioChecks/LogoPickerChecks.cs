using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CampaignCore;
using CampaignCreator;

static class LogoPickerChecks
{
    public static void Run(string output)
    {
        output=Path.GetFullPath(output); Directory.CreateDirectory(output);
        string root=Path.Combine(output,"fixture-"+Guid.NewGuid().ToString("N"));
        string team=Path.Combine(root,"campaigns","Test","teams","Test","team.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(team)!);
        File.WriteAllText(team,"Team Name = Test\nCity = Fixture\nAbbreviation = TST\nBody = Body/Customization/Customization_colors\nGloves = Glove/Customization/Customization_colors\nPants = Pants/Customization/Customization_colors\n");
        File.WriteAllText(Path.Combine(root,"_game_team_logos.txt"),"Native crest\n");
        Directory.CreateDirectory(Path.Combine(root,"_team_logos")); Directory.CreateDirectory(Path.Combine(root,"logos"));
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing=false }).SetupWithoutStarting();
        void Png(string file, IBrush brush) {
            var box=new Border { Width=24, Height=24, Background=brush }; box.Measure(new Size(24,24)); box.Arrange(new Rect(0,0,24,24));
            using var image=new RenderTargetBitmap(new PixelSize(24,24)); image.Render(box); image.Save(file);
        }
        string native=LogoFiles.NativeFile(root,"Native crest"), custom=Path.Combine(root,"logos","Native crest.png");
        Png(native,Brushes.Red); Png(custom,Brushes.Blue);
        string packaged = Path.Combine(GameArt.ArtCache.Latest() ?? throw new Exception("Build the game art cache before this check."), "team-logos", Path.GetFileName(LogoFiles.NativeFile(root, "Hockey FC")));
        Check(File.Exists(packaged), "Base-game logos are available in the local game art cache");
        string stale = LogoFiles.NativeFile(root, "Hockey FC"); Png(stale, Brushes.Gray);
        Check(LogoExport.FindFile(root, "Hockey FC") == packaged, "Verified base-game artwork wins over a stale grey cache");
        var nativeDoc = new CampaignDocument("Logo From = Hockey FC\n");
        Check(Convert.FromBase64String(LogoExport.Snapshot(nativeDoc, root).Get("Export Logo PNG")).SequenceEqual(File.ReadAllBytes(packaged)), "Play Now export embeds the verified base-game artwork");
        using (var preview = LogoExport.Preview(root, "Hockey FC")) Check(preview != null && preview.PixelSize.Width > 24, "Base-game preview decodes real extracted artwork");
        Check(LogoExport.FindFile(root,"Native crest")==native,"Native source wins over same-name custom image");
        Check(LogoExport.FindFile(root,custom)==custom,"Explicit custom source retains its exact identity");
        var window=new MainWindow(["--content",root]); window.Show(); Pump();
        // The logo is part of the team look for player teams and opponents alike.
        string squad=Path.Combine(root,"campaigns","Test","player_teams","Squad","team.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(squad)!); File.WriteAllText(squad,"Team Name = Squad\n");
        foreach (var (file,kind) in new[] { (squad,"player team"), (team,"opponent") })
        {
            typeof(MainWindow).GetMethod("OpenFile",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[file]); Pump();
            SelectTab(window,"Team settings");
            Check(!window.GetVisualDescendants().OfType<Button>().Any(b=>b.Content?.ToString()=="Choose logo"),kind+" Team settings has no logo picker");
            SelectTab(window,"Team look");
            Check(window.GetVisualDescendants().OfType<Control>().Single(c=>c.Name=="TeamLogo").GetVisualDescendants().OfType<Button>().Any(b=>b.Content?.ToString()=="Choose logo"),kind+" Team look has the logo picker");
        }
        Click(window,"Choose logo"); var dialog=window.OwnedWindows.Single(w=>w.Title=="Choose team logo");
        var tabs=dialog.GetVisualDescendants().OfType<TabControl>().Single(); tabs.SelectedIndex=0; Pump();
        var list=dialog.GetVisualDescendants().OfType<ListBox>().Single(); list.SelectedIndex=0; Pump();
        Check(dialog.GetVisualDescendants().OfType<Image>().Single(i=>i.Width==140).Source is Bitmap,"Native logo preview loads");
        tabs.SelectedIndex=1; Pump();
        Check(dialog.GetVisualDescendants().OfType<Image>().Single(i=>i.Width==140).Source==null,"Switching tabs clears the previous logo preview");
        Click(dialog,"Use logo"); Check(window.OwnedWindows.Count(w=>w.Title=="Choose team logo")==1,"Cannot apply stale selection from the other tab");
        list=dialog.GetVisualDescendants().OfType<ListBox>().Single();
        list.SelectedItem=list.Items.Cast<object>().Single(i=>i.ToString()=="Native crest"); Pump();
        Check(dialog.GetVisualDescendants().OfType<Image>().Single(i=>i.Width==140).Source is Bitmap,"Custom logo preview loads");
        using(var frame=dialog.CaptureRenderedFrame()) frame!.Save(Path.Combine(output,"custom-logo-picker.png"));
        Click(dialog,"Use logo");
        var view=window.GetVisualDescendants().Single(v=>v.GetType().Name=="TeamEditorView");
        string selected=(string)view.GetType().GetMethod("Value",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(view,["Logo From"])!;
        var doc=new CampaignDocument(""); doc.Set("Logo From",selected);
        var snapshot=LogoExport.Snapshot(doc,root);
        Check(Convert.FromBase64String(snapshot.Get("Export Logo PNG")).SequenceEqual(File.ReadAllBytes(custom)),"Export embeds exactly the selected custom image");
        Check(LogoFiles.Resolve(root,Path.Combine(root,"unused"),selected)==custom,"Runtime resolver uses the same image as preview and export");
        Click(window,"Choose logo"); dialog=window.OwnedWindows.Single(w=>w.Title=="Choose team logo");
        tabs=dialog.GetVisualDescendants().OfType<TabControl>().Single(); tabs.SelectedIndex=0; Pump();
        list=dialog.GetVisualDescendants().OfType<ListBox>().Single(); list.SelectedIndex=0; Pump();
        Click(dialog,"Use logo");
        selected=(string)view.GetType().GetMethod("Value",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(view,["Logo From"])!;
        Check(selected=="Native crest","Base-game logo stores its team name, not an install path: "+selected);
        string Value(string key) => (string)view.GetType().GetMethod("Value",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(view,[key])!;
        var homeMode=window.GetVisualDescendants().OfType<ComboBox>().Single(c=>c.Name=="JerseyLogoModeHome");
        Check(homeMode.Items.Cast<string>().SequenceEqual(["Use team logo","No jersey logo","Choose image"]),"Home jersey picker exposes the three explicit choices");
        homeMode.SelectedIndex=1; Pump();
        Check(Value("Jersey Logo From")=="none","Home no-logo choice updates the unsaved draft");
        homeMode.SelectedIndex=0; Pump();
        Check(Value("Jersey Logo From")=="","Home inherited choice remains blank in the unsaved draft");
        Click(window,"Choose jersey image");
        dialog=window.OwnedWindows.Single(w=>w.Title=="Choose jersey logo");
        tabs=dialog.GetVisualDescendants().OfType<TabControl>().Single(); tabs.SelectedIndex=1; Pump();
        list=dialog.GetVisualDescendants().OfType<ListBox>().Single();
        list.SelectedItem=list.Items.Cast<object>().Single(i=>i.ToString()=="Native crest"); Pump();
        Click(dialog,"Use logo");
        Check(Value("Jersey Logo From")==custom,"Home image choice retains its exact source path");
        var kit=window.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.TabStrip>().Single(t=>t.Name=="TeamKitTabs");
        kit.SelectedIndex=1; Pump();
        var awayMode=window.GetVisualDescendants().OfType<ComboBox>().Single(c=>c.Name=="JerseyLogoModeAway");
        awayMode.SelectedIndex=1; Pump();
        Check(Value("Jersey Logo Away From")=="none" && Value("Jersey Logo From")==custom,"Away no-logo choice leaves Home independent");
        Click(window,"Save opponent");
        var saved=new CampaignDocument(File.ReadAllText(team));
        Check(saved.Get("Jersey Logo From")==custom && saved.Get("Jersey Logo Away From")=="none","Save persists both kit selections exactly");
        typeof(MainWindow).GetMethod("OpenFile",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[team]); Pump();
        view=window.GetVisualDescendants().Single(v=>v.GetType().Name=="TeamEditorView");
        Check((string)view.GetType().GetMethod("Value",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(view,["Jersey Logo From"])! == custom
            && window.GetVisualDescendants().OfType<ComboBox>().Single(c=>c.Name=="JerseyLogoModeHome").SelectedIndex==2,
            "Reopened team shows its saved Home image");
        kit=window.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.TabStrip>().Single(t=>t.Name=="TeamKitTabs");
        kit.SelectedIndex=1; Pump();
        Check(window.GetVisualDescendants().OfType<ComboBox>().Single(c=>c.Name=="JerseyLogoModeAway").SelectedIndex==1,
            "Reopened team shows its saved Away no-logo choice");
        kit.SelectedIndex=0; Pump();
        var crest=view.GetType().GetMethod("Crest",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var equipment=(Dictionary<string,string>)view.GetType().GetField("equipment",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(view)!;
        Check(crest.Invoke(view,[false]) is Bitmap,"Colorable skater jersey previews its selected Home crest");
        equipment["Body"]="Body/Meatballs";
        Check(crest.Invoke(view,[false])==null && crest.Invoke(view,[true]) is Bitmap,
            "Premade skater jersey hides its crest while compatible goalie still previews it");
        equipment["Body"]="Body/Customization/Customization_colors";
        foreach(bool slotAway in new[]{false,true}) {
            kit.SelectedIndex=slotAway?1:0;Pump();
            equipment=(Dictionary<string,string>)view.GetType().GetField("equipment",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(view)!;
            string field=JerseyLogoOptions.Field(slotAway), prefix=JerseyLogoOptions.Prefix(slotAway);
            var embedded=new CampaignDocument("");embedded.Set(field,custom);embedded=LogoExport.Snapshot(embedded,root,slotAway);
            foreach(var pair in embedded.Fields)equipment[pair.Key]=pair.Value;
            Click(window,"Save opponent");
            typeof(MainWindow).GetMethod("OpenFile",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[team]);Pump();
            view=window.GetVisualDescendants().Single(v=>v.GetType().Name=="TeamEditorView");
            kit=window.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.TabStrip>().Single(t=>t.Name=="TeamKitTabs");kit.SelectedIndex=slotAway?1:0;Pump();
            equipment=(Dictionary<string,string>)view.GetType().GetField("equipment",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(view)!;
            string originalIdentity=Value("Logo From"), other=Value(JerseyLogoOptions.Field(!slotAway));
            byte[] pixels=File.ReadAllBytes(custom);File.Delete(custom);
            view.GetType().GetField("logoSource",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(view, "");
            Check(crest.Invoke(view,[false]) is Bitmap,"Embedded kit previews after original file deletion: "+slotAway);
            using(var replacement=new RenderTargetBitmap(new PixelSize(7,9)))replacement.Save(custom);
            Check(crest.Invoke(view,[false]) is Bitmap kept && kept.PixelSize.Width==24,"Embedded pixels take precedence over replaced path: "+slotAway);
            var newer=new CampaignDocument("");newer.Set(field,custom);newer=LogoExport.Snapshot(newer,root,slotAway);
            foreach(var pair in newer.Fields)equipment[pair.Key]=pair.Value;
            Check(crest.Invoke(view,[false]) is Bitmap changed && changed.PixelSize.Width==7,"Embedded content change invalidates same-source preview cache: "+slotAway);
            Check(Value("Logo From")==originalIdentity && Value(JerseyLogoOptions.Field(!slotAway))==other,"Preview preserves identity and opposite kit");
            File.WriteAllBytes(custom,pixels);
        }
        foreach(bool slotAway in new[]{false,true})
        foreach(int modeIndex in new[]{0,1}) {
            kit.SelectedIndex=slotAway?1:0;Pump();
            var selector=window.GetVisualDescendants().OfType<ComboBox>().Single(c=>c.Name==(slotAway?"JerseyLogoModeAway":"JerseyLogoModeHome"));
            Click(window,"Choose jersey image");dialog=window.OwnedWindows.Single(w=>w.Title=="Choose jersey logo");
            tabs=dialog.GetVisualDescendants().OfType<TabControl>().Single();tabs.SelectedIndex=1;Pump();
            list=dialog.GetVisualDescendants().OfType<ListBox>().Single();list.SelectedItem=list.Items.Cast<object>().Single(i=>i.ToString()=="Native crest");Pump();Click(dialog,"Use logo");
            view=window.GetVisualDescendants().Single(v=>v.GetType().Name=="TeamEditorView");
            selector=window.GetVisualDescendants().OfType<ComboBox>().Single(c=>c.Name==(slotAway?"JerseyLogoModeAway":"JerseyLogoModeHome"));
            selector.SelectedIndex=modeIndex;Pump();
            var picker=window.GetVisualDescendants().OfType<Control>().Single(c=>c.Name==(slotAway?"JerseyLogoImageAway":"JerseyLogoImageHome"));
            Check(picker.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text=="No image selected"),"Image caption resets for kit "+slotAway+" mode "+modeIndex);
            Check(Value(JerseyLogoOptions.Field(slotAway))==(modeIndex==0?"":"none"),"Caption reset preserves authored mode: actual="+Value(JerseyLogoOptions.Field(slotAway))+", selected="+selector.SelectedIndex);
            Click(window,"Save opponent");
            typeof(MainWindow).GetMethod("OpenFile",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[team]);Pump();
            view=window.GetVisualDescendants().Single(v=>v.GetType().Name=="TeamEditorView");
            kit=window.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.TabStrip>().Single(t=>t.Name=="TeamKitTabs");kit.SelectedIndex=slotAway?1:0;Pump();
            picker=window.GetVisualDescendants().OfType<Control>().Single(c=>c.Name==(slotAway?"JerseyLogoImageAway":"JerseyLogoImageHome"));
            Check(picker.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text=="No image selected"),"Reopened caption reflects saved mode");
        }
        window.Close();
    }
    static void Pump(){for(int i=0;i<6;i++){Dispatcher.UIThread.RunJobs();Thread.Sleep(20);}}
    static void SelectTab(Window w,string header){var tabs=w.GetVisualDescendants().OfType<TabControl>().First(t=>t.Items.OfType<TabItem>().Any(i=>i.Header?.ToString()==header));tabs.SelectedItem=tabs.Items.OfType<TabItem>().Single(i=>i.Header?.ToString()==header);Pump();}
    static void Click(Window w,string text){w.GetVisualDescendants().OfType<Button>().Single(b=>b.Content?.ToString()==text).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();}
    static void Check(bool ok,string text){if(!ok)throw new Exception(text);Console.WriteLine("PASS "+text);}
}

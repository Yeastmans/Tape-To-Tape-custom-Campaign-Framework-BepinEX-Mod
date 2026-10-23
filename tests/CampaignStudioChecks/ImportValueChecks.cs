using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using CampaignCore;
using CampaignCreator;

static class ImportValueChecks
{
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        string file=Path.Combine(output,"invalid-player.txt");
        File.WriteAllText(file,"Name = Imported\nPosition = Center\nSkates = Not_A_Real_Skate\nSkates Away = black skates\nHelmet = Faces/Custom/Helmet_Face\nBody = Body/Customization/Customization_colors\nJersey Color = 300,2,3\nSpeed = banana\nSize = Giant\nAbility = Imaginary ability\nTalents = Unknown talent\nDescription = Keep my words\n");
        var imported=CreatorContent.CustomPlayerCopy(file,"Imported copy");
        foreach(string key in new[]{"Skates","Skates Away","Helmet","Jersey Color","Speed","Size","Ability"})
            Check(imported.Get(key)=="",key+" invalid import is unselected");
        Check(imported.Get("Talents")=="Unknown talent","Unknown imported talent title is preserved");
        Check(imported.Get("Body")=="Body/Customization/Customization_colors","Known equipment retained");
        Check(imported.Get("Description")=="Keep my words","Free text retained");
        Check(File.ReadAllText(file).Contains("Not_A_Real_Skate"),"Import source untouched");
        string basePlayer=Directory.EnumerateFiles(Path.Combine(GameArt.ArtCache.Latest() ?? throw new Exception("Build the game art cache before this check."),"base-rosters","Base Game Players"),"*.txt").Order().First();
        string originalNative = File.ReadAllText(basePlayer);
        var legacyNative = new CampaignDocument(originalNative); legacyNative.Set("Skates", "black skates"); legacyNative.Set("Skates Away", "black skates");
        string legacyFile = Path.Combine(output, "legacy-native-player.txt"); legacyNative.Save(legacyFile);
        var nativeCopy=CreatorContent.CustomPlayerCopy(legacyFile,"Native import");
        Check(nativeCopy.Get("Skates")=="" && nativeCopy.Get("Skates Away")=="" && File.ReadAllText(basePlayer) == originalNative,
            "Legacy native-player copy clears unsupported skate style without relying on obsolete extracted data or changing its source");
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var type=typeof(MainWindow).Assembly.GetType("CampaignCreator.EquipmentStylePicker")!;
        const BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        Control Picker(string saved)=> (Control)Activator.CreateInstance(type,hidden,null,
            [new[]{"Body_Skates/Customization/Customization_colors"}, (Func<string>)(()=>saved), (Action<string>)(_=>{}),new Button(),true,"Custom color",false,null,false,false,null,null,null,null],null)!;
        var blank=Picker(""); var bad=Picker("Body_Skates/Black_Skates");
        var styles=(ComboBox)type.GetField("styles",hidden)!.GetValue(bad)!;
        var mode=(ComboBox)type.GetField("mode",hidden)!.GetValue(bad)!;
        Check(styles.Items.Count==0,"Imported Black Skates cannot invent an option when absent from catalog");
        Check(((ComboBoxItem)mode.SelectedItem!).Content is TextBlock { Text: "Nothing selected" },"Unknown premade shows Nothing selected");
        var good=Picker("Body_Skates/Customization/Customization_colors");
        Check(((ComboBoxItem)((ComboBox)type.GetField("mode",hidden)!.GetValue(good)!).SelectedItem!).Content?.ToString()=="Custom color","Valid custom equipment remains selectable");
        var fieldType=typeof(MainWindow).Assembly.GetType("CampaignCreator.FieldControls")!;
        var create=fieldType.GetMethod("Create",BindingFlags.NonPublic|BindingFlags.Static)!;
        foreach (var (key, role) in new[] { ("Relics", "skater"), ("Team Relics", "skater"), ("Talents", "skater"), ("Goalie Talents", "goalie") })
        {
            const string title = "Imported_Random Charm+";
            var doc = new CampaignDocument($"{key} = {title}, {title}\n");
            new UpgradeCleanup().CleanImportedValues(doc, role);
            Check(doc.Get(key) == title, key + " import retains and deduplicates absent titles");
            var field = ((Control control, Func<string> read))create.Invoke(null,
                [key, doc.Get(key), role, Activator.CreateInstance(typeof(MainWindow).Assembly.GetType("CampaignCreator.GameData")!, Path.GetFullPath("release")), (Action)(()=>{}), (Action<string>)(_=>{}), false, false, false])!;
            var window = new Window { Width = 850, Height = 650, Content = field.control };
            window.Show(); window.UpdateLayout();
            field.control.GetVisualDescendants().OfType<TextBox>().Single().Text = title;
            window.UpdateLayout();
            var check = field.control.GetVisualDescendants().OfType<CheckBox>().Single(c => c.IsChecked == true);
            Check(check.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == title), key + " displays exact imported title");
            var tile = check.GetVisualDescendants().OfType<Border>().Single(b => b.Tag?.ToString() == "ImportedRewardTitleTile");
            Check(tile.Child is TextBlock { Text: title, TextWrapping: Avalonia.Media.TextWrapping.Wrap }, key + " puts the full imported title inside the fallback tile");
            window.CaptureRenderedFrame()!.Save(Path.Combine(output, key.Replace(' ', '-') + "-title-tile.png"));
            Check(field.read() == title, key + " retains selection when opened");
            string savedFile = Path.Combine(output, key.Replace(' ', '-') + ".txt");
            doc.Set(key, field.read()); doc.Save(savedFile);
            Check(ContentImport.ReadDocument(savedFile).Get(key) == title, key + " survives save and reimport");
            check.IsChecked = false;
            Check(field.read() == "", key + " can be removed explicitly");
            window.Close();
        }
        var choices=fieldType.GetMethod("Choices",BindingFlags.NonPublic|BindingFlags.Static)!;
        var result=choices.Invoke(null,[new[]{"Small","Medium"},"Size","Imaginary size",(Action)(()=>{}),(Action<string>)(_=>{}),null,null])!;
        var combo=(ComboBox)result.GetType().GetField("Item1")!.GetValue(result)!;
        var read=(Func<string>)result.GetType().GetField("Item2")!.GetValue(result)!;
        Check(!combo.Items.Contains("Imaginary size") && combo.SelectedItem?.ToString()=="" && read()=="","Finite dropdown neither adds nor saves unsupported import");
        imported.Save(Path.Combine(output,"clean-player.txt"));
        Check(new CampaignDocument(File.ReadAllText(Path.Combine(output,"clean-player.txt"))).Get("Skates")=="","Unselected equipment survives save/reopen");
        string effectFile=Path.Combine(output,"effect-player.txt");
        File.WriteAllText(effectFile,"Name = Effect player\nPosition = Center\nFace = Faces/Anyteam/Heavy_Helmet\nBody = Body/Crusaders/GSpiked_Armor\nHelmet = Faces/Helmets/Helmet_Frog\nBicep = Body_Bicep/Injured\nGloves = Body_Gloves/Injured\nSkates = Body_Skates/Winged\n");
        var effectCopy=CreatorContent.CustomPlayerCopy(effectFile,"Effect copy");
        foreach (string key in new[]{"Face","Body","Helmet","Bicep","Gloves","Skates"})
            Check(effectCopy.Get(key)=="",key+" gameplay visual clears on import");
        Check(File.ReadAllText(effectFile).Contains("Helmet_Frog"),"Effect import source remains untouched");
        string goalieEffect=Path.Combine(output,"effect-goalie.txt");
        File.WriteAllText(goalieEffect,"Name = Effect goalie\nPosition = Goalie\nHelmet Skin = Helmet/Helmet_Enraged\n");
        Check(CreatorContent.CustomPlayerCopy(goalieEffect,"Goalie copy").Get("Helmet Skin")=="",
            "Goalie enraged mask clears on import");
        object catalogData=Activator.CreateInstance(typeof(App).Assembly.GetType("CampaignCreator.GameData")!, Path.GetFullPath("release"))!;
        var catalogAssets=(AssetCatalog)catalogData.GetType().GetProperty("Assets")!.GetValue(catalogData)!;
        foreach(var (role,key,effect) in new[]{
            ("skater","Helmet","Faces/Helmets/Helmet_Frog"),
            ("skater","Bicep","Body_Bicep/Injured"),
            ("skater","Skates","Body_Skates/Winged"),
            ("skater","Face","Faces/Anyteam/Heavy_Helmet"),
            ("goalie","Helmet Skin","Helmet/Helmet_Enraged")})
            Check(!catalogAssets.Values(role,key).Contains(effect),role+" "+key+" menu omits "+effect);
        Check(!((IReadOnlyList<string>)typeof(App).Assembly.GetType("CampaignCreator.GoalieEquipmentCatalog")!.GetMethod("Values",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(null,["Helmet Skin"])!).Contains("Helmet/Helmet_Enraged"),
            "Goalie export catalog omits enraged mask");
    }
    static void Check(bool ok,string message) { if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message); }
}

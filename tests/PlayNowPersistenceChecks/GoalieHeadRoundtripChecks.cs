using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using CampaignCore;
using CampaignCreator;
using Data;
using EndlessMode;
using Tape2Tape.Customization;
using Tape2Tape.Customization.UI;
using UnityEngine;

// Actual Studio files -> owned native goalie edit/save -> Studio import and
// import cleanup -> standalone/team re-export/update. Native host objects are doubles.
static class GoalieHeadRoundtripChecks
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;
    static readonly Assembly Studio = typeof(MainWindow).Assembly;
    static object Call(string type, string method, params object[] args) => Studio.GetType("CampaignCreator." + type, true).GetMethod(method, Flags).Invoke(null, args);
    static T Property<T>(object value, string name) => (T)value.GetType().GetProperty(name).GetValue(value);
    internal static void Run(string root, Action<bool,string> check)
    {
        foreach (var example in new[] {
            (Name:"distinct", Head:"Helmet/Helmet_Prisoners", Helmet:"Helmet/Helmet_Canadians"),
            (Name:"head-clear", Head:"", Helmet:"Helmet/Helmet_Canadians"),
            (Name:"helmet-clear", Head:"Helmet/Helmet_Prisoners", Helmet:""),
            (Name:"both-clear", Head:"", Helmet:""),
            // A goalie native head is opaque: even a known skater suffix must
            // survive exactly rather than pass through skater Base normalization.
            (Name:"exact-native-head", Head:"Faces/Toronto/Mathieu_Helmet", Helmet:"Helmet/Helmet_Canadians"),
            (Name:"custom-head-reference", Head:"Helmet/CustomMasks/Boundary", Helmet:"Helmet/Helmet_Prisoners"),
            (Name:"custom-mask", Head:"Helmet/Helmet_Prisoners", Helmet:"Helmet/CustomMasks/Boundary") })
        {
            string workspace=Path.Combine(root,example.Name), native=Path.Combine(workspace,"native"), library=Path.Combine(workspace,"workspace");
            Plugin.ModContentRoot=native;PlayNowRosterRuntime.Clear();PlayNowTranslation.InvalidateAll();
            string save=Path.Combine(native,"TeamDataModels"), side=PlayNowRosterSave.Overrides;
            Directory.CreateDirectory(save);
            string teamFile=Path.Combine(library,"source","team.txt"), players=Path.Combine(library,"source","players");Directory.CreateDirectory(players);
            var teamDoc=new CampaignDocument("Team Name = Mask boundary\nLogo From = none\nSquad Behavior two_lines = On");teamDoc.Save(teamFile);
            foreach (string position in PlayNowPositions.Names.Concat(PlayNowPositions.Names.Select(p=>"Line 2 "+p)))
                File.WriteAllText(Path.Combine(players,position+".txt"),$"Name = {position}\nPosition = {position}\nFace = Faces/Toronto/Mathieu\n");
            File.WriteAllText(Path.Combine(players,"Goalie.txt"),"Name = Independent Goalie\nPosition = Goalie\nOverride Team Settings = yes\nFace = Helmet/Helmet_Toronto\nHelmet Skin = "+(example.Helmet.Length==0?"none":example.Helmet)+"\nPass Read = 0.731\n");
            if(example.Name=="custom-mask") CreateMask(library);
            object game=Activator.CreateInstance(Studio.GetType("CampaignCreator.GameData",true),library);
            object package=Call("ExportTranslation","BuildTeam",teamDoc,teamFile,game,library,null);
            Call("ExportTranslation","Write",package,save,side,library);
            string id=Property<string>(package,"TeamId");
            var teamModel=JsonNode.Parse(File.ReadAllText(PlayNowSaveJournal.NativePath(native,"team",id)));
            string gid=teamModel["goalieId"].GetValue<string>();
            string goalieFile=PlayNowSaveJournal.NativePath(native,"goalie",gid);
            string json=File.ReadAllText(goalieFile);
            var g=JsonUtility.FromJson<GoaltenderData>(json);
            var repo=new GoalieRepository();repo._customGoalieRepository._persistentDataPath=native;
            PlayNowPlayerSave.Observe(native,"goalie",g,json);
            var stage=new CustomizeGoalieData(g);var editorMenu=new SkaterCustomizationMenu{CurrentSkater=stage};PlayNowPlayerSave.EditorInitialized(editorMenu,stage,true);
            stage.headSkin=example.Head;g.headSkin=example.Head;
            PlayNowPlayerSave.HeadConfirmed(editorMenu);
            repo.SaveCustomGoalie(g);
            var saved=JsonNode.Parse(File.ReadAllText(goalieFile));
            check(saved["headSkin"].GetValue<string>()==example.Head && saved["helmetSkin"].GetValue<string>()==example.Helmet,example.Name+": native confirmed head save keeps independent helmet");

            var imported=((IEnumerable)Studio.GetType("CampaignCreator.GameCustomContent",true).GetMethod("Load",Flags,null,new[]{typeof(string),typeof(string),typeof(string)},null).Invoke(null,new object[]{library,save,side})).Cast<object>().ToArray();
            string loose=imported.Select(x=>Property<string>(x,"File")).Single(f=>Path.GetFileName(f)==gid+".txt");
            var looseDoc=ContentImport.ReadDocument(loose);
            check(looseDoc.Get("Face")==example.Head,example.Name+": actual import cleanup retains exact native head");
            check(looseDoc.Get("Helmet Skin")== (example.Helmet.Length==0?"none":example.Helmet),example.Name+": actual import cleanup retains mask or explicit clear");
            check(looseDoc.Get("Override Team Settings")=="yes" && looseDoc.Get("Export Own Equipment")=="yes",example.Name+": actual sidecar import retains independent policy");
            var model=(Dictionary<string,object>)Call("PlayerExport","Convert",looseDoc,game,"goalie","standalone",null);
            var companion=(CampaignDocument)Call("PlayerExport","TranslationSidecar",looseDoc,game,"goalie",model,null);
            string standalone=Path.Combine(workspace,"standalone","CustomGoalie-standalone.json");Directory.CreateDirectory(Path.GetDirectoryName(standalone));
            Call("PlayerExport","Write",model,companion,standalone,Path.Combine(workspace,"standalone","standalone.txt"),Path.Combine(workspace,"standalone","snapshot.txt"));
            AssertModel(File.ReadAllText(standalone),example.Head,example.Helmet,"standalone",check);
            AssertCompanion(File.ReadAllText(Path.Combine(workspace,"standalone","standalone.txt")),example.Head,example.Helmet,"standalone",check);
            check(companion.Get("Face")==example.Head && companion.Get("Pass Read")=="0.731",example.Name+": standalone sidecar retains exact head and supplemental data");

            string importedTeam=imported.Select(x=>Property<string>(x,"File")).Single(f=>Path.GetFileName(f)=="team.txt");
            string importedPlayers=Path.Combine(Path.GetDirectoryName(importedTeam),"players");
            foreach(string file in Directory.GetFiles(importedPlayers,"*.txt"))Call("ContentImport","ValidateCopiedFile",file);
            object rebuilt=Call("ExportTranslation","BuildTeam",ContentImport.ReadDocument(importedTeam),importedTeam,game,library,null);
            string nextNative=Path.Combine(workspace,"reexport","TeamDataModels"),nextSide=Path.Combine(workspace,"reexport","overrides");Directory.CreateDirectory(nextNative);
            Call("ExportTranslation","Write",rebuilt,nextNative,nextSide,library);
            string nextId=Property<string>(rebuilt,"TeamId");
            string nextGid=JsonNode.Parse(File.ReadAllText(Path.Combine(nextNative,"CustomTeam-"+nextId+".json")))["goalieId"].GetValue<string>();
            AssertModel(File.ReadAllText(Path.Combine(nextNative,"CustomGoalie-"+nextGid+".json")),example.Head,example.Helmet,"team re-export",check);
            AssertCompanion(File.ReadAllText(Path.Combine(nextSide,nextGid+".txt")),example.Head,example.Helmet,"team re-export",check);
            object target=((IEnumerable)Call("ExportTranslation","ExistingTeams",save,side,library)).Cast<object>().Single(x=>Property<string>(x,"Id")==id);
            Call("ExportTranslation","WriteUpdate",rebuilt,target,save,side,library);
            string updatedGid=JsonNode.Parse(File.ReadAllText(PlayNowSaveJournal.NativePath(native,"team",id)))["goalieId"].GetValue<string>();
            AssertModel(File.ReadAllText(PlayNowSaveJournal.NativePath(native,"goalie",updatedGid)),example.Head,example.Helmet,"team update",check);
            AssertCompanion(File.ReadAllText(Path.Combine(side,updatedGid+".txt")),example.Head,example.Helmet,"team update",check);
            Console.WriteLine("PASS goalie roundtrip "+example.Name);
        }
        Controls(root,check);
    }
    static void Controls(string root,Action<bool,string> check)
    {
        object game=Activator.CreateInstance(Studio.GetType("CampaignCreator.GameData",true),root);
        var legacy=new CampaignDocument("Name = Legacy Goalie\nPosition = Goalie\nOverride Team Settings = yes");
        var defaultModel=(Dictionary<string,object>)Call("PlayerExport","Convert",legacy,game,"goalie","legacy",null);
        AssertModel(JsonSerializer.Serialize(defaultModel),"","Helmet/Helmet_Customization_colors","legacy missing Face and blank equipment",check);
        var inherited=new CampaignDocument("Name = Inherited Goalie\nPosition = Goalie\nFace = Helmet/Helmet_Prisoners\nHelmet Skin = none\nOverride Team Settings = no");
        var defaults=new CampaignDocument("Helmet Skin = Helmet/Helmet_Canadians");
        var resolved=(CampaignDocument)Call("ExportTranslation","Player",inherited,new CampaignDocument(""),defaults,"goalie",game);
        var inheritedModel=(Dictionary<string,object>)Call("PlayerExport","Convert",resolved,game,"goalie","inherited",null);
        AssertModel(JsonSerializer.Serialize(inheritedModel),"Helmet/Helmet_Prisoners","Helmet/Helmet_Canadians","intentional team inheritance",check);
        check(resolved.Get("Export Own Equipment")=="no","Inherited equipment retains author intent");
        using var native=JsonDocument.Parse("{\"headSkin\":\"Helmet/Helmet_Prisoners\",\"helmetSkin\":\"\",\"firstName\":\"Loose\"}");
        var raw=(CampaignDocument)Call("GameCustomContent","Player",native.RootElement,true);
        check(raw.Get("Override Team Settings")=="no" && raw.Get("Helmet Skin")=="none","Native-only loose import retains existing inheritance policy and records known clear");
        var looseModel=(Dictionary<string,object>)Call("PlayerExport","Convert",raw,game,"goalie","loose",null);
        AssertModel(JsonSerializer.Serialize(looseModel),"Helmet/Helmet_Prisoners","Helmet/Helmet_Customization_colors","native-only loose team-inheriting export",check);
        var invalid=new CampaignDocument("Name = Invalid Goalie\nPosition = Goalie\nOverride Team Settings = yes\nFace = Helmet/Helmet_Toronto\nHelmet Skin = Helmet/UnknownAuthoredMask");
        var invalidModel=(Dictionary<string,object>)Call("PlayerExport","Convert",invalid,game,"goalie","invalid",null);
        check(!invalidModel.ContainsKey("helmetSkin"),"Authored goalie mask validation still omits unknown equipment");
        var skater=new CampaignDocument("Name = Invalid Skater\nFace = Faces/UnknownAuthoredHead");new UpgradeCleanup().CleanImportedValues(skater,"skater");
        check(skater.Get("Face")=="","Skater face import validation remains enforced");
    }
    static void CreateMask(string root)
    {
        string folder=Path.Combine(root,"custom-heads","Boundary");Directory.CreateDirectory(folder);
        using(var bitmap=new SkiaSharp.SKBitmap(512,512)) {
            bitmap.Erase(SkiaSharp.SKColors.White);
            using var image=SkiaSharp.SKImage.FromBitmap(bitmap);using var png=image.Encode(SkiaSharp.SKEncodedImageFormat.Png,100);
            foreach(string file in new[]{"mask.png","portrait.png"}) {using var output=File.Create(Path.Combine(folder,file));png.SaveTo(output);}
        }
        var definition=new CustomHeadDefinition{Version=3,IsGoalie=true,Name="Boundary",TemplateSkin="Helmet/Helmet_Prisoners",TemplateHelmetSkin=""};
        foreach(var pair in new[]{("mask.png","Head"),("portrait.png","Portrait")}) definition.Images.Add(new CustomHeadImage{File=pair.Item1,Group=pair.Item2,Width=512,Height=512,NativeWidth=512,NativeHeight=512,ContentWidth=512,ContentHeight=512});
        foreach(string direction in new[]{"S","SE","SW","N","NE","NW"}) definition.Bindings.Add(new CustomHeadBinding{View=direction.StartsWith("N")?"North":"South",Slot="Helmet",Placeholder=direction,Direction=direction,File="mask.png"});
        File.WriteAllText(Path.Combine(folder,"head-project.json"),JsonSerializer.Serialize(definition));
        CustomHeadDefinition.Read(folder);
    }
    static void AssertModel(string json,string head,string helmet,string boundary,Action<bool,string> check)
    {
        var model=JsonNode.Parse(json);
        check(model["headSkin"].GetValue<string>()==head,boundary+": native head identity/clear survives");
        check(model["helmetSkin"].GetValue<string>()==helmet,boundary+": separate native helmet identity/clear survives");
    }
    static void AssertCompanion(string text,string head,string helmet,string boundary,Action<bool,string> check)
    {
        var doc=new CampaignDocument(text);
        check(doc.Get("Face")==head && doc.Get("Helmet Skin")== (helmet.Length==0?"none":helmet),boundary+": sidecar preserves separate head and mask selections");
        AssertModel(doc.Get("Export Model"),head,helmet,boundary+" sidecar model",check);
    }
}

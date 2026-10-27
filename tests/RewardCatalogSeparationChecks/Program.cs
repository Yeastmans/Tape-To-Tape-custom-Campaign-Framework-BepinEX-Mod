using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CampaignCore;
using CampaignCreator;

string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
string root=Path.Combine(output,"fixture-"+Guid.NewGuid().ToString("N"));
string gui=Path.Combine(root,"T2T_Dumps/_gui_data");Directory.CreateDirectory(gui);
string campaign=Path.Combine(root,"campaigns/Separation");Directory.CreateDirectory(campaign);
const string Custom="00540026-811e-58e1-bd9c-e34d63a1b9f1", Orphan="11111111-2222-4333-8444-555555555555";
const string Native="00e6e2f9-cfbe-438a-9ebe-7f2287f68436";
const string OpponentOnly="0a2ba8af-1f12-4e2f-93de-4e91d7027263";
int checks=0;
void Check(bool value,string message){if(!value)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
File.WriteAllText(Path.Combine(gui,"_reward_catalog.tsv"),
    $"Talents\t{Custom}\t{Custom}\t1\tCCF_Reward_{Custom}\tOath of Maedhros\tCustom definition from another campaign.\n"+
    $"Talents\t{Orphan}\t{Orphan}\t1\tccf_reward_{Orphan}\tUnowned custom clone\tDump entry without a local definition.\n"+
    $"Relics\t{Orphan}\t{Orphan}\t1\tCCF_Reward_{Orphan}\tUnowned custom relic\tDump entry without a local definition.\n");
var definition=new CustomRewardDefinition{Id=Custom,Name="Oath of Maedhros",Kind=CustomRewardDefinition.CustomTalents,
    IconPng="iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aNn8AAAAASUVORK5CYII="};
definition.Save(Path.Combine(root,"library/rewards"));
string definitionFile=Path.Combine(root,"library/rewards",Custom+CustomRewardDefinition.Extension);
string originalDefinition=File.ReadAllText(definitionFile);
Check(RewardCatalog.ReadEntries(root,"Talents").ContainsKey(Native),"Native En Garde remains in the base-game catalog");
Check(!RewardCatalog.ReadEntries(root,"Talents").ContainsKey(Custom),"Known custom definition is absent from the native catalog");
Check(!RewardCatalog.ReadEntries(root,"Talents").ContainsKey(Orphan),"Orphan custom runtime clone cannot become a native talent");
Check(!RewardCatalog.ReadEntries(root,"Relics").ContainsKey(Orphan),"Custom runtime relic is also separate from native rewards");
Check(!RewardCatalog.FreeAgentBaseTalents(root).Contains(Custom),"Custom talent is not a base recruitment talent");
Check(!RewardCatalog.ReadPool(root,new CampaignDocument(""),"Talents").Selected.Contains(Custom),"Default selection cannot include a dumped custom clone");
var explicitPool=RewardCatalog.ReadPool(root,new CampaignDocument("Pool Mode Talents = Custom\nSelected Talents = "+Custom),"Talents");
Check(explicitPool.Selected.Contains(Custom),"Existing explicit saved IDs are preserved");
File.WriteAllText(Path.Combine(campaign,"campaign.txt"),"Act Sequence = 1\nSquad Template = No Effects\nUse Player Teams = yes\n");
File.WriteAllText(Path.Combine(campaign,"reward_pools.txt"),"Pool Mode Talents = Base Game\nNative Defaults Talents = "+Native+", c6aeb029-9e63-45ca-b81a-f5922bff6507, 9168222a-87fe-4f6e-b4e8-851aaa1f3a1a, "+OpponentOnly+"\n");
Directory.CreateDirectory(Path.Combine(campaign,"map_layouts"));
var map=new MapLayout{BaseMap=0,BaseSquad="Vanilla Squad"};
map.Nodes.AddRange([new(){Id="start",Layer=0,Type="Nurse",Outgoing=["boss"]},new(){Id="boss",Layer=1,Type="Boss",TalentRewards=0,RelicRewards=0}]);
File.WriteAllText(Path.Combine(campaign,"map_layouts/map-1.txt"),map.Serialize());
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions{UseHeadlessDrawing=false}).SetupWithoutStarting();
var owner=new Window{Width=1200,Height=900};
var workspace=new CampaignWorkspace(owner,root,campaign,()=>{},_=>{},()=>Task.CompletedTask);
owner.Content=workspace;owner.Show();Pump();
workspace.GetVisualDescendants().OfType<Button>().Single(b=>b.Name=="CampaignOverviewSection5").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
var tabs=workspace.GetVisualDescendants().OfType<TabControl>().Single(t=>t.Name=="RewardSections");
tabs.SelectedIndex=1;Pump();
Check(!tabs.GetVisualDescendants().OfType<CheckBox>().Any(c=>Equals(c.Tag,Custom)||Equals(c.Tag,Orphan)),"Regular talent UI contains no custom runtime clones");
Check(tabs.GetVisualDescendants().OfType<CheckBox>().Single(c=>Equals(c.Tag,Native)).IsChecked==true,"Base mode still checks genuine native talents");
Check(tabs.GetVisualDescendants().OfType<CheckBox>().Single(c=>Equals(c.Tag,OpponentOnly)).IsChecked!=true,"A stale copied default cannot auto-check the native opponent-only Karlson behavior");
Check(!RewardCatalog.OfferedInNormalRun("unreviewed-native-id"),"Unverified IDs are not automatically declared normal-run rewards");
tabs.SelectedItem=tabs.Items.OfType<TabItem>().Single(t=>t.Header?.ToString()==CustomRewardDefinition.CustomTalents);Pump();
var customRow=tabs.GetVisualDescendants().OfType<CheckBox>().Single(c=>Equals(c.Tag,Custom));
Check(customRow.IsChecked!=true,"Custom talent appears in its own section, off by default");
customRow.IsChecked=true;Pump();
owner.GetVisualDescendants().OfType<Button>().Single(b=>b.Content?.ToString()=="Save campaign").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
var saved=new CampaignDocument(File.ReadAllText(Path.Combine(campaign,"reward_pools.txt")));
Check(CustomRewardPoolSelection.Read(saved,CustomRewardDefinition.CustomTalents,[definition]).Selected.Contains(Custom),"Custom selection saves in its separate custom pool");
Check(!RewardPoolDefinition.Read(saved,"Talents",[]).Selected.Contains(Custom),"Custom selection does not leak into the native pool");
Check(File.ReadAllText(definitionFile)==originalDefinition,"Original custom definition is unchanged");
using(var frame=owner.CaptureRenderedFrame())frame!.Save(Path.Combine(output,"custom-talents-separated.png"));
owner.Close();Console.WriteLine($"{checks} separation checks passed");
static void Pump(){for(int i=0;i<4;i++){Dispatcher.UIThread.RunJobs();Thread.Sleep(20);}}

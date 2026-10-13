using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CampaignCore;

namespace CampaignCreator;

/// <summary>All native candidates stay visible, including pools larger than a single offer.</summary>
public sealed class NativePlayerPoolEditor : UserControl
{
    private readonly Func<string> directory;
    private readonly HashSet<string> selected;
    private readonly StackPanel list=new() {Spacing=8};
    private readonly TextBlock count=new() {TextWrapping=TextWrapping.Wrap};
    private readonly Action changed;
    public bool HasChanges {get;private set;}
    public NativePlayerPoolEditor(Window owner,string root,Func<string> directory,Action<string> edit,Action changed,bool insetList=false)
    {
        this.directory=directory;this.changed=changed;
        selected=(SelectedPlayerPool.Read(directory())??[]).Select(f=>Path.GetFileName(f)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        void Refresh()
        {
            list.Children.Clear();
            foreach (string file in Directory.GetFiles(directory(),"*.txt").Order(StringComparer.OrdinalIgnoreCase))
            {
                string key=Path.GetFileName(file);var doc=new CampaignDocument(File.ReadAllText(file));
                var check=new CheckBox {Content=doc.Get("Name",key),IsChecked=selected.Contains(key),Tag=key};
                check.IsCheckedChanged+=(_,_)=>{if(check.IsChecked==true)selected.Add(key);else selected.Remove(key);HasChanges=true;changed();UpdateCount();};
                var button=new Button {Content="Edit player",Padding=StudioTheme.ButtonPadding};
                button.Click+=(_,_)=>{try {Save();edit(Path.Combine(directory(),key));}catch(Exception ex){count.Text=ex.Message;}};
                var row=new Grid {ColumnDefinitions=new ColumnDefinitions("*,Auto"),ColumnSpacing=12};row.Children.Add(check);Grid.SetColumn(button,1);row.Children.Add(button);list.Children.Add(row);
            }
            UpdateCount();
        }
        var import=new Button {Content="Import candidate",Padding=StudioTheme.ButtonPadding};
        import.Click+=async(_,_)=>
        {
            string? source=await ContentImport.Pick(owner,root,"skater");if(source==null)return;
            string key="custom-"+Guid.NewGuid().ToString("N")+".txt";
            var doc=ContentImport.ReadDocument(source);doc.Remove(VanillaCopyData.PlayerId);doc.Remove(VanillaCopyData.PlayerAsset);
            doc.Save(Path.Combine(directory(),key));selected.Add(key);HasChanges=true;changed();Refresh();
        };
        Content=new StackPanel {Spacing=12,Children={
            new TextBlock {Text="The full native candidate pool is copied below. Unchanged choices keep the game's selection and unlock rules. Edit a player or change the checked candidates to customize this pool.",TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Muted},
            count,insetList ? InsetPoolPanel.List(list,480,"NativeFreeAgentPoolScroll") : new ScrollViewer {Content=list,MaxHeight=480},import
        }};
        AttachedToVisualTree+=(_,_)=>Refresh();Refresh();
    }
    private void UpdateCount()=>count.Text=selected.Count+" candidates selected";
    public void Save()
    {
        if(!HasChanges)return;
        if(selected.Count==0)throw new InvalidOperationException("Select at least one candidate. Starting screens can be disabled in Squad behaviors.");
        SelectedPlayerPool.Save(directory(),selected.Order(StringComparer.OrdinalIgnoreCase));HasChanges=false;
    }
}

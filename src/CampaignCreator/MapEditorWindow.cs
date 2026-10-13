using Avalonia.Controls;
using CampaignCore;

namespace CampaignCreator;

public sealed class MapEditorWindow : Window
{
    private readonly MapEditorView editor;
    public MapLayout? CurrentLayout => editor.CurrentLayout;
    public bool HasUnsavedChanges => editor.HasUnsavedChanges;
    public MapEditorWindow(string contentRoot, string campaignDirectory)
    {
        Title = "Campaign map · " + System.IO.Path.GetFileName(campaignDirectory);
        Width = 1380; Height = 860; MinWidth = 1000; MinHeight = 650;
        editor = new MapEditorView(contentRoot, campaignDirectory);
        Background = editor.Background;
        Content = editor;
        UnsavedChangesGuard.Attach(this, () => HasUnsavedChanges);
    }
    public void Undo() => editor.Undo();
    public void Redo() => editor.Redo();
    public void SaveMap() => editor.SaveMap();
}

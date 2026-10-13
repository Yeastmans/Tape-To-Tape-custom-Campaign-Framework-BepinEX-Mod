using CampaignCore;
namespace CampaignCreator;
public sealed partial class MainWindow
{
    private string[] Missing(CampaignDocument next) => EditorRequirements.Missing(next, editorShell is TeamEditorView, role);
    private void RefreshAttention()
    {
        var missing = Missing(ReadEditor());
        if (editorShell is PlayerEditorView player) player.Attention.Update(missing);
        if (editorShell is TeamEditorView team) team.Attention.Update(missing);
    }
    private bool ValidateEditor(CampaignDocument next)
    {
        string[] missing = Missing(next); RefreshAttention();
        if (missing.Length == 0) return true;
        status.Text = "Needs attention — " + string.Join("; ", missing.GroupBy(EditorRequirements.Zone).Select(g => g.Key + ": " + string.Join(", ", g)));
        return false;
    }
}

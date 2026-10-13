using Avalonia.Controls;
using CampaignCore;

namespace CampaignCreator;

internal sealed partial class TeamEditorView
{
    private async Task FillLook(string root, TextBlock status)
    {
        string? sourceFile = await TeamLookPicker.Pick(owner, root);
        if (sourceFile == null) return;
        try
        {
            var look = TeamLookCopy.Load(sourceFile);
            foreach (string key in TeamLookCopy.Keys)
            {
                string value = look.Get(key);
                if (FieldSchema.GoalieAssets.Contains(key)) goalieEquipment.Set(key, value);
                else
                {
                    equipment[key] = value;
                    Track(key, () => equipment[key]);
                }
            }
            BuildParts();
            RefreshUniform();
            changed();
            status.Text = "Look filled from " + new CampaignDocument(File.ReadAllText(sourceFile)).Get("Team Name", Path.GetFileName(Path.GetDirectoryName(sourceFile)!)) + ". Save to keep it.";
        }
        catch (Exception ex) { status.Text = "Could not fill look: " + ex.Message; }
    }
}

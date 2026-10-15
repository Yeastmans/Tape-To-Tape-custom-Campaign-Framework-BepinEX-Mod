namespace CampaignCreator;

// Shared by import validation and selectors; excluded artwork cannot reappear through imports.
public static class EditorEquipmentPolicy
{
    public static bool IsSelectable(string value) =>
        // Preserve the legacy goalie identity accepted by the export catalog.
        value.Equals("Body/Mid_Cheese", StringComparison.OrdinalIgnoreCase)
        || CampaignCore.EquipmentCompatibility.IsSelectable(value);
}

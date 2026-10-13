namespace CampaignCreator;

// Shared by import validation and selectors; excluded artwork cannot reappear through imports.
public static class EditorEquipmentPolicy
{
    public static bool IsSelectable(string value) => CampaignCore.EquipmentCompatibility.IsSelectable(value);
}

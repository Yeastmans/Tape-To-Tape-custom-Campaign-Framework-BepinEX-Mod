using System.Buffers.Binary;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CampaignCreator;

/// <summary>Public campaign details, independent of its private management code and storage path.</summary>
internal sealed record CommunityPresentation(string CampaignName, string Author = "", string Description = "", string Icon = "", string DisplayName = "")
{
    private const int MaximumIconCharacters = 350000;
    private const string PngPrefix = "data:image/png;base64,";
    private const string ManagedPrefix = @"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12} - ";
    private const string TimestampPrefix = @"\d{4}-\d{2}-\d{2}T\d{2}-\d{2}-\d{2}-\d{3}Z - ";

    internal static string CleanDisplayName(string storageName)
    {
        string name = storageName.Trim();
        name = Regex.Replace(name, "^" + ManagedPrefix + "(?=" + TimestampPrefix + ")", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        name = Regex.Replace(name, "^" + TimestampPrefix, "", RegexOptions.CultureInvariant);
        if (name.EndsWith(".t2tcampaign", StringComparison.OrdinalIgnoreCase)) name = name[..^12];
        else if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        return name.Trim();
    }

    internal static CommunityPresentation Parse(JsonElement item, string storageName)
    {
        string name = Text(item, "name", 1000);
        string campaignName = Text(item, "campaignName", 160);
        string displayName = Text(item, "displayName", 240);
        if (displayName.Length == 0)
        {
            string fallback = name.Length > 0 ? name : storageName;
            if (Regex.IsMatch(Text(item, "path", 2000), "^/managed/" + ManagedPrefix, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                fallback = Regex.Replace(fallback, "^" + ManagedPrefix, "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            displayName = campaignName.Length > 0 ? (name.Length > 0 ? name : campaignName) : CleanDisplayName(fallback);
        }
        if (displayName.Length == 0) displayName = campaignName;
        string icon = Text(item, "icon", MaximumIconCharacters);
        if (!ValidIcon(icon)) icon = "";
        return new(campaignName.Length > 0 ? campaignName : displayName, Text(item, "author", 60),
            Text(item, "description", 2000), icon, displayName);
    }

    internal CommunityPresentation ValidateForUpload(string displayName)
    {
        string title = ValidateText(DisplayName.Length > 0 ? DisplayName : displayName, 240, "Display name");
        string campaign = ValidateText(CampaignName, 160, "Campaign name");
        string author = ValidateText(Author, 60, "Author");
        string description = ValidateText(Description, 2000, "Description");
        if (title.Length == 0 || campaign.Length == 0) throw new InvalidOperationException("A campaign name and display name are required.");
        if (!ValidIcon(Icon)) throw new InvalidOperationException("Campaign icons must be PNG images up to 512 × 512 pixels and 350,000 data URL characters.");
        return new(campaign, author, description, Icon, title);
    }

    private static string ValidateText(string value, int maximum, string label)
    {
        value = value.Trim();
        if (value.Length > maximum) throw new InvalidOperationException($"{label} must be {maximum} characters or fewer.");
        if (HasInvalidControls(value)) throw new InvalidOperationException($"{label} contains unsupported control characters.");
        return value;
    }

    private static string Text(JsonElement item, string field, int maximum)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(field, out var value) || value.ValueKind != JsonValueKind.String) return "";
        string text = value.GetString()?.Trim() ?? "";
        return text.Length <= maximum && !HasInvalidControls(text) ? text : "";
    }

    private static bool HasInvalidControls(string text) => text.Any(c => c < 32 && c is not ('\t' or '\r' or '\n'));

    private static bool ValidIcon(string icon)
    {
        if (icon.Length == 0) return true;
        if (icon.Length > MaximumIconCharacters || !icon.StartsWith(PngPrefix, StringComparison.Ordinal)) return false;
        if (!Regex.IsMatch(icon[PngPrefix.Length..], @"\A[A-Za-z0-9+/]+={0,2}\z", RegexOptions.CultureInvariant)) return false;
        byte[] bytes;
        try { bytes = Convert.FromBase64String(icon[PngPrefix.Length..]); }
        catch (FormatException) { return false; }
        // Reject other formats and oversized images before the UI decodes them.
        return bytes.Length >= 33 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            && BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(8, 4)) == 13
            && bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8)
            && BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4)) is >= 1 and <= 512
            && BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4)) is >= 1 and <= 512;
    }
}

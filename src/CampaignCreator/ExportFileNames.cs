using System.Globalization;
using System.Text;

namespace CampaignCreator;

internal static class ExportFileNames
{
    // The name is part of the initial identity, so native saves and sidecars keep
    // using the same path even if the exported entity is renamed later.
    internal static string NewId(string? name, string fallback)
    {
        var prefix = new StringBuilder(48);
        foreach (char c in (name ?? "").Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsAsciiLetterOrDigit(c)) prefix.Append(c);
            else if (prefix.Length > 0 && prefix[^1] != '-') prefix.Append('-');
            if (prefix.Length == 48) break;
        }
        string readable = prefix.ToString().TrimEnd('-');
        return (readable.Length == 0 ? fallback : readable) + "-" + Guid.NewGuid().ToString("D");
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CampaignCore;

public static class HeadAppearance
{
    private static readonly Dictionary<string, string> Variants = Load();
    public static string[] NativeChoices => Variants.Keys.Where(k => !k.StartsWith("Faces/CustomHeads/", StringComparison.Ordinal)).ToArray();
    public static void RegisterCustom(string head, bool supportsHelmet)
    {
        if (!head.StartsWith("Faces/CustomHeads/", StringComparison.Ordinal)) throw new ArgumentException("Invalid custom head identity.");
        Variants[head] = supportsHelmet ? head + "_Helmet" : "";
    }
    private static Dictionary<string, string> Load()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var stream = typeof(HeadAppearance).Assembly.GetManifestResourceStream("HeadFamilies.tsv");
        if (stream == null) throw new InvalidOperationException("Missing head family catalog.");
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is string line)
        {
            if (line.StartsWith("#")) continue;
            var cells = line.Split('\t');
            if (cells.Length == 2) result[cells[0]] = cells[1];
        }
        return result;
    }
    public static string Base(string head)
    {
        head = NativeSkin(head ?? "");
        if (!head.Contains("/"))
        {
            var matches = Variants.Keys.Concat(Variants.Values.Where(v => v.Length > 0))
                .Where(v => v.Substring(v.LastIndexOf('/') + 1).Equals(head, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length == 1) head = matches[0];
        }
        if (head.EndsWith("_Helmet", StringComparison.OrdinalIgnoreCase)
            && Variants.ContainsKey(head.Substring(0, head.Length - 7))) return head.Substring(0, head.Length - 7);
        return head;
    }
    // The standard native bench Stu uses this complete skin name even while
    // bareheaded. Its unsuffixed counterpart is Helmet_Face placeholder artwork,
    // not a bare variant. Repair the old editor selection without rewriting
    // ordinary fitted head identities used by preview callers.
    public static string NativeSkin(string head) =>
        string.Equals(head, "Faces/Anyteam/Bench_Stumple", StringComparison.OrdinalIgnoreCase)
        || string.Equals(head, "Bench_Stumple", StringComparison.OrdinalIgnoreCase)
            ? "Faces/Anyteam/Bench_Stumple_Helmet" : head;
    public static bool CanWearHelmet(string head) => Variants.TryGetValue(Base(head), out var variant) && variant.Length > 0;
    public static bool HasNoHelmetVariant(string head) => Variants.TryGetValue(Base(head), out var variant) && variant.Length == 0;
    public static bool IsNone(string helmet) => string.IsNullOrWhiteSpace(helmet)
        || helmet.Equals("none", StringComparison.OrdinalIgnoreCase) || helmet == "__NO_HELMET__";
    // Blank authored values inherit the team's helmet; only a deliberate choice removes it.
    public static bool IsExplicitNone(string helmet) => !string.IsNullOrWhiteSpace(helmet) && IsNone(helmet);
    public static string Resolve(string head, bool helmet) => helmet && CanWearHelmet(head) ? Variants[Base(head)] : Base(head);
    public static IEnumerable<string> Choices(IEnumerable<string> heads) => heads.Select(Base).Distinct(StringComparer.OrdinalIgnoreCase);
}

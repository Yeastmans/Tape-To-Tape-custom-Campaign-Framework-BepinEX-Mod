using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CampaignCore;

/// <summary>A portable Coach snapshot. A map owns its copy, including image and custom relic rules.</summary>
public sealed class CustomCoachDefinition
{
    public const string Extension = ".coach";
    public const int MaxSnapshotLength = 64 * 1024 * 1024;
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string ImagePng { get; set; } = "";
    public HashSet<string> Relics { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public List<CustomRewardDefinition> Rewards { get; } = new List<CustomRewardDefinition>();

    public void Validate()
    {
        if (!Guid.TryParseExact(Id, "D", out _)) throw new ArgumentException("The Custom Coach needs a valid stable ID.");
        if (string.IsNullOrWhiteSpace(Name) || Name.Trim().Length > 80 || Name.Any(char.IsControl))
            throw new ArgumentException("Enter a Coach name of 1–80 characters on one line.");
        if (Relics.Count == 0) throw new ArgumentException("Select at least one relic for this Coach.");
        if (Relics.Any(id => string.IsNullOrWhiteSpace(id) || id != id.Trim() || id.Length > 200 || id.Any(char.IsControl) || id.Contains(',')))
            throw new ArgumentException("The Coach contains an invalid relic ID.");
        if (ImagePng.Length > 5600000) throw new ArgumentException("Choose a PNG no larger than 4 MB.");
        if (ImagePng.Length > 0) CustomRewardDefinition.ValidatePng(Convert.FromBase64String(ImagePng));
        var dependencies = new Dictionary<string, CustomRewardDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var reward in Rewards)
        {
            if (reward == null || !reward.IsRelic || reward.NeedsRedesign || !Relics.Contains(reward.Id))
                throw new ArgumentException("A Custom Coach may embed only its selected, supported custom relics.");
            reward.Validate();
            CustomRewardExport.AddSnapshot(dependencies, reward);
        }
    }

    public CampaignDocument ToDocument()
    {
        Validate();
        var document = new CampaignDocument("# Custom Coach snapshot\n");
        document.Set("Format", "1"); document.Set("Coach ID", Id);
        document.Set("Name", Name.Trim()); document.Set("Behavior", "Coach");
        document.Set("Relics", string.Join(", ", Relics.OrderBy(id => id, StringComparer.OrdinalIgnoreCase)));
        document.Set("Image PNG", ImagePng);
        CustomRewardExport.Embed(document, Rewards);
        if (document.ToString().Length > MaxSnapshotLength) throw new ArgumentException("This Custom Coach snapshot is too large. Use smaller images for its custom relics.");
        return document;
    }

    public static CustomCoachDefinition Parse(string text)
    {
        if (text == null || text.Length > MaxSnapshotLength) throw new ArgumentException("Custom Coach snapshot is too large.");
        var document = new CampaignDocument(text);
        if (document.Get("Format") != "1" || document.Get("Behavior", "Coach") != "Coach")
            throw new ArgumentException("Unsupported Custom Coach format or behavior.");
        var result = new CustomCoachDefinition { Id = document.Get("Coach ID"), Name = document.Get("Name"), ImagePng = document.Get("Image PNG") };
        result.Relics.UnionWith(document.Get("Relics").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0));
        result.Rewards.AddRange(CustomRewardExport.Read(document));
        result.Validate();
        return result;
    }

    public string Encode() => Convert.ToBase64String(Encoding.UTF8.GetBytes(ToDocument().ToString()));
    public static CustomCoachDefinition Decode(string encoded)
    {
        if (encoded == null || encoded.Length > MaxSnapshotLength * 2) throw new ArgumentException("Custom Coach snapshot is too large.");
        return Parse(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)));
    }
    public CustomCoachDefinition Copy() => Parse(ToDocument().ToString());
    public CustomCoachDefinition SaveNew(string directory)
    {
        var copy = Copy(); copy.Id = Guid.NewGuid().ToString();
        Directory.CreateDirectory(directory);
        copy.ToDocument().Save(Path.Combine(directory, copy.Id + Extension));
        return copy;
    }
}

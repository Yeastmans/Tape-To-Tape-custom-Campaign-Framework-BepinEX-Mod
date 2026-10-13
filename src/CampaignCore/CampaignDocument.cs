using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CampaignCore;

/// <summary>Lossless legacy campaign document shared by editor and game adapter.</summary>
public sealed class CampaignDocument
{
    private readonly List<string> lines;
    private Dictionary<string, string> parsed = null!;
    public CampaignDocument(string text) => lines = text.Replace("\r\n", "\n").Split('\n').ToList();
    public static bool TryReadField(string raw, out string key, out string value)
    {
        key = value = "";
        string line = raw.Replace("\t", " ").Trim();
        if (line.StartsWith("#") || line.StartsWith("====")) return false;
        int eq = line.IndexOf('=');
        if (eq < 1) return false;
        key = line.Substring(0, eq).Trim();
        value = line.Substring(eq + 1).Trim();
        return key.Length > 0;
    }
    private Dictionary<string, string> Parsed
    {
        get
        {
            if (parsed != null) return parsed;
            parsed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in lines)
                if (TryReadField(line, out var key, out var value)) parsed[key] = value;
            return parsed;
        }
    }
    // Callers historically receive an editable snapshot, never the live document.
    public Dictionary<string, string> Fields => new Dictionary<string, string>(Parsed, StringComparer.OrdinalIgnoreCase);
    public string Get(string key, string fallback = "") => Parsed.TryGetValue(key, out var v) ? v : fallback;
    public void Set(string key, string value)
    {
        if (key.IndexOfAny(new[] { '\r', '\n', '=' }) >= 0 || value.IndexOfAny(new[] { '\r', '\n' }) >= 0)
            throw new ArgumentException("A field must fit on one line.");
        parsed = null!;
        bool found = false;
        for (int i = 0; i < lines.Count; i++)
            if (TryReadField(lines[i], out var k, out _) && k.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = lines[i].Substring(0, lines[i].IndexOf('=') + 1) + " " + value;
                found = true;
            }
        if (!found) lines.Add(key.PadRight(28) + "= " + value);
    }
    public void Remove(string key)
    {
        lines.RemoveAll(line => TryReadField(line, out var k, out _) && k.Equals(key, StringComparison.OrdinalIgnoreCase));
        parsed = null!;
    }
    public override string ToString() => string.Join(Environment.NewLine, lines);
    public void Save(string path)
    {
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, ToString(), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}


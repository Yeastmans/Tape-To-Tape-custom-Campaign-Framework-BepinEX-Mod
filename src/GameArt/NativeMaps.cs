using System.Globalization;
using System.Text;
using AssetsTools.NET;

namespace GameArt;

/// <summary>Native map graphs and per-node pools, read from the installed assets.</summary>
public sealed class NativeMaps : IArtExtractor
{
    public string Folder => "base-maps";
    public string Description => "Base game maps and random node choices";

    public void Extract(UnityGame game, string output, IProgress<string>? progress)
    {
        var configs = game.Behaviours("MapConfig").ToDictionary(x => (x.File.name, x.Info.PathId));
        var templates = game.Behaviours("MapTemplateData").ToDictionary(x => (x.File.name, x.Info.PathId));
        var pools = game.Behaviours("NodeTypeConfig").ToDictionary(x => (x.File.name, x.Info.PathId), x =>
        {
            var data = x.Value["serializationData"];
            if (data["SerializedFormat"].AsInt != 0) throw new InvalidDataException("Unsupported native random-pool format.");
            return ReadPool(data["SerializedBytes"]["Array"].AsByteArray);
        });
        var text = new StringBuilder("# Native map graphs and pools read from the installed game.\n");
        text.AppendLine("# game_sha256=" + Png.Sha256(Path.Combine(game.Root, "GameAssembly.dll")));
        int count = 0;
        foreach (var squad in game.Behaviours("RunSquadScriptableObject"))
        {
            var maps = squad.Value["maps"]["Array"].Children;
            if (maps.Count == 0) continue;
            string name = squad.Value["squadName"].AsString;
            text.AppendLine($"SQUAD '{name}' maps={maps.Count}");
            for (int mapIndex = 0; mapIndex < maps.Count; mapIndex++)
            {
                var reference = game.Resolve(squad.File, maps[mapIndex]) ?? throw new InvalidDataException("A native map reference is missing.");
                if (!configs.TryGetValue((reference.File.name, reference.Info.PathId), out var config))
                    throw new InvalidDataException("A native map config is missing.");
                var poolReference = game.Resolve(config.File, config.Value["nodeTypeConfig"])
                    ?? throw new InvalidDataException("A native map has no random-pool config.");
                var fallback = pools[(poolReference.File.name, poolReference.Info.PathId)];
                var variants = config.Value["mapTemplates"]["Array"].Children;
                text.AppendLine($"  MAP index={mapIndex} act={config.Value["act"].AsInt} templates={variants.Count}");
                for (int variant = 0; variant < variants.Count; variant++)
                {
                    var templateReference = game.Resolve(config.File, variants[variant])
                        ?? throw new InvalidDataException("A native map variant is missing.");
                    var template = templates[(templateReference.File.name, templateReference.Info.PathId)];
                    text.AppendLine($"    TEMPLATE {variant} '{template.Value["m_Name"].AsString}'");
                    foreach (var layer in template.Value["layers"]["Array"].Children)
                    {
                        text.AppendLine($"      LAYER {layer["layerIndex"].AsInt}");
                        foreach (var node in layer["nodes"]["Array"].Children)
                        {
                            string type = NodeName(node["type"].AsInt);
                            string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);
                            string pool = "";
                            if (type == "NULL")
                            {
                                var custom = node["nodeTypeWeights"]["Array"].Children;
                                var weights = custom.Count == 0 ? fallback : custom.ToDictionary(w => NodeName(w["type"].AsInt), w => w["weight"].AsFloat);
                                if (weights.Values.Any(w => !float.IsFinite(w) || w < 0)) throw new InvalidDataException("Invalid native event weight.");
                                pool = " weights=[" + string.Join(",", weights.OrderBy(w => w.Key, StringComparer.Ordinal).Select(w => w.Key + ":" + Number(w.Value))) + "]";
                            }
                            string outgoing = string.Join(",", node["outgoing"]["Array"].Children.Select(v => v.AsInt));
                            var position = node["gridPosition"];
                            text.AppendLine($"        NODE n={node["nodeIndex"].AsInt} type={type} pos={Number(position["x"].AsFloat)},{Number(position["y"].AsFloat)}"
                                + $" out=[{outgoing}] group='{node["eliteGroupId"].AsString}' team='{node["eliteTeamId"].AsString}'"
                                + $" gm={node["gmSelectionCount"].AsInt} talents={node["talentRewardCount"].AsInt} relics={node["relicRewardCount"].AsInt}" + pool);
                        }
                    }
                    count++;
                }
            }
        }
        if (count == 0) throw new InvalidDataException("No native map variants were found.");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "_game_maps.txt"), text.ToString());
    }

    // DAGG.Core.NodeType, not STS.Map.NodeType (Coach/GM have different values).
    private static string NodeName(int value) => value switch
    {
        0 => "Challenge", 1 => "Elite", 2 => "Boss", 3 => "RandomConditioning", 4 => "Chaos", 5 => "Nurse",
        6 => "Blademaster", 7 => "FanNumber1", 8 => "Coach", 9 => "GeneralManager", 10 => "NULL",
        12 => "PlayerTraining", 13 => "TeamTraining", 14 => "StatTraining",
        _ => throw new InvalidDataException("Unrecognized native node type: " + value)
    };

    // Read only the bounded enum/float dictionary stored by NodeTypeConfig.
    // Format reference: TeamSirenix/odin-serializer, BinaryEntryType and BinaryDataReader.
    // No type resolution or object deserialization is performed. A changed shape
    // fails extraction visibly instead of substituting guessed default weights.
    private static Dictionary<string, float> ReadPool(byte[] bytes)
    {
        using var reader = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8, false);
        void Expect(byte value) { if (reader.ReadByte() != value) throw new InvalidDataException("Unsupported native random-pool encoding."); }
        string ReadString()
        {
            byte width = reader.ReadByte();
            int length = reader.ReadInt32();
            if (width > 1 || length < 0 || length > 4096) throw new InvalidDataException("Invalid native random-pool string.");
            int size = checked(length * (width + 1));
            var data = reader.ReadBytes(size);
            if (data.Length != size) throw new EndOfStreamException();
            return (width == 1 ? Encoding.Unicode : Encoding.Latin1).GetString(data);
        }
        void Name(string expected) { if (ReadString() != expected) throw new InvalidDataException("Unexpected native random-pool field."); }
        string Reference(string name)
        {
            Expect(0x01); Name(name); Expect(0x2f); reader.ReadInt32();
            string type = ReadString(); reader.ReadInt32(); return type;
        }
        string dictionary = Reference("nodeTypeChances");
        if (!dictionary.StartsWith("System.Collections.Generic.Dictionary`2[[DAGG.Core.NodeType,", StringComparison.Ordinal)
            || !dictionary.Contains("[System.Single,", StringComparison.Ordinal)) throw new InvalidDataException("Unexpected native random-pool type.");
        string comparer = Reference("comparer");
        if (!comparer.StartsWith("System.Collections.Generic.EnumEqualityComparer`1[[DAGG.Core.NodeType,", StringComparison.Ordinal))
            throw new InvalidDataException("Unexpected native random-pool comparer.");
        Expect(0x06); if (reader.ReadInt64() != 0) throw new InvalidDataException("Unexpected native random-pool comparer data.");
        Expect(0x07); Expect(0x05); Expect(0x06);
        long count = reader.ReadInt64();
        if (count < 1 || count > 128) throw new InvalidDataException("Invalid native random-pool size.");
        var result = new Dictionary<string, float>();
        for (int i = 0; i < count; i++)
        {
            Expect(0x04); Expect(0x2e); Expect(0x1d); Name("$k");
            ulong key = reader.ReadUInt64();
            if (key > int.MaxValue) throw new InvalidDataException("Invalid native random event.");
            Expect(0x1f); Name("$v"); float weight = reader.ReadSingle();
            if (!float.IsFinite(weight) || weight < 0) throw new InvalidDataException("Invalid native random weight.");
            result.Add(NodeName((int)key), weight); Expect(0x05);
        }
        Expect(0x07); Expect(0x05);
        if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("Unexpected trailing native random-pool data.");
        return result;
    }
}

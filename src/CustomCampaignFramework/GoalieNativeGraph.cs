using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace EndlessMode;

// The native ES3 graph reader owns derived-state dispatch. Never substitute a
// base-class JSON projection or a hand-maintained list of earned state types.
internal static class GoalieNativeGraph
{
    internal const string Profile = "es3-json-utf8-byvalue-v1";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    internal static ES3Settings Settings() => new ES3Settings(false) {
        format = ES3.Format.JSON, encoding = Il2CppSystem.Text.Encoding.UTF8,
        encryptionType = ES3.EncryptionType.None, compressionType = ES3.CompressionType.None,
        referenceMode = ES3.ReferenceMode.ByValue, memberReferenceMode = ES3.ReferenceMode.ByValue,
        serializationDepthLimit = 256, typeChecking = true, safeReflection = true, prettyPrint = false
    };
    internal static byte[] ObjectBytes(byte[] bytes)
    {
        if (bytes == null || bytes.Length < 2 || bytes[0] != (byte)'{' || bytes[^1] != (byte)'}')
            throw new InvalidDataException("Expected an unmarked ES3 JSON object payload.");
        try { StrictUtf8.GetString(bytes); }
        catch (DecoderFallbackException ex) { throw new InvalidDataException("Invalid ES3 UTF-8 payload.", ex); }
        return bytes;
    }
    internal static string Capture(Il2CppSystem.Object value)
    {
        if (value == null) throw new InvalidDataException("Missing native graph.");
        var type = ES3Internal.ES3TypeMgr.GetOrCreateES3Type(value.GetIl2CppType(), true);
        return Convert.ToBase64String(ObjectBytes(ES3.Serialize(value, type, Settings()).ToArray()));
    }
    internal static T Restore<T>(string payload) where T : Il2CppSystem.Object
    {
        byte[] bytes;
        try { bytes = ObjectBytes(Convert.FromBase64String(payload ?? "")); }
        catch (FormatException ex) { throw new InvalidDataException("Invalid native graph encoding.", ex); }
        var expected = Il2CppType.Of<T>();
        var type = ES3Internal.ES3TypeMgr.GetOrCreateES3Type(expected, true);
        var value = ES3.Deserialize(type, new Il2CppStructArray<byte>(bytes), Settings());
        if (value == null || value.GetIl2CppType().FullName != expected.FullName)
            throw new InvalidDataException("Native graph role/type mismatch.");
        return value.TryCast<T>() ?? throw new InvalidDataException("Native graph cast failed.");
    }
    // Snapshot validation only, never checkpoint identity. Object property order
    // is irrelevant here; type tags, array order and every scalar remain exact.
    internal static bool Equivalent(string saved, string reconstructed)
    {
        using var first = JsonDocument.Parse(Convert.FromBase64String(saved));
        using var second = JsonDocument.Parse(Convert.FromBase64String(reconstructed));
        bool Same(JsonElement a, JsonElement b)
        {
            if (a.ValueKind != b.ValueKind) return false;
            if (a.ValueKind == JsonValueKind.Object)
                return a.EnumerateObject().Count() == b.EnumerateObject().Count()
                    && a.EnumerateObject().All(p => b.TryGetProperty(p.Name, out var value) && Same(p.Value, value));
            if (a.ValueKind == JsonValueKind.Array)
                return a.GetArrayLength() == b.GetArrayLength() && a.EnumerateArray().Zip(b.EnumerateArray(), Same).All(x => x);
            return a.ValueKind == JsonValueKind.String ? a.GetString() == b.GetString() : a.GetRawText() == b.GetRawText();
        }
        return Same(first.RootElement, second.RootElement);
    }
}

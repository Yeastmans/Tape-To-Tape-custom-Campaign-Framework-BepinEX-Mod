// Native boundary doubles only. These exercise adapter decisions, not native ES3
// execution, AOT metadata, Unity ownership or real game save fidelity.
using System.Text;
using System.Text.Json;
namespace Il2CppSystem {
    public class Type : Object { public System.Type Managed; public string FullName => Managed.FullName; }
    public class Object {
        [System.Text.Json.Serialization.JsonIgnore] public IntPtr Pointer { get; } = NativeRegistry.Next();
        public Object() { NativeRegistry.Values[Pointer] = this; }
        public Object(IntPtr pointer) { Pointer = pointer; }
        public Type GetIl2CppType() => new() { Managed = GetType() };
        public T Cast<T>() where T : class => (T)(object)this;
        public T TryCast<T>() where T : class => this as T;
    }
    public class String : Object { public string Value; public static implicit operator String(string value) => new() { Value = value }; public static implicit operator string(String value) => value?.Value; }
}
static class NativeRegistry { static int next; internal static readonly Dictionary<IntPtr, object> Values = new(); internal static IntPtr Next() => (IntPtr)(++next); }
namespace Il2CppSystem.Text { public class Encoding : Il2CppSystem.Object { public int CodePage => 65001; public static Encoding UTF8 { get; } = new(); } }
namespace Il2CppInterop.Runtime { public static class Il2CppType { public static Il2CppSystem.Type Of<T>() => new() { Managed = typeof(T) }; } }
namespace Il2CppInterop.Runtime.InteropTypes.Arrays {
    public class Il2CppStructArray<T> : List<T> {
        public IntPtr Pointer { get; } = NativeRegistry.Next();
        public Il2CppStructArray(T[] values) : base(values) { NativeRegistry.Values[Pointer] = this; }
        public Il2CppStructArray(IntPtr pointer) : this(((Il2CppStructArray<T>)NativeRegistry.Values[pointer]).ToArray()) {}
    }
}
namespace ES3Types { public class ES3Type : Il2CppSystem.Object { public Il2CppSystem.Type type; } }
namespace ES3Internal { public static class ES3TypeMgr {
    static readonly Dictionary<System.Type, ES3Types.ES3Type> types = new();
    public static ES3Types.ES3Type GetOrCreateES3Type(Il2CppSystem.Type type, bool fail) {
        if (!types.TryGetValue(type.Managed, out var value)) types[type.Managed] = value = new() { type = type }; return value;
    }
} }
public class ES3Settings : Il2CppSystem.Object {
    public ES3Settings(bool defaults) {}
    public ES3Settings(string path, ES3Settings source) {
        this.path = path; format = source.format; encoding = source.encoding; encryptionType = source.encryptionType; compressionType = source.compressionType;
        referenceMode = source.referenceMode; memberReferenceMode = source.memberReferenceMode; serializationDepthLimit = source.serializationDepthLimit;
        typeChecking = source.typeChecking; safeReflection = source.safeReflection; prettyPrint = source.prettyPrint; location = source.location;
        directory = source.directory; encryptionPassword = source.encryptionPassword; bufferSize = source.bufferSize; saveChildren = source.saveChildren;
        postprocessRawCachedData = source.postprocessRawCachedData; storeCacheAtEndOfEveryFrame = source.storeCacheAtEndOfEveryFrame;
        storeCacheOnApplicationQuit = source.storeCacheOnApplicationQuit; storeCacheOnApplicationPause = source.storeCacheOnApplicationPause;
        autoCacheDefaultFile = source.autoCacheDefaultFile; autoCacheFileOnLoad = source.autoCacheFileOnLoad; assemblyNames = source.assemblyNames;
    }
    public static ES3Settings defaultSettings = new(false) { encoding = Il2CppSystem.Text.Encoding.UTF8, serializationDepthLimit = 256, typeChecking = true, referenceMode = ES3.ReferenceMode.ByValue, memberReferenceMode = ES3.ReferenceMode.ByValue };
    public string path; public ES3.Location location;
    public ES3.Format format; public Il2CppSystem.Text.Encoding encoding; public ES3.EncryptionType encryptionType; public ES3.CompressionType compressionType;
    public int directory, bufferSize; public string encryptionPassword; public string[] assemblyNames;
    public bool saveChildren, postprocessRawCachedData, storeCacheAtEndOfEveryFrame, storeCacheOnApplicationQuit, storeCacheOnApplicationPause, autoCacheDefaultFile, autoCacheFileOnLoad;
    public ES3.ReferenceMode referenceMode, memberReferenceMode; public int serializationDepthLimit; public bool typeChecking, safeReflection, prettyPrint;
}
public static partial class ES3 {
    public enum Location { File, Cache }
    public static readonly IntPtr NativeMethodInfoPtr_Serialize_Internal_Static_Il2CppStructArray_1_Byte_Object_ES3Type_ES3Settings_0 = (IntPtr)123;
    public enum Format { JSON } public enum EncryptionType { None } public enum CompressionType { None } public enum ReferenceMode { ByRef, ByValue }
    static readonly JsonSerializerOptions Options = new() { IncludeFields = true };
    static void Profile(ES3Settings s) {
        if (s.referenceMode != ReferenceMode.ByValue || s.memberReferenceMode != ReferenceMode.ByValue || s.encoding.CodePage != 65001
            || s.serializationDepthLimit != 256 || !s.typeChecking) throw new Exception("Unexpected ES3 adapter settings");
    }
    public static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte> Serialize(Il2CppSystem.Object value, ES3Types.ES3Type type, ES3Settings settings) {
        var args = new object[] { value, type, settings, null };
        if (!BridgeDoubleHooks.Call<bool>("SerializePrefix", args)) return (Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte>)args[3];
        Profile(settings); return new(JsonSerializer.SerializeToUtf8Bytes(value, type.type.Managed, Options));
    }
    public static Il2CppSystem.Object Deserialize(ES3Types.ES3Type type, Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte> bytes, ES3Settings settings) {
        var args = new object[] { type, bytes, null }; BridgeDoubleHooks.Call("DeserializePrefix", args);
        Profile(settings); var value = (Il2CppSystem.Object)JsonSerializer.Deserialize(bytes.ToArray(), type.type.Managed, Options);
        BridgeDoubleHooks.Call("DeserializePostfix", new object[] { value, args[2] }); return value;
    }
}

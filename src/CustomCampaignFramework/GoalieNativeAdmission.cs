using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using State;

namespace EndlessMode;

internal static class GoalieNativeAdmission
{
    private static IntPtr serialize;
    internal static void Preflight()
    {
        RuntimeHelpers.RunClassConstructor(typeof(ES3).TypeHandle);
        serialize = (IntPtr)AccessTools.Field(typeof(ES3),
            "NativeMethodInfoPtr_Serialize_Internal_Static_Il2CppStructArray_1_Byte_Object_ES3Type_ES3Settings_0").GetValue(null);
        if (serialize == IntPtr.Zero) throw new InvalidOperationException("Missing exact non-generic native Serialize MethodInfo.");
    }
    internal static ES3Settings SaveSettings(string path)
    {
        ES3Settings source;
        if (ES3File.cachedFiles.TryGetValue(path, out var file)) source = file.settings;
        else source = new ES3Settings(path, ES3Settings.defaultSettings) {
            location = ES3.Location.Cache, referenceMode = ES3.ReferenceMode.ByValue, memberReferenceMode = ES3.ReferenceMode.ByValue
        };
        // Exactly ES3File.Save<object>'s copied serialization profile, including
        // native depth/reflection settings; the snapshot profile is separate.
        return new ES3Settings(path, source) { encryptionType = ES3.EncryptionType.None, compressionType = ES3.CompressionType.None };
    }
    internal sealed class Frozen
    {
        internal readonly RunDataModelV2 Model;
        internal readonly ES3Types.ES3Type Type;
        internal readonly Il2CppStructArray<byte> NativeBytes;
        internal readonly byte[] Bytes;
        private readonly string profile;
        internal Frozen(RunDataModelV2 model, ES3Types.ES3Type type, Il2CppStructArray<byte> bytes, ES3Settings settings)
        { Model = model; Type = type; Bytes = bytes.ToArray(); NativeBytes = new Il2CppStructArray<byte>(Bytes); profile = Profile(settings); }
        internal bool MatchesProfile(ES3Settings settings) => settings != null && profile == Profile(settings);
    }
    // Own scalar/array values, not the mutable settings wrapper. Include all
    // native settings fields conservatively, plus exact encoding identity.
    private static string Profile(ES3Settings s) => JsonSerializer.Serialize(new {
        s.path, s.location, s.directory, s.format, s.encryptionType, s.compressionType, s.encryptionPassword,
        Encoding = s.encoding?.Pointer.ToInt64(), CodePage = s.encoding?.CodePage,
        s.prettyPrint, s.bufferSize, s.saveChildren, s.postprocessRawCachedData,
        s.storeCacheAtEndOfEveryFrame, s.storeCacheOnApplicationQuit, s.storeCacheOnApplicationPause,
        s.autoCacheDefaultFile, s.autoCacheFileOnLoad, s.typeChecking, s.safeReflection,
        s.referenceMode, s.memberReferenceMode, s.serializationDepthLimit, AssemblyNames = s.assemblyNames?.ToArray()
    });
    // This is the generated wrapper's exact three-reference runtime-invoke ABI.
    // Retain the native exception before the normal wrapper would discard its
    // pointer in a managed Il2CppException containing only message text.
    internal static unsafe Frozen Freeze(RunDataModelV2 model, string path, out Il2CppSystem.Exception error)
    {
        if (serialize == IntPtr.Zero) throw new InvalidOperationException("Native admission was not preflighted.");
        var settings = SaveSettings(path);
        var type = ES3Internal.ES3TypeMgr.GetOrCreateES3Type(model?.GetIl2CppType() ?? Il2CppType.Of<RunDataModelV2>(), true);
        void** args = stackalloc void*[3];
        args[0] = (void*)IL2CPP.Il2CppObjectBaseToPtr(model);
        args[1] = (void*)IL2CPP.Il2CppObjectBaseToPtr(type);
        args[2] = (void*)IL2CPP.Il2CppObjectBaseToPtr(settings);
        IntPtr exception = IntPtr.Zero;
        IntPtr result = IL2CPP.il2cpp_runtime_invoke(serialize, IntPtr.Zero, args, ref exception);
        error = exception == IntPtr.Zero ? null : new Il2CppSystem.Exception(exception);
        if (error != null) return null;
        if (result == IntPtr.Zero) throw new InvalidDataException("Native Serialize returned no bytes without an exception.");
        return new Frozen(model, type, new Il2CppStructArray<byte>(result), settings);
    }
}

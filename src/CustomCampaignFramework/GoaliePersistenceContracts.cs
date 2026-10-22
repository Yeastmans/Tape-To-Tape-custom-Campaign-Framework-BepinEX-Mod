using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using CampaignCore;
using HarmonyLib;
using Data;
using State;

namespace EndlessMode;

internal static class GoaliePersistenceContracts
{
    internal const string Feature = "goalie-checkpoint";
    internal static readonly NamedManagedHookContract[] Required = {
        Target("save", "State.RunDataRepositoryV2", "SaveToPersistentStorage", false, "Il2CppSystem.Threading.Tasks.Task<System.Boolean>", new[] { "State.RunDataModelV2" }, "SavePrefix"),
        Target("load", "State.RunDataRepositoryV2", "LoadFromPersistentStorage", false, "Il2CppSystem.Threading.Tasks.Task<State.RunDataModelV2>", Array.Empty<string>(), "LoadPrefix"),
        Target("serialize", "ES3", "Serialize", true, "Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<System.Byte>", new[] { "Il2CppSystem.Object", "ES3Types.ES3Type", "ES3Settings" }, "SerializePrefix"),
        Target("deserialize", "ES3", "Deserialize", true, "Il2CppSystem.Object", new[] { "ES3Types.ES3Type", "Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<System.Byte>", "ES3Settings" }, "DeserializePrefix", "DeserializePostfix"),
        Target("store", "ES3", "StoreCachedFile", true, "System.Void", new[] { "System.String" }, "StorePrefix"),
        Target("cache", "ES3File", "CacheFile", true, "ES3File", new[] { "ES3Settings" }, "CachePrefix"),
        Target("fresh", "State.CampaignState", "ApplyNewRunStartEffects", false, "System.Void", Array.Empty<string>(), postfix: "FreshPostfix"),
        Target("hydrate", "State.DeserializerV2", "DeserializeFromModel", false, "System.Void", new[] { "Data.RunData", "State.RunDataModelV2" }, postfix: "HydratedPostfix")
    };
    private static NamedManagedHookContract Target(string id, string type, string method, bool isStatic, string result, string[] args, string prefix = "", string postfix = "")
        => new() { Feature = Feature, Id = "goalie-" + id, Assembly = "MainScriptsAssembly", DeclaringType = type, Method = method,
            IsStatic = isStatic, ReturnType = result, Parameters = args, Prefix = prefix, Postfix = postfix };
    internal static (MethodInfo Native, MethodInfo Prefix, MethodInfo Postfix)[] Resolve()
        => Required.Select(c => (JerseyLogoManagedTargets.Resolve(c, AppDomain.CurrentDomain.GetAssemblies()),
            c.Prefix.Length == 0 ? null : AccessTools.Method(typeof(GoaliePersistenceBridge), c.Prefix) ?? throw new MissingMethodException(c.Prefix),
            c.Postfix.Length == 0 ? null : AccessTools.Method(typeof(GoaliePersistenceBridge), c.Postfix) ?? throw new MissingMethodException(c.Postfix))).ToArray();

    // Generated interop DLLs can have different MVIDs after regeneration. Check
    // callable structure and exercise exact closed promises instead of pinning
    // an incidental whole-file generated-wrapper hash.
    private static void ValidateWrapperApi()
    {
        void Method(Type type, string name, Type result, params Type[] args)
        {
            var method = AccessTools.Method(type, name, args);
            if (method == null || method.ReturnType != result) throw new MissingMethodException("Goalie wrapper contract: " + type.FullName + "." + name);
        }
        void Constructor(Type type, params Type[] args)
        { if (type.GetConstructor(args) == null) throw new MissingMethodException("Goalie wrapper constructor: " + type.FullName); }
        Method(typeof(ES3), "Save", typeof(void), typeof(string), typeof(Il2CppSystem.Object), typeof(ES3Settings));
        Method(typeof(ES3File), "Load", typeof(Il2CppSystem.Object), typeof(string));
        Method(typeof(ES3File), "KeyExists", typeof(bool), typeof(string));
        Method(typeof(ES3Internal.ES3TypeMgr), "GetOrCreateES3Type", typeof(ES3Types.ES3Type), typeof(Il2CppSystem.Type), typeof(bool));
        Method(typeof(SerializerV2), "SerializeToModel", typeof(SkaterDataModelV2), typeof(GoaltenderData));
        Method(typeof(DeserializerV2), "ConvertGoaltenderDataModelToGoaltenderData", typeof(GoaltenderData), typeof(GoaltenderDataModelV2));
        Method(typeof(SkaterData), "SaveState", typeof(SkaterState));
        Method(typeof(SkaterData), "LoadState", typeof(void), typeof(SkaterState));
        Constructor(typeof(ES3File), typeof(ES3Settings), typeof(bool));
        Constructor(typeof(ES3Settings), typeof(bool)); Constructor(typeof(ES3Settings), typeof(string), typeof(ES3Settings));
        var repository = typeof(RunDataRepositoryV2);
        var dependencies = new[] { "_talentRepository", "_relicRepository", "_abilityRepository" }.Select(name => repository.GetProperty(name)?.PropertyType
            ?? throw new MissingMemberException("Native repository injection contract: " + name)).ToArray();
        var constructors = typeof(DeserializerV2).GetConstructors().Where(c => c.GetParameters().Length == 4).ToArray();
        if (constructors.Length != 1 || !constructors[0].GetParameters().Take(3).Select(p => p.ParameterType).SequenceEqual(dependencies)
            || constructors[0].GetParameters()[3].ParameterType.FullName != "Tape2Tape.Customization.ILogoRepository")
            throw new MissingMethodException("Native injected deserializer constructor contract changed.");
    }
    internal static NativeCompatibilityProfile Preflight()
    {
        {
            if (!OperatingSystem.IsWindows() || IntPtr.Size != 8) throw new PlatformNotSupportedException("Audited Windows x64 required.");
            var module = Process.GetCurrentProcess().Modules.Cast<ProcessModule>().Single(m => m.ModuleName.Equals("GameAssembly.dll", StringComparison.OrdinalIgnoreCase));
            var profile = NativeCompatibility.Load(module.FileName);
            if (profile.GameHash != "A58718A1AF369E064DF3A7CE149129C0130C4F316BC5E47EF7E6365853B1AA42"
                || profile.MetadataHash != "7CABE7311509564E5C2D4A0580212E2839FAA0CAC05949940015DD4D092E4EDB")
                throw new InvalidDataException("Goalie checkpoint adapter requires pinned build 26141904.");
            NamedManagedHookContract.RequireExactSet(Required, profile.NamedHooks, Feature);
            ValidateWrapperApi();
            foreach (var dependency in new[] {
                ("Il2CppInterop.HarmonySupport", "F7612EA2E5A4D473B771F34EE3B20CF32DC0C4ECEA0185416474999A17FBCC07"),
                ("Il2CppInterop.Runtime", "65AB051A681C2C1E52DF7596FF738BE401AAE72CFF882FF18241CF54A3EA38A4") })
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == dependency.Item1);
                if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))) != dependency.Item2)
                    throw new InvalidDataException("Goalie native dependency changed: " + dependency.Item1);
            }
            GoaliePersistenceBridge.Preflight();
            return profile;
        }
    }
}

using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using CampaignCore;
using HarmonyLib;
namespace EndlessMode;

// Existing custom starting rewards and skater contracts do not depend on goalie
// capability activation. Both owners still require the pinned native profile.
internal static class FreshRunActivation
{
    internal static bool Installed { get; private set; }
    private const string Owner = "EndlessMode.FreshRun";
    internal const string Feature = "campaign-fresh-run";
    internal static readonly NamedManagedHookContract[] Required = {
        Hook("reset", "State.CampaignState", "ResetRunData", Array.Empty<string>(), "ResetPrefix", "ResetPostfix"),
        Hook("map", "RunManager", "CreateMapFromScratch", Array.Empty<string>(), "MapPrefix", ""),
        Hook("hydrate", "State.DeserializerV2", "DeserializeFromModel", new[] { "Data.RunData", "State.RunDataModelV2" }, "HydratePrefix", "")
    };
    private static NamedManagedHookContract Hook(string id, string type, string method, string[] args, string prefix, string postfix)
        => new() { Feature=Feature, Id="fresh-run-"+id, Assembly="MainScriptsAssembly", DeclaringType=type, Method=method,
            ReturnType="System.Void", Parameters=args, Prefix=prefix, Postfix=postfix };
    internal static (MethodInfo Native, MethodInfo Prefix, MethodInfo Postfix)[] Resolve()
        => Required.Select(c => (JerseyLogoManagedTargets.Resolve(c, AppDomain.CurrentDomain.GetAssemblies()),
            AccessTools.Method(typeof(FreshRunLifecycle), c.Prefix) ?? throw new MissingMethodException(c.Prefix),
            c.Postfix.Length == 0 ? null : AccessTools.Method(typeof(FreshRunLifecycle), c.Postfix) ?? throw new MissingMethodException(c.Postfix))).ToArray();
    internal static void Install()
    {
        if (Installed) return;
        var harmony = new Harmony(Owner);
        try {
            var module = Process.GetCurrentProcess().Modules.Cast<ProcessModule>().Single(m => m.ModuleName.Equals("GameAssembly.dll", StringComparison.OrdinalIgnoreCase));
            var profile = NativeCompatibility.Load(module.FileName);
            if (profile.GameHash != "A58718A1AF369E064DF3A7CE149129C0130C4F316BC5E47EF7E6365853B1AA42"
                || profile.MetadataHash != "7CABE7311509564E5C2D4A0580212E2839FAA0CAC05949940015DD4D092E4EDB") throw new InvalidOperationException("Fresh run boundary requires audited build 26141904.");
            NamedManagedHookContract.RequireExactSet(Required, profile.NamedHooks, Feature);
            foreach (var h in Resolve()) harmony.Patch(h.Native, new HarmonyMethod(h.Prefix), h.Postfix == null ? null : new HarmonyMethod(h.Postfix));
            foreach (var h in Resolve()) {
                var patches = Harmony.GetPatchInfo(h.Native);
                if (patches == null || patches.Prefixes.Count(p => p.owner == Owner && p.PatchMethod == h.Prefix) != 1
                    || h.Postfix != null && patches.Postfixes.Count(p => p.owner == Owner && p.PatchMethod == h.Postfix) != 1)
                    throw new InvalidOperationException("Incomplete fresh run boundary installation.");
            }
            Installed = true;
            Plugin.Log.LogInfo("[FreshRun] Reset witness, map initialization and hydration invalidation installed.");
        } catch (Exception ex) { Installed = false; harmony.UnpatchSelf(); Plugin.Log.LogError("[FreshRun] Starting rewards/contracts unavailable: " + ex); }
    }
}

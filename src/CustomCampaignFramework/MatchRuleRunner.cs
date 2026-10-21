using System;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace EndlessMode;

/// <summary>Arms match rules after their scene controllers become available.</summary>
public sealed class MatchRuleRunner : MonoBehaviour
{
    private static bool installed;

    public MatchRuleRunner(IntPtr pointer) : base(pointer) { }

    internal static void Install()
    {
        if (installed) return;
        ClassInjector.RegisterTypeInIl2Cpp<MatchRuleRunner>();
        var runner = new GameObject("CustomCampaignMatchRules");
        UnityEngine.Object.DontDestroyOnLoad(runner);
        runner.hideFlags = HideFlags.HideAndDontSave;
        runner.AddComponent<MatchRuleRunner>();
        installed = true;
    }

    private void Update()
    {
        OffsideMatch.Tick();
        GoaliePersistenceBridge.Tick();
    }
}

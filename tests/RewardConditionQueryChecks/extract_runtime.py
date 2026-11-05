"""Build the actual query and owner traversal against counted native boundaries."""
from pathlib import Path
import sys

source = Path(__file__).resolve().parents[2] / "src" / "CustomCampaignFramework"


def section(filename, start, end):
    text = (source / filename).read_text(encoding="utf-8-sig")
    begin = text.index(start)
    return text[begin:text.index(end, begin)]


runtime = "CustomRewardRuntime.cs"
parts = [
    "using System.Reflection; using CampaignCore; using Data; using HarmonyLib; using Rogue; using Skaters; using UnityEngine; using NativeGoalie = Goaltender.Goaltender;",
    "namespace EndlessMode; internal static partial class CustomRewardRuntime {",
    section(runtime, "// Rules owned", "private static readonly HashSet<IntPtr> boundStats"),
    section(runtime, "internal static bool Has(SkaterData", "internal static int RemainingCharges"),
    section(runtime, "private static bool OwnsTalent(", "internal static void MatchStarted()"),
    section(runtime, "internal static bool StillOwned(", "private static readonly HashSet<string> brokenSticks"),
    section(runtime, "internal static IEnumerable<CustomRewardDefinition> ActiveConditions(", "internal static void Signal("),
    section(runtime, "internal static void Signal(", "internal static void Notify("),
    "}",
    section("CustomRewardAttributeTransforms.cs", "[HarmonyPatch]", "[HarmonyPatch(typeof(ForwardData)"),
]
output = Path(sys.argv[1])
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text("\n".join(parts), encoding="utf-8")

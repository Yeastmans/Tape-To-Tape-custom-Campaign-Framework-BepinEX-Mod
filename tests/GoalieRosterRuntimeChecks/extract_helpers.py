"""Compile the actual narrow appearance/legacy helper bodies, not replicas.

The full runtime files also depend on UI/renderer/selection APIs unrelated to
these tests. Only those surrounding native services are boundary doubles.
"""
from pathlib import Path
import re
import sys

source = Path(__file__).resolve().parents[2] / "src" / "CustomCampaignFramework"


def body(text, signature):
    start = text.index(signature)
    opening = text.index("{", start)
    depth = 1
    end = opening + 1
    while depth:
        if text[end] == "{":
            depth += 1
        elif text[end] == "}":
            depth -= 1
        end += 1
    return text[start:end]


appearance = (source / "CampaignAppearanceRuntime.cs").read_text(encoding="utf-8-sig")
recruitment = (source / "GoalieRecruitment.cs").read_text(encoding="utf-8-sig")
parts = ["// Generated from actual production helper bodies on every build.",
         "using System; using System.Linq; using System.Globalization; using CampaignCore; using Data; using UnityEngine; using HarmonyLib;",
         "namespace EndlessMode;", "static partial class CampaignAppearanceRuntime {"]
for signature in ("private sealed class Binding", "internal static void OwnColors(",
                  "private static ColorScheme Scheme(", "private static void Merge(",
                  "internal static CampaignDocument RecruitSnapshot(", "internal static void RestoreRecruit("):
    parts.append(body(appearance, signature))
parts += ["}", "static class GoalieRecruitment {"]
parts.append(re.search(r'internal const string IdPrefix = .*?;', recruitment).group())
parts.append(re.search(r'internal static bool IsRecruit\(.*?;', recruitment).group())
for signature in ("internal static CampaignDocument Snapshot(", "internal static void Restore("):
    parts.append(body(recruitment, signature))
if "menu" in sys.argv:
    parts.insert(1, "using System.IO; using System.Collections.Generic; using State; using Tape2Tape; using STS.Map; using Tape2Tape.Hockey.UI; using Rogue.NPC.GeneralManagerNPC;")
    for signature in ("internal sealed class Selection", "internal sealed class Visit", "internal sealed class Offer", "internal static bool BeginSelection(", "internal static void Cancel(", "internal static void Consumed(", "internal static GoalieRecruitmentVisitState VisitLedger(", "internal static void Selected(", "internal static void BeginVisit(", "internal static void Closed(EventChoiceMenu", "internal static void Closed(EventChoicePanel", "internal static bool Choices("):
        parts.append(body(recruitment, signature))
    for signature in ("internal static bool IsCurrent(", "internal static bool Valid(", "internal static bool OwnsEvent("):
        start = recruitment.index(signature); parts.append(recruitment[start:recruitment.index(";", start)+1])
    for signature in ("internal static bool MatchesPool(", "internal static bool MatchesPoolId("):
        start = recruitment.index(signature); parts.append(recruitment[start:recruitment.index(";", start)+1])
    parts.append("private static Selection selected; private static Visit visit; internal static Visit Current => visit; internal static bool IsGoalieNode => selected?.Matches(CampaignUtil.CampaignState) == true;")
    parts.append("static GoaltenderData Candidate(PlayerConfig cfg, object team, string id, GoaltenderData template) => new() { id = id };")
parts.append("}")
parts.append(body(recruitment, "internal static class GoalieRecruitLoaded"))
parts.append(body((source / "CustomRewardGrants.cs").read_text(encoding="utf-8-sig"), "internal static class CustomRewardContractRetention"))
output = Path(sys.argv[1])
output.parent.mkdir(parents=True, exist_ok=True)
# Real loader/ownership logic, with only native carrier construction removed.
runtime = (source / "CustomRewardRuntime.cs").read_text(encoding="utf-8-sig")
loader = body(runtime, "internal static void EnsureAssets()")
asset_start = loader.index("            if (Definitions.Count == 0) return;")
asset_end = loader.index("        } catch (Exception ex) { Plugin.Log.LogError(\"[CustomRewards] Loading definitions:")
loader = loader[:asset_start] + "            CreateNativeAssets();\n" + loader[asset_end:]
parts += ["static partial class CustomRewardRuntime {", re.search(r'internal static readonly Dictionary<string, CustomRewardDefinition> Definitions = .*?;', runtime).group(), loader]
for signature in ("internal static bool EnsureDefinitionsForRetention(", "internal static bool DefinitionsForRetentionCurrent(", "internal static void DefinitionSourceUnavailable()", "private static string DefinitionScope()"):
    if signature in runtime:
        parts.append(body(runtime, signature))
parts.append("}")
parts += ["static partial class CustomRewardCampaignRules {", body((source / "CustomRewardCampaignRules.cs").read_text(encoding="utf-8-sig"), "internal static IEnumerable<CustomRewardDefinition> Owned("), "}"]
lifecycle = (source / "CustomRewardLifecycle.cs").read_text(encoding="utf-8-sig")
retained_signature = re.search(r'internal static (?:void|bool) LoadRetained\(', lifecycle).group()
parts += ["static partial class CustomRewardLifecycle {", body(lifecycle, retained_signature), "}"]
output.write_text("\n".join(parts), encoding="utf-8")

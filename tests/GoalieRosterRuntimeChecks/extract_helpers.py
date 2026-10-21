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
parts.append("}")
parts.append(body(recruitment, "internal static class GoalieRecruitLoaded"))
output = Path(sys.argv[1])
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text("\n".join(parts), encoding="utf-8")

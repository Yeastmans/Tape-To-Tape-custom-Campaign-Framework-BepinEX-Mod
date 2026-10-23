"""Source-link the actual preview palette and shared overlay, not a replica."""
from pathlib import Path
import sys

source = Path(__file__).resolve().parents[2] / "src/CustomCampaignFramework"


def method(text, signature):
    start = text.index(signature)
    opening = text.index("{", start)
    arrow = text.find("=>", start, opening)
    if arrow >= 0:
        return text[start:text.index(";", arrow) + 1]
    depth, end = 1, opening + 1
    while depth:
        depth += (text[end] == "{") - (text[end] == "}")
        end += 1
    return text[start:end]


appearance = (source / "CampaignAppearanceRuntime.cs").read_text(encoding="utf-8-sig")
translation = (source / "PlayNowTranslation.cs").read_text(encoding="utf-8-sig")
parts = ["using System; using CampaignCore; using Data; using UnityEngine; using Tape2Tape.Customization.UI; using Tape2Tape.Hockey.UI; namespace EndlessMode;",
         "static partial class CampaignAppearanceRuntime {"]
for signature in ("internal static CampaignDocument Read(", "internal static bool Away(", "internal static CampaignDocument OwnPalette(",
                  "internal static TeamColorsData PreviewPalette(", "private static TeamColorsData KitPalette(",
                  "internal static TeamColorsData Unset(", "private static void Overlay("):
    parts.append(method(appearance, signature))
parts += ["}", "static partial class PlayNowTranslation {"]
for signature in ("internal static TeamColorsData GoaliePreviewColors(", "private static void ApplyPlayerColors(", "internal static void Paint("):
    parts.append(method(translation, signature))
parts.append("}")
preview = (source / "LineupPreviewKit.cs").read_text(encoding="utf-8-sig")
parts += ["static partial class LineupPreviewKit {", method(preview, "internal static void Prefix("), "}"]
Path(sys.argv[1]).write_text("\n".join(parts), encoding="utf-8")

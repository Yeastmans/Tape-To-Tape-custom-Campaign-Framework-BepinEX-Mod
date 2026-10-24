"""Compile the real disk loader and registration; only Unity creation is doubled."""
from pathlib import Path
import sys
root = Path(__file__).resolve().parents[2] / 'src/CustomCampaignFramework'
source = (root / 'CustomRewardRuntime.cs').read_text(encoding='utf-8-sig')
def body(text, signature):
    start = text.index(signature)
    opening = text.index('{', start)
    end, depth = opening + 1, 1
    while depth:
        if text[end] == '{': depth += 1
        elif text[end] == '}': depth -= 1
        end += 1
    return text[start:end]
loader = body(source, 'internal static void EnsureAssets()')
a = loader.index('            if (Definitions.Count == 0) return;')
b = loader.index('        } catch (Exception ex) { Plugin.Log.LogError("[CustomRewards] Loading definitions:')
loader = loader[:a] + '            CreateNativeAssets();\n' + loader[b:]
parts = ['using CampaignCore; using UnityEngine; using STS.Map; using System.Text; using System.Security.Cryptography; namespace EndlessMode; static partial class CustomRewardRuntime {', loader]
for signature in ('internal static void RegisterCoach(', 'private static string DefinitionScope()', 'private static void InvalidateCoachAsset('):
    parts.append(body(source, signature))
parts.append('}')
maps = (root / 'CustomMapLayout.cs').read_text(encoding='utf-8-sig')
parts.append('static partial class CustomMapLayout { private static string RuntimeFolder => Path.Combine(Plugin.ModFolder, "_map_runtime");')
for signature in ('private static string RunId(', 'private static string SaveHash(', 'internal static void SaveReceipt(',
                  'private static void RestoreReceiptCore(', 'internal static void ClearApplied(',
                  'internal static void LoadAssignments(', 'private static void CommitStadiums('):
    parts.append(body(maps, signature))
parts.append('}')
output = Path(sys.argv[1]); output.parent.mkdir(parents=True, exist_ok=True)
output.write_text('\n'.join(parts), encoding='utf-8')

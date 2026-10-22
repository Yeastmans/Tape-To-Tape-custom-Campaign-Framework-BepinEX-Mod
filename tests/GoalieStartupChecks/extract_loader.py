from pathlib import Path
import sys
source = Path(__file__).resolve().parents[2] / "src/CustomCampaignFramework/Plugin.cs"
text = source.read_text(encoding="utf-8-sig")
start = text.index("    internal static void LoadPlayerTeamsFolders(")
end = text.index("    // ===== PLAYER TEAM EDITOR =====", start)
output = Path(sys.argv[1])
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text("using CampaignCore; namespace LoaderBoundary; static partial class Plugin {\n" + text[start:end] + "\n}", encoding="utf-8")

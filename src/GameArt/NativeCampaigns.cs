namespace GameArt;

/// <summary>Exact native sources for editable campaign copies, including duplicate-name player variants.</summary>
public sealed class NativeCampaigns : IArtExtractor
{
    public string Folder => "base-campaigns";
    public string Description => "Base campaigns, native opponent bindings and player pools";
    public void Extract(UnityGame game, string output, IProgress<string>? progress)
        => new BaseRosters().ExtractCampaigns(game, output, progress);
}

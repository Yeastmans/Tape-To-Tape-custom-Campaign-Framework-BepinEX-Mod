namespace GameArt;

/// <summary>All playable mask layouts, with artwork read from the user's game.</summary>
public sealed class GoalieMaskTemplates : IArtExtractor
{
    public string Folder => "mask-templates";
    public string Description => "Goalie mask templates";
    public void Extract(UnityGame game, string output, IProgress<string>? progress)
        => HeadTemplate.ExtractCatalog(game, output, progress, Maps.Load("goalie-mask-templates.json").AsObject());
}

namespace GameArt;

// An empty native helmet field means bareheaded. Export it explicitly so the
// editor's new-player default cannot turn it into a standard starting helmet.
internal static class BaseRosterHelmet
{
    internal static string Export(string key, string value) =>
        (key is "Helmet" or "Helmet Away") && string.IsNullOrEmpty(value) ? "none" : value;
}

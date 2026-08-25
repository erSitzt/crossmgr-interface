namespace CrossMgrInterface;

/// <summary>
/// A basemap the track view can draw on.
///
/// Each one is a different service with its own terms, so the attribution string
/// travels with the tiles rather than being a global constant - the renderer draws
/// whatever the current provider says, and the tile cache partitions by host so
/// switching provider can never serve the previous one's imagery from disk.
///
/// USAGE TERMS, because these are other people's servers:
///
///   - The three OpenStreetMap-based layers are community-run and free, under
///     policies of the same shape: identify yourself with a real User-Agent, keep
///     concurrency low, and cache locally. TileFetcher enforces all three.
///   - The satellite layer is Esri's. It is reachable without a key and is widely
///     used this way, but it is a commercial service and the terms are theirs, not
///     an open licence. It is included because tracing a circuit off aerial imagery
///     is far easier than off a street map - but if this application is ever
///     distributed, check Esri's terms of use before shipping it as a default.
/// </summary>
public sealed record TileProvider(
  string Id,
  string Name,
  string UrlTemplate,
  string Attribution,
  int MaxZoom,
  string? Caveat = null)
{
  public static readonly TileProvider OpenStreetMap = new(
    "osm",
    "Map (OpenStreetMap)",
    "https://tile.openstreetmap.org/{z}/{x}/{y}.png",
    "© OpenStreetMap contributors",
    19);

  public static readonly TileProvider Satellite = new(
    "esri-imagery",
    "Satellite",
    "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}",
    "Imagery © Esri, Maxar, Earthstar Geographics",
    19,
    "Esri's service, under Esri's terms rather than an open licence.");

  public static readonly TileProvider Cycle = new(
    "cyclosm",
    "Cycling (CyclOSM)",
    "https://a.tile-cyclosm.openstreetmap.fr/cyclosm/{z}/{x}/{y}.png",
    "CyclOSM | © OpenStreetMap contributors",
    19);

  public static readonly TileProvider Topographic = new(
    "opentopomap",
    "Topographic",
    "https://a.tile.opentopomap.org/{z}/{x}/{y}.png",
    "© OpenTopoMap (CC-BY-SA) | © OpenStreetMap contributors",
    17,
    "Contour lines, but no tiles past zoom 17.");

  public static IReadOnlyList<TileProvider> All { get; } = new[]
  {
    OpenStreetMap, Satellite, Cycle, Topographic
  };

  public static TileProvider ById(string? id) =>
    All.FirstOrDefault(p => p.Id == id) ?? OpenStreetMap;

  public override string ToString() => Name;
}

using System.Globalization;
using System.Xml.Linq;

namespace CrossMgrInterface;

/// <summary>What came out of a GPX file, and what had to be done to it.</summary>
public sealed record GpxImportResult(
  TrackDefinition? Track,
  int RawPointCount,
  int KeptPointCount,
  double ToleranceMetres,
  int LapsDetected,
  IReadOnlyList<string> Warnings)
{
  public bool Success => Track is not null;

  public static GpxImportResult Failed(string reason) =>
    new(null, 0, 0, 0, 0, new[] { reason });

  /// <summary>One line for the import summary, in the idiom the rider importer already uses.</summary>
  public string Summary => Success
    ? $"{KeptPointCount} points ({RawPointCount} in the file, thinned to {ToleranceMetres:F0}m), " +
      $"{Track!.LengthMetres:F0}m round" +
      (LapsDetected > 1 ? $", trimmed from {LapsDetected} laps" : "")
    : Warnings.FirstOrDefault() ?? "The file could not be read.";
}

/// <summary>
/// Turns a GPX file into a circuit.
///
/// Three things stand between a bike computer's export and something usable as a
/// track, and all three are handled here rather than left for the operator:
///
///   1. A ride trace is very often five laps, not one. Concatenated laps drawn on
///      a map look like overlapping spaghetti and there is no obvious cause, so
///      the trace is truncated at the point it first returns to its own start.
///   2. A 5Hz trace of a 1.5km loop is around 5000 points. Douglas-Peucker thins
///      it to a couple of hundred while keeping every corner.
///   3. GPX 1.0 and 1.1 use different namespace URIs, and plenty of exporters get
///      the namespace wrong anyway - so elements are matched by local name.
/// </summary>
public static class GpxTrackImporter
{
  /// <summary>
  /// Maximum distance the thinned line may sit from the original, in metres.
  ///
  /// Four metres because a circuit's rideable line is four to eight metres wide,
  /// so the thinned polyline stays on the actual track surface and a rider dot
  /// never appears in the adjacent field. At OSM zoom 17 it is about three pixels,
  /// roughly where a corner starts to look faceted.
  /// </summary>
  public const double DefaultToleranceMetres = 4.0;

  /// <summary>Hard cap on vertices, so a pathological trace cannot produce an unbounded track.</summary>
  public const int MaxPoints = 600;

  public const int MinPoints = 4;

  /// <summary>How close a trace must come back to its own start to count as a completed lap.</summary>
  public const double LapCloseMetres = 20;

  /// <summary>Consecutive points closer than this are GPS noise from standing still.</summary>
  public const double DuplicateMetres = 1.0;

  public static GpxImportResult Import(string path)
  {
    try
    {
      return ImportXml(File.ReadAllText(path), Path.GetFileName(path));
    }
    catch (Exception ex)
    {
      return GpxImportResult.Failed($"Could not read {Path.GetFileName(path)}: {ex.Message}");
    }
  }

  /// <summary>Separated from file access so the parsing rules can be tested from string literals.</summary>
  public static GpxImportResult ImportXml(string xml, string? sourceName = null)
  {
    XDocument doc;
    try
    {
      doc = XDocument.Parse(xml);
    }
    catch (Exception ex)
    {
      return GpxImportResult.Failed($"That is not a readable GPX file: {ex.Message}");
    }

    var warnings = new List<string>();

    // Track points first. Waypoints are deliberately ignored - they are markers,
    // not the line - but a route is accepted when there is no track, which is what
    // you get when someone drew the loop in a mapping tool instead of riding it.
    var raw = ReadPoints(doc, "trkpt");
    if (raw.Count == 0)
    {
      raw = ReadPoints(doc, "rtept");
      if (raw.Count > 0) warnings.Add("No track in the file, so its route was used instead.");
    }

    if (raw.Count < MinPoints)
      return GpxImportResult.Failed(
        $"The file has {raw.Count} usable points; a circuit needs at least {MinPoints}.");

    var rawCount = raw.Count;
    var cosLat0 = GeoMath.CosLatitude(GeoMath.CentroidLatitude(raw));

    var laps = 1;
    var closeIndex = FindLapClose(raw, cosLat0, out laps);
    if (closeIndex > 0)
    {
      raw = raw.Take(closeIndex).ToList();
      if (laps > 1)
        warnings.Add($"The trace covers about {laps} laps; only the first was used.");
    }

    raw = Deduplicate(raw, cosLat0);

    // Douglas-Peucker keeps the vertices that carry curvature and drops the ones
    // sitting on straights. If it still leaves too many, back off the tolerance
    // and try again rather than truncating - truncating would cut a corner off.
    var tolerance = DefaultToleranceMetres;
    var thinned = Thin(raw, tolerance, cosLat0);

    while (thinned.Count > MaxPoints && tolerance < 200)
    {
      tolerance *= 2;
      thinned = Thin(raw, tolerance, cosLat0);
    }

    if (tolerance > DefaultToleranceMetres)
      warnings.Add($"The trace was dense, so it was thinned to {tolerance:F0}m rather than " +
                   $"{DefaultToleranceMetres:F0}m to stay under {MaxPoints} points.");

    if (thinned.Count < MinPoints)
      return GpxImportResult.Failed("The points in that file do not describe a loop.");

    var track = new TrackDefinition
    {
      Name = ReadName(doc) ?? Path.GetFileNameWithoutExtension(sourceName ?? "") ?? "Imported circuit",
      SourceGpxFile = sourceName
    };
    track.SetPoints(thinned);

    // The trace's first point is where the rider pressed "go", which is almost
    // never the start line. Defaulting silently would produce a circuit that is
    // wrong in the one way that ruins every rider position, so flag it instead.
    track.StartFinish.Fraction = 0;
    track.StartFinish.NeedsReview = true;
    warnings.Add("Drag the chequered handle onto the actual start/finish line.");

    return new GpxImportResult(track, rawCount, thinned.Count, tolerance, laps, warnings);
  }

  // ---- Parsing -------------------------------------------------------------

  private static List<LatLon> ReadPoints(XDocument doc, string localName)
  {
    var points = new List<LatLon>();

    foreach (var element in doc.Descendants().Where(e => e.Name.LocalName == localName))
    {
      var lat = Attribute(element, "lat");
      var lon = Attribute(element, "lon");

      if (lat is null || lon is null) continue;

      // Invariant culture is not optional: on a German-locale machine
      // double.Parse("50.123") reads as fifty thousand.
      if (!double.TryParse(lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var latValue) ||
          !double.TryParse(lon, NumberStyles.Float, CultureInfo.InvariantCulture, out var lonValue))
        continue;

      if (latValue is < -90 or > 90 || lonValue is < -180 or > 180) continue;

      points.Add(new LatLon(latValue, lonValue));
    }

    return points;
  }

  private static string? Attribute(XElement element, string localName) => element.Attributes()
    .FirstOrDefault(a => string.Equals(a.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase))
    ?.Value;

  private static string? ReadName(XDocument doc)
  {
    var trackName = doc.Descendants()
      .FirstOrDefault(e => e.Name.LocalName is "trk" or "rte")
      ?.Elements().FirstOrDefault(e => e.Name.LocalName == "name")?.Value;

    if (!string.IsNullOrWhiteSpace(trackName)) return trackName.Trim();

    var metaName = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "metadata")
      ?.Elements().FirstOrDefault(e => e.Name.LocalName == "name")?.Value;

    return string.IsNullOrWhiteSpace(metaName) ? null : metaName.Trim();
  }

  // ---- Shaping -------------------------------------------------------------

  /// <summary>
  /// Index at which the trace first comes back to its own start, or 0 if it never
  /// does. The search starts a quarter of the way in so the first few metres of
  /// the trace cannot match themselves.
  /// </summary>
  public static int FindLapClose(IReadOnlyList<LatLon> points, double cosLat0, out int laps)
  {
    laps = 1;
    if (points.Count < 8) return 0;

    var start = points[0];
    var bestIndex = 0;
    var bestDistance = double.MaxValue;

    for (var i = points.Count / 4; i < points.Count; i++)
    {
      var d = GeoMath.PlanarDistanceMetres(start, points[i], cosLat0);
      if (d >= bestDistance) continue;

      bestDistance = d;
      bestIndex = i;
    }

    if (bestDistance > LapCloseMetres || bestIndex <= 0) return 0;

    var lapLength = PathLength(points, 0, bestIndex, cosLat0);
    if (lapLength <= 0) return 0;

    laps = Math.Max(1, (int)Math.Round(PathLength(points, 0, points.Count - 1, cosLat0) / lapLength));
    return bestIndex;
  }

  /// <summary>Collapses consecutive points a rider could not have moved between.</summary>
  private static List<LatLon> Deduplicate(IReadOnlyList<LatLon> points, double cosLat0)
  {
    var kept = new List<LatLon>(points.Count) { points[0] };

    for (var i = 1; i < points.Count; i++)
      if (GeoMath.PlanarDistanceMetres(kept[^1], points[i], cosLat0) >= DuplicateMetres)
        kept.Add(points[i]);

    // GPX exporters disagree about repeating the first point at the end. The ring
    // is open here, so drop it and nothing downstream has to ask.
    if (kept.Count > 2 && GeoMath.PlanarDistanceMetres(kept[0], kept[^1], cosLat0) < DuplicateMetres)
      kept.RemoveAt(kept.Count - 1);

    return kept;
  }

  /// <summary>
  /// Ramer-Douglas-Peucker: drops every vertex that sits within
  /// <paramref name="toleranceMetres"/> of the line the survivors describe.
  ///
  /// Shape-preserving, which is exactly the trade a track outline wants - it keeps
  /// the apex of every corner and deletes the redundant points on straights.
  /// Uniform decimation would do the opposite.
  ///
  /// Iterative rather than recursive: a pathological 5000-point trace can drive
  /// the recursion depth to O(n).
  /// </summary>
  public static List<LatLon> Thin(IReadOnlyList<LatLon> points, double toleranceMetres, double cosLat0)
  {
    if (points.Count <= 2) return points.ToList();

    var keep = new bool[points.Count];
    keep[0] = true;
    keep[^1] = true;

    var pending = new Stack<(int First, int Last)>();
    pending.Push((0, points.Count - 1));

    while (pending.Count > 0)
    {
      var (first, last) = pending.Pop();
      if (last <= first + 1) continue;

      var worstIndex = -1;
      var worstDistance = toleranceMetres;

      for (var i = first + 1; i < last; i++)
      {
        var d = GeoMath.DistanceToSegmentMetres(points[first], points[last], points[i], cosLat0);
        if (d <= worstDistance) continue;

        worstDistance = d;
        worstIndex = i;
      }

      if (worstIndex < 0) continue;

      keep[worstIndex] = true;
      pending.Push((first, worstIndex));
      pending.Push((worstIndex, last));
    }

    var kept = new List<LatLon>();
    for (var i = 0; i < points.Count; i++)
      if (keep[i]) kept.Add(points[i]);

    return kept;
  }

  private static double PathLength(IReadOnlyList<LatLon> points, int from, int to, double cosLat0)
  {
    double total = 0;
    for (var i = from; i < to; i++)
      total += GeoMath.PlanarDistanceMetres(points[i], points[i + 1], cosLat0);
    return total;
  }
}

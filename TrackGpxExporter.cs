using System.Globalization;
using System.Xml.Linq;

namespace CrossMgrInterface;

/// <summary>
/// Writes a circuit out for other software to read.
///
/// Two formats, because they are not interchangeable:
///
///   - GPX is what every mapping tool, bike computer and course designer
///     understands, but it can only carry the SHAPE. There is no standard way to
///     say "the start/finish line is here" or "this stretch is called The
///     Climb", so those are written as waypoints - readable by a human looking at
///     the file, but not something this application will read back as structure.
///   - The circuit file is this application's own JSON. It carries everything,
///     and is what to send another CrossMgr machine.
///
/// Exporting to GPX and importing it again therefore gives you the loop back with
/// its start/finish reset - which is why the importer flags it for placing.
/// </summary>
public static class TrackGpxExporter
{
  private const string Ns = "http://www.topografix.com/GPX/1/1";

  /// <summary>Seven decimal places is about a centimetre - far finer than any circuit needs.</summary>
  private const string CoordinateFormat = "F7";

  public static string FileExtension => ".gpx";
  public static string CircuitFileExtension => ".cmtrack";

  public static string ToGpx(TrackDefinition track)
  {
    XNamespace ns = Ns;

    var gpx = new XElement(ns + "gpx",
      new XAttribute("version", "1.1"),
      new XAttribute("creator", "CrossMgrInterface"),
      new XElement(ns + "metadata",
        new XElement(ns + "name", track.Name),
        new XElement(ns + "time", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture))));

    // Waypoints first, as the GPX schema requires: wpt, then rte, then trk.
    if (track.IsUsable)
    {
      gpx.Add(Waypoint(ns, track.StartFinishLocation, "Start/Finish",
        $"Start/finish line, {track.StartFinish.Fraction * 100:F1}% round the loop"));

      for (var i = 0; i < track.Sectors.Count; i++)
      {
        var sector = track.Sectors[i];
        var name = string.IsNullOrWhiteSpace(sector.Name) ? $"Sector {i + 1}" : sector.Name;

        gpx.Add(Waypoint(ns, track.Geometry.LocationAtFraction(sector.Start.Fraction),
          name, $"Sector starts at {sector.Start.Fraction * 100:F1}% round the loop"));
      }
    }

    var segment = new XElement(ns + "trkseg");
    foreach (var point in track.Points) segment.Add(TrackPoint(ns, point));

    // Repeat the first point so other tools draw a closed loop. The importer
    // drops a trailing duplicate, so this still round-trips cleanly.
    if (track.Points.Count > 2) segment.Add(TrackPoint(ns, track.Points[0]));

    gpx.Add(new XElement(ns + "trk",
      new XElement(ns + "name", track.Name),
      new XElement(ns + "desc",
        $"{track.LengthMetres:F0}m, {track.Points.Count} points, {track.Sectors.Count} sectors"),
      segment));

    return new XDocument(new XDeclaration("1.0", "utf-8", null), gpx).ToString();
  }

  public static void SaveGpx(TrackDefinition track, string path) =>
    File.WriteAllText(path, ToGpx(track));

  public static void SaveCircuitFile(TrackDefinition track, string path) =>
    File.WriteAllText(path, TrackStore.ExportJson(track));

  private static XElement Waypoint(XNamespace ns, LatLon at, string name, string description) =>
    new(ns + "wpt",
      new XAttribute("lat", Coordinate(at.Lat)),
      new XAttribute("lon", Coordinate(at.Lon)),
      new XElement(ns + "name", name),
      new XElement(ns + "desc", description));

  private static XElement TrackPoint(XNamespace ns, LatLon at) =>
    new(ns + "trkpt",
      new XAttribute("lat", Coordinate(at.Lat)),
      new XAttribute("lon", Coordinate(at.Lon)));

  /// <summary>
  /// Invariant culture is not optional. On this machine's German locale the
  /// default would write "52,1508" - a comma, which makes the attribute
  /// unparseable by every GPX reader including our own.
  /// </summary>
  private static string Coordinate(double value) =>
    value.ToString(CoordinateFormat, CultureInfo.InvariantCulture);
}

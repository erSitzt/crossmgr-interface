using System.Text.Json.Serialization;

namespace CrossMgrInterface;

/// <summary>
/// A fixed place on the loop - the start/finish line, or the start of a sector.
///
/// It carries the position twice, and that redundancy is the point:
///
///   - <see cref="Fraction"/> is canonical for the maths. Everything downstream
///     works in fractions of the loop's arc length.
///   - <see cref="Lat"/>/<see cref="Lon"/> are canonical for identity. They are
///     where the operator actually put it on the ground.
///
/// Keeping only the fraction breaks the moment the loop is re-edited: refine one
/// hairpin with twenty extra points and the total length grows, sliding every
/// fraction backwards - the finish line quietly ends up somewhere else. Keeping
/// only a vertex index is worse; inserting a point earlier in the list renumbers
/// it with no warning at all. So after any geometry edit, <see cref="Reproject"/>
/// finds the fraction that matches the remembered ground position again.
/// </summary>
public sealed class TrackAnchor
{
  /// <summary>
  /// How far an anchor may end up from the re-edited loop before we stop trusting
  /// the match. Comfortably above the worst thinning displacement plus GPS noise,
  /// and below the narrowest realistic gap between two parallel parts of a circuit.
  /// </summary>
  public const double MaxDriftMetres = 25;

  public double Fraction { get; set; }
  public double? Lat { get; set; }
  public double? Lon { get; set; }

  /// <summary>Set when the loop moved out from under this anchor and a human should look.</summary>
  public bool NeedsReview { get; set; }

  /// <summary>
  /// Whether a human actually put this here, as opposed to it defaulting to the
  /// start of the loop.
  ///
  /// Cannot be inferred from having a ground position: the default branch in
  /// Reproject records one too, so the very next point added would otherwise
  /// clear the warning again.
  ///
  /// Nullable for circuits saved before this existed - those are treated as
  /// placed, because nagging about a circuit already in use all season is worse
  /// than the occasional missed warning on an old file.
  /// </summary>
  public bool? Placed { get; set; }

  [JsonIgnore] public bool WasPlaced => Placed ?? HasGround;

  [JsonIgnore] public bool HasGround => Lat.HasValue && Lon.HasValue;
  [JsonIgnore] public LatLon Ground => new(Lat ?? 0, Lon ?? 0);

  /// <summary>
  /// Puts the anchor where the operator clicked, snapped onto the polyline.
  ///
  /// The stored ground position is the snapped one, not the raw click: the anchor
  /// is by definition on the track, and remembering a point a few metres off it
  /// would make every later reprojection drift in the same direction.
  /// </summary>
  public void PlaceAt(TrackGeometry geometry, LatLon ground)
  {
    if (!geometry.IsUsable) return;

    Fraction = geometry.NearestFraction(ground, out _);
    var onTrack = geometry.LocationAtFraction(Fraction);
    Lat = onTrack.Lat;
    Lon = onTrack.Lon;
    Placed = true;
    NeedsReview = false;
  }

  /// <summary>Re-derives the fraction from the remembered ground position after a geometry edit.</summary>
  public void Reproject(TrackGeometry geometry)
  {
    if (!geometry.IsUsable) return;

    if (!HasGround)
    {
      // Never placed. Adopt whatever the current fraction names so later edits
      // have something to hold on to - but flag it, because this is a default
      // rather than a decision.
      //
      // For a hand-drawn loop that default is wherever the operator happened to
      // start clicking, which is almost never the painted start/finish line. The
      // GPX importer already warns about exactly this; without the flag, a drawn
      // circuit saved silently and every rider position on it was measured from
      // the wrong place.
      var here = geometry.LocationAtFraction(Fraction);
      Lat = here.Lat;
      Lon = here.Lon;
      Placed = false;
      NeedsReview = true;
      return;
    }

    var fraction = geometry.NearestFraction(Ground, out var drift);

    if (drift <= MaxDriftMetres)
    {
      Fraction = fraction;

      // Following the loop correctly, but still only ever a default until
      // somebody says otherwise.
      NeedsReview = !WasPlaced;
      return;
    }

    // Keep the fraction and flag it rather than silently teleporting the finish
    // line to the far side of the circuit because that happens to be nearest now.
    NeedsReview = true;
  }

  public TrackAnchor Clone() => new()
  {
    Fraction = Fraction, Lat = Lat, Lon = Lon, NeedsReview = NeedsReview, Placed = Placed
  };
}

/// <summary>
/// A named stretch of the circuit, defined by where it STARTS.
///
/// Sector i runs to the start of sector i+1, and the last wraps round to the
/// first. A start-and-end model would admit both gaps and overlaps, which then
/// need validating and an arbitrary decision about what colour a gap is; this
/// one cannot represent either.
/// </summary>
public sealed class TrackSector
{
  public string Name { get; set; } = "";

  /// <summary>Stored as an int because System.Text.Json has no Color converter.</summary>
  public int ColorArgb { get; set; }

  public TrackAnchor Start { get; set; } = new();

  [JsonIgnore] public Color Color => Color.FromArgb(ColorArgb);

  public TrackSector Clone() => new()
  {
    Name = Name, ColorArgb = ColorArgb, Start = Start.Clone()
  };
}

/// <summary>
/// A circuit: a closed loop of points, a start/finish line, and optional sectors.
///
/// Reusable across races - this is a venue asset, surveyed once and used all
/// season, which is why it lives in tracks.json rather than in the race database.
///
/// Every mutation of <see cref="Points"/> goes through the methods on this class.
/// They invalidate the cached geometry and reproject the anchors, and forgetting
/// either leaves the finish line pointing at stale arc length. Setting the list
/// directly from outside is the one way to break that invariant, so don't.
/// </summary>
public sealed class TrackDefinition
{
  /// <summary>Sector colours, picked to stay distinguishable over map imagery.</summary>
  private static readonly int[] SectorPalette =
  {
    unchecked((int)0xFF1F77B4), unchecked((int)0xFFD62728),
    unchecked((int)0xFF2CA02C), unchecked((int)0xFFFF7F0E),
    unchecked((int)0xFF9467BD), unchecked((int)0xFF8C564B)
  };

  private TrackGeometry? _geometry;

  public string Id { get; set; } = Guid.NewGuid().ToString("N");
  public string Name { get; set; } = "";
  public List<LatLon> Points { get; set; } = new();
  public TrackAnchor StartFinish { get; set; } = new();
  public List<TrackSector> Sectors { get; set; } = new();
  public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
  public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;
  public string? SourceGpxFile { get; set; }
  public string? Notes { get; set; }

  /// <summary>Measured form of the loop. Rebuilt lazily after any edit.</summary>
  [JsonIgnore]
  public TrackGeometry Geometry => _geometry ??= TrackGeometry.Build(Points);

  [JsonIgnore] public double LengthMetres => Geometry.TotalLengthMetres;
  [JsonIgnore] public bool IsUsable => Geometry.IsUsable;
  [JsonIgnore] public LatLon StartFinishLocation => Geometry.LocationAtFraction(StartFinish.Fraction);
  [JsonIgnore] public GeoBounds Bounds => GeoBounds.FromPoints(Points);

  // ---- Point editing -------------------------------------------------------

  public void AddPoint(LatLon p)
  {
    Points.Add(p);
    GeometryChanged();
  }

  public void InsertPoint(int index, LatLon p)
  {
    Points.Insert(Math.Clamp(index, 0, Points.Count), p);
    GeometryChanged();
  }

  public void MovePoint(int index, LatLon p)
  {
    if (index < 0 || index >= Points.Count) return;
    Points[index] = p;
    GeometryChanged();
  }

  /// <summary>
  /// Takes back the last point placed.
  ///
  /// Unlike RemovePointAt this has no three-point floor, because it is what
  /// Backspace does while the loop is still being drawn - and at that stage
  /// having fewer than three points is a normal state to pass through, not an
  /// invalid circuit to refuse.
  /// </summary>
  public bool RemoveLastPoint()
  {
    if (Points.Count == 0) return false;

    Points.RemoveAt(Points.Count - 1);
    GeometryChanged();
    return true;
  }

  /// <summary>Removes a vertex. Refuses to go below three - that is not a loop any more.</summary>
  public bool RemovePointAt(int index)
  {
    if (index < 0 || index >= Points.Count || Points.Count <= 3) return false;

    Points.RemoveAt(index);
    GeometryChanged();
    return true;
  }

  public void SetPoints(IEnumerable<LatLon> points)
  {
    Points = points.ToList();
    GeometryChanged();
  }

  /// <summary>
  /// Flips which way round riders go. A GPX ridden anticlockwise on a clockwise
  /// circuit is a coin flip, and getting it wrong sends every dot backwards.
  /// The anchors survive because they reproject by ground position.
  /// </summary>
  public void ReverseDirection()
  {
    Points.Reverse();
    GeometryChanged();
  }

  // ---- Sectors -------------------------------------------------------------

  public TrackSector AddSector(string name, LatLon ground)
  {
    var sector = new TrackSector
    {
      Name = name,
      ColorArgb = SectorPalette[Sectors.Count % SectorPalette.Length]
    };
    sector.Start.PlaceAt(Geometry, ground);

    Sectors.Add(sector);
    SortSectors();
    return sector;
  }

  /// <summary>
  /// Removes a boundary, which merges that sector into the one before it. Under a
  /// start-only model that is the only thing deletion can consistently mean.
  /// </summary>
  public bool RemoveSectorAt(int index)
  {
    if (index < 0 || index >= Sectors.Count) return false;
    Sectors.RemoveAt(index);
    return true;
  }

  public void SortSectors() => Sectors.Sort((a, b) => a.Start.Fraction.CompareTo(b.Start.Fraction));

  public int SectorIndexAt(double fraction) => TrackGeometry.SectorIndexAt(fraction, Sectors);

  public string SectorNameAt(double fraction)
  {
    var i = SectorIndexAt(fraction);
    if (i < 0) return "";
    var name = Sectors[i].Name;
    return string.IsNullOrWhiteSpace(name) ? $"Sector {i + 1}" : name;
  }

  // ---- Housekeeping --------------------------------------------------------

  public void InvalidateGeometry() => _geometry = null;

  /// <summary>Drops the cached geometry, then pulls every anchor back onto the new loop.</summary>
  public void GeometryChanged()
  {
    _geometry = null;
    ModifiedUtc = DateTime.UtcNow;

    if (!Geometry.IsUsable) return;

    StartFinish.Reproject(Geometry);
    foreach (var sector in Sectors) sector.Start.Reproject(Geometry);
    SortSectors();
  }

  public TrackDefinition Clone() => new()
  {
    Id = Id,
    Name = Name,
    Points = Points.ToList(),
    StartFinish = StartFinish.Clone(),
    Sectors = Sectors.Select(s => s.Clone()).ToList(),
    CreatedUtc = CreatedUtc,
    ModifiedUtc = ModifiedUtc,
    SourceGpxFile = SourceGpxFile,
    Notes = Notes
  };

  /// <summary>
  /// Problems a human should fix, in plain words. Self-intersection is NOT one of
  /// them - a figure-of-eight is a perfectly legitimate layout.
  /// </summary>
  public IReadOnlyList<string> Validate()
  {
    var problems = new List<string>();

    if (string.IsNullOrWhiteSpace(Name))
      problems.Add("The circuit has no name.");

    if (Points.Count < 3)
      problems.Add($"A circuit needs at least three points; this one has {Points.Count}.");
    else if (LengthMetres < 50)
      problems.Add($"The loop is only {LengthMetres:F0}m round, which is too short to be a circuit.");

    if (StartFinish.NeedsReview)
      problems.Add("The start/finish line no longer sits on the loop. Drag it back onto the track.");

    foreach (var sector in Sectors.Where(s => s.Start.NeedsReview))
      problems.Add($"Sector \"{sector.Name}\" no longer sits on the loop.");

    return problems;
  }
}

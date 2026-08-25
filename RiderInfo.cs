namespace CrossMgrInterface;

/// <summary>
/// Class to track comprehensive rider information including laps, times, and race status
/// </summary>
public class RiderInfo
{
  public string TagID { get; set; } = "";
  public string RiderNumber { get; set; } = "";
  public string FirstName { get; set; } = "";
  public string LastName { get; set; } = "";
  public string Team { get; set; } = "";
  public string Category { get; set; } = "";
  public string Machine { get; set; } = "";
  public DateTime LastCrossingTime { get; set; }
  public List<RiderLap> Laps { get; set; } = new List<RiderLap>();
  public DateTime FirstCrossing { get; set; }
  public DateTime LastCrossing { get; set; }
  public DateTime? RaceStartTime { get; set; } // Store race start time for this rider
  public int FinalAllowedLap { get; set; } = int.MaxValue; // Maximum lap number allowed for this rider after race finish
  public bool IsDNF { get; set; } = false; // Did Not Finish - marked when rider times out after race ends
  public DateTime? DNFTime { get; set; } // When the rider was marked as DNF

  /// <summary>Did Not Start. Only ever set by an operator.</summary>
  public bool IsDNS { get; set; } = false;

  /// <summary>
  /// True once an operator has set this rider's status by hand. The automatic
  /// DNF timeout must not overwrite a decision a human already made.
  /// </summary>
  public bool StatusSetByOperator { get; set; } = false;

  /// <summary>Why the status was set, for the results sheet and any protest.</summary>
  public string? StatusReason { get; set; }

  /// <summary>
  /// Bumped on every lap mutation. A correction dialog captures this when it
  /// opens and passes it back on apply, so a crossing that lands while the
  /// operator is deciding is detected rather than silently overwritten.
  /// </summary>
  public int Revision { get; set; }

  /// <summary>Status as shown to a person: "DNF", "DNS", or empty while racing.</summary>
  public string StatusText => IsDNS ? "DNS" : IsDNF ? "DNF" : "";

  /// <summary>True when any lap carries an operator correction or a pending warning.</summary>
  public bool HasAnomalies =>
    Laps.Any(l => l.WasCorrected || (l.IsSuggestedForSplit && !l.SuggestionDismissed));

  /// <summary>
  /// Display name combining first and last name, or just tag ID if no name available
  /// </summary>
  public string DisplayName
  {
    get
    {
      if (!string.IsNullOrEmpty(FirstName) || !string.IsNullOrEmpty(LastName))
      {
        return $"{FirstName} {LastName}".Trim();
      }
      return TagID;
    }
  }

  /// <summary>
  /// How this rider should be named anywhere a human will read it:
  /// "#127 John Smith", falling back through number-only or name-only to the
  /// bare transponder when nothing else is known.
  ///
  /// Lives here rather than on Form1 so the renderers, the report generator and
  /// the lap progression grid can all reach it - they previously either
  /// duplicated the logic or printed the raw tag.
  /// </summary>
  public string Label
  {
    get
    {
      var hasNumber = !string.IsNullOrEmpty(RiderNumber);
      var name = $"{FirstName} {LastName}".Trim();

      if (hasNumber && name.Length > 0) return $"#{RiderNumber} {name}";
      if (hasNumber) return $"#{RiderNumber}";
      if (name.Length > 0) return name;
      return TagID;
    }
  }

  public int TotalLaps => Laps.Count;
  /// <summary>
  /// Quickest completed lap, ignoring the first.
  ///
  /// The first lap is not a lap: it runs from the start of the race to this
  /// rider's first crossing. When the race starts on the first transponder read
  /// that is 0.000s for whoever triggered it, and a couple of seconds for the
  /// riders immediately behind - which would otherwise be published as their
  /// best lap on the results sheet. The rest of the application already excludes
  /// the first lap from pace calculations; this now matches.
  /// </summary>
  public TimeSpan? BestLapTime =>
    Laps.Skip(1).Where(l => l.LapTime.HasValue).Min(l => l.LapTime);

  /// <summary>Mean of the completed laps, ignoring the first for the same reason.</summary>
  public TimeSpan? AverageLapTime
  {
    get
    {
      var timed = Laps.Skip(1).Where(l => l.LapTime.HasValue).ToList();
      return timed.Count == 0
        ? null
        : TimeSpan.FromMilliseconds(timed.Average(l => l.LapTime!.Value.TotalMilliseconds));
    }
  }
  public TimeSpan? LastLapTime => Laps.LastOrDefault()?.LapTime;

  /// <summary>
  /// Total time should be from race start (if available) to last crossing
  /// </summary>
  public TimeSpan TotalTime
  {
    get
    {
      // Use race start time if available, otherwise fall back to first crossing
      var startTime = RaceStartTime ?? FirstCrossing;
      return LastCrossing - startTime;
    }
  }

  /// <summary>
  /// Predicted next lap time, weighting the three most recent timed laps 1:2:3.
  /// Walks the list backwards rather than building an intermediate collection -
  /// this is read several times per rider per grid refresh.
  /// </summary>
  public TimeSpan? PredictedLapTime
  {
    get
    {
      double weightedSum = 0;
      double totalWeight = 0;
      var found = 0;

      // Collect the last three timed laps, oldest of those three first, so the
      // weights stay 1, 2, 3 in the original order.
      Span<double> recent = stackalloc double[3];
      for (var i = Laps.Count - 1; i >= 0 && found < 3; i--)
      {
        if (Laps[i].LapTime.HasValue)
          recent[found++] = Laps[i].LapTime!.Value.TotalMilliseconds;
      }

      if (found == 0) return null;

      for (var i = 0; i < found; i++)
      {
        // recent[0] is the newest, so it carries the highest weight.
        double weight = found - i;
        weightedSum += recent[i] * weight;
        totalWeight += weight;
      }

      return TimeSpan.FromMilliseconds(weightedSum / totalWeight);
    }
  }

  /// <summary>
  /// Pace of a rider already circulating: the same 3:2:1 weighting as
  /// <see cref="PredictedLapTime"/>, but over the last three timed laps EXCLUDING
  /// the first, for the reason spelled out on <see cref="BestLapTime"/>. Null
  /// until a second lap has been timed.
  ///
  /// The track map dead-reckons from this and not from PredictedLapTime, and the
  /// difference is not cosmetic. When the race starts on the first transponder
  /// read, lap 1 is 0.000s for whoever triggered it, so dividing elapsed time by
  /// it sends the leader's dot - the most watched thing on that screen - orbiting
  /// the circuit. Even three laps in, including lap 1 still leaves the estimate
  /// around 17% fast.
  ///
  /// PredictedLapTime deliberately keeps its own behaviour: it is persisted to the
  /// race database and drives the riders grid's countdown columns. The two are
  /// pinned apart by test, so do not "unify" them.
  /// </summary>
  public TimeSpan? RacingPace
  {
    get
    {
      // Walks backwards into a fixed buffer for the same reason PredictedLapTime
      // does: with the whole field on the map this is read once per rider per frame.
      Span<double> recent = stackalloc double[3];
      var found = 0;

      for (var i = Laps.Count - 1; i >= 1 && found < 3; i--)
      {
        if (Laps[i].LapTime.HasValue)
          recent[found++] = Laps[i].LapTime!.Value.TotalMilliseconds;
      }

      if (found == 0) return null;

      double weightedSum = 0;
      double totalWeight = 0;

      for (var i = 0; i < found; i++)
      {
        // recent[0] is the newest, so it carries the highest weight.
        double weight = found - i;
        weightedSum += recent[i] * weight;
        totalWeight += weight;
      }

      return TimeSpan.FromMilliseconds(weightedSum / totalWeight);
    }
  }

  /// <summary>
  /// Estimated time for next finish line crossing based on predicted lap time
  /// </summary>
  public DateTime? EstimatedNextCrossing
  {
    get
    {
      var predicted = PredictedLapTime;
      return predicted.HasValue ? LastCrossing + predicted.Value : null;
    }
  }
}

namespace CrossMgrInterface;

/// <summary>Outcome of a correction attempt.</summary>
public sealed class CorrectionResult
{
  public bool Ok { get; private init; }
  public string? Error { get; private init; }
  public CorrectionCommand? Command { get; private init; }

  public static CorrectionResult Success(CorrectionCommand command) =>
    new() { Ok = true, Command = command };

  public static CorrectionResult Failure(string error) =>
    new() { Ok = false, Error = error };

  /// <summary>Nothing needed doing; not an error, but nothing to undo either.</summary>
  public static CorrectionResult NoOp() => new() { Ok = true };
}

/// <summary>How an unidentified transponder should be resolved.</summary>
public enum AssignTagMode
{
  /// <summary>Give this transponder a name and number. Laps stay where they are.</summary>
  AttachIdentity,
  /// <summary>Move its laps onto a rider already tracked under another transponder.</summary>
  MergeIntoRider
}

/// <summary>What the operator chose in the assign-transponder dialog.</summary>
public sealed class AssignTagRequest
{
  public AssignTagMode Mode { get; init; }

  /// <summary>Target for <see cref="AssignTagMode.MergeIntoRider"/>.</summary>
  public string? MergeTargetTag { get; init; }

  public string RiderNumber { get; init; } = "";
  public string FirstName { get; init; } = "";
  public string LastName { get; init; } = "";
  public string Team { get; init; } = "";
  public string Category { get; init; } = "";
  public string Machine { get; init; } = "";

  /// <summary>Drop crossings that land impossibly close to one already recorded.</summary>
  public bool DropDuplicateCrossings { get; init; } = true;

  /// <summary>Route later reads of the stray transponder to the same rider.</summary>
  public bool RegisterAlias { get; init; } = true;
}

/// <summary>Which status an operator is applying by hand.</summary>
public enum RiderStatus { Racing, DNF, DNS }

/// <summary>
/// The single place laps are mutated by an operator.
///
/// Every operation follows the same shape: snapshot the affected riders, change
/// them, run <see cref="RecomputeRider"/>, snapshot again, and record the pair so
/// the change can be undone exactly. Nothing else in the application is allowed
/// to renumber laps.
///
/// Form1 keeps ownership of the riders dictionary and its lock and passes them
/// in; this class does not own race state.
/// </summary>
public sealed class RaceCorrectionService
{
  private readonly Dictionary<string, RiderInfo> _riders;
  private readonly object _ridersLock;
  private readonly Func<DateTime?> _getRaceStartTime;
  private readonly Action<string> _log;

  public CorrectionHistory History { get; } = new();

  /// <summary>Raised after any change, with the transponders that were touched.</summary>
  public event Action<IReadOnlyList<string>>? CorrectionApplied;

  public RaceCorrectionService(
    Dictionary<string, RiderInfo> riders,
    object ridersLock,
    Func<DateTime?> getRaceStartTime,
    Action<string> log)
  {
    _riders = riders;
    _ridersLock = ridersLock;
    _getRaceStartTime = getRaceStartTime;
    _log = log;
  }

  // ---- The canonical recompute ---------------------------------------------

  /// <summary>
  /// Puts a rider's laps back into a consistent state: drops tombstoned laps,
  /// orders by crossing time, renumbers sequentially, recomputes every lap time
  /// and refreshes the rider's first/last crossing.
  ///
  /// Lap numbers must never be assigned anywhere else. Inserting or deleting a
  /// lap renumbers everything after it, and having two places that do the
  /// renumbering is how the numbering and the database drift apart.
  /// </summary>
  public static void RecomputeRider(RiderInfo rider, DateTime? raceStartTime)
  {
    rider.Laps = rider.Laps
      .Where(l => !l.IsDeleted)
      .OrderBy(l => l.CrossingTime)
      .ToList();

    for (var i = 0; i < rider.Laps.Count; i++)
    {
      var lap = rider.Laps[i];
      lap.LapNumber = i + 1;

      // The first lap is measured from the start of the race, not from a
      // previous crossing - matching how a live first lap is recorded.
      lap.LapTime = i == 0
        ? (raceStartTime.HasValue ? lap.CrossingTime - raceStartTime.Value : null)
        : lap.CrossingTime - rider.Laps[i - 1].CrossingTime;
    }

    if (rider.Laps.Count > 0)
    {
      rider.FirstCrossing = rider.Laps[0].CrossingTime;
      rider.LastCrossing = rider.Laps[^1].CrossingTime;
      rider.LastCrossingTime = rider.Laps[^1].CrossingTime;
    }

    rider.Revision++;
  }

  // ---- Operations ----------------------------------------------------------

  /// <summary>Inserts a lap that was never read, at the given crossing time.</summary>
  public CorrectionResult AddLap(string tagId, DateTime crossingTime, int expectedRevision, string? note = null)
    => Mutate(tagId, expectedRevision, CorrectionKind.AddLap, rider =>
    {
      rider.Laps.Add(new RiderLap
      {
        TagID = tagId,
        CrossingTime = crossingTime,
        Source = LapSource.ManualInsert,
        CorrectionNote = note
      });

      return $"Added a lap for {rider.Label} at {crossingTime:HH:mm:ss.fff}";
    });

  /// <summary>Corrects the crossing time of an existing lap.</summary>
  public CorrectionResult EditLapTime(string tagId, int lapNumber, DateTime newCrossingTime, int expectedRevision)
    => Mutate(tagId, expectedRevision, CorrectionKind.EditLapTime, rider =>
    {
      var lap = rider.Laps.FirstOrDefault(l => l.LapNumber == lapNumber && !l.IsDeleted);
      if (lap == null) throw new CorrectionException($"Lap {lapNumber} no longer exists.");

      // Keep the original so the results sheet can show the lap was adjusted.
      lap.OriginalCrossingTime ??= lap.CrossingTime;
      lap.CrossingTime = newCrossingTime;

      return $"Changed lap {lapNumber} of {rider.Label} to {newCrossingTime:HH:mm:ss.fff}";
    });

  /// <summary>Tombstones a lap. The rows stay so the change can be undone.</summary>
  public CorrectionResult DeleteLap(string tagId, int lapNumber, int expectedRevision)
    => Mutate(tagId, expectedRevision, CorrectionKind.DeleteLap, rider =>
    {
      var lap = rider.Laps.FirstOrDefault(l => l.LapNumber == lapNumber && !l.IsDeleted);
      if (lap == null) throw new CorrectionException($"Lap {lapNumber} no longer exists.");

      lap.IsDeleted = true;
      return $"Deleted lap {lapNumber} of {rider.Label}";
    });

  /// <summary>
  /// Replaces one long lap with <paramref name="intoCount"/> equal ones, for a
  /// rider whose transponder was missed on one or more passes.
  /// </summary>
  public CorrectionResult SplitLap(string tagId, int lapNumber, int intoCount, int expectedRevision)
  {
    if (intoCount < 2)
      return CorrectionResult.Failure("A lap must be split into at least two.");

    return Mutate(tagId, expectedRevision, CorrectionKind.SplitLap, rider =>
    {
      var lap = rider.Laps.FirstOrDefault(l => l.LapNumber == lapNumber && !l.IsDeleted);
      if (lap == null) throw new CorrectionException($"Lap {lapNumber} no longer exists.");
      if (!lap.LapTime.HasValue) throw new CorrectionException("That lap has no recorded duration to divide up.");

      var start = lap.CrossingTime - lap.LapTime.Value;
      var segment = TimeSpan.FromMilliseconds(lap.LapTime.Value.TotalMilliseconds / intoCount);

      lap.IsDeleted = true;

      for (var i = 1; i <= intoCount; i++)
      {
        rider.Laps.Add(new RiderLap
        {
          TagID = tagId,
          // The last segment lands exactly on the original crossing, which is a
          // real read; the intermediate ones are interpolated.
          CrossingTime = i == intoCount ? lap.CrossingTime : start + segment * i,
          IsSplitLap = true,
          Source = LapSource.Split
        });
      }

      return $"Split lap {lapNumber} of {rider.Label} into {intoCount}";
    });
  }

  /// <summary>Marks a flagged lap as genuinely long so it stops being re-flagged.</summary>
  public CorrectionResult DismissSplitSuggestion(string tagId, int lapNumber)
    => Mutate(tagId, expectedRevision: -1, CorrectionKind.DismissSuggestion, rider =>
    {
      var lap = rider.Laps.FirstOrDefault(l => l.LapNumber == lapNumber && !l.IsDeleted);
      if (lap == null) throw new CorrectionException($"Lap {lapNumber} no longer exists.");

      lap.IsSuggestedForSplit = false;
      lap.SuggestedSplitCount = 0;
      lap.SuggestedSplitLapTime = null;
      lap.SuggestionDismissed = true;

      return $"Kept lap {lapNumber} of {rider.Label} as recorded";
    });

  /// <summary>Reinstates a read that was rejected for being too soon after the last one.</summary>
  public CorrectionResult RestoreRejectedRead(string tagId, DateTime crossingTime)
    => Mutate(tagId, expectedRevision: -1, CorrectionKind.RestoreRejectedRead, rider =>
    {
      rider.Laps.Add(new RiderLap
      {
        TagID = tagId,
        CrossingTime = crossingTime,
        Source = LapSource.RestoredShortRead
      });

      return $"Restored the {crossingTime:HH:mm:ss.fff} read for {rider.Label}";
    });

  /// <summary>
  /// Sets a rider's status by hand. Marking the last straggler DNF is how an
  /// operator closes out a race that would otherwise sit waiting for the timeout.
  /// </summary>
  public CorrectionResult SetRiderStatus(string tagId, RiderStatus status, string? reason = null)
    => Mutate(tagId, expectedRevision: -1, CorrectionKind.SetStatus, rider =>
    {
      rider.IsDNF = status == RiderStatus.DNF;
      rider.IsDNS = status == RiderStatus.DNS;
      rider.DNFTime = status == RiderStatus.DNF ? DateTime.Now : null;

      // Racing clears the flag so the automatic timeout can take over again.
      rider.StatusSetByOperator = status != RiderStatus.Racing;
      rider.StatusReason = status == RiderStatus.Racing ? null : reason;

      return status switch
      {
        RiderStatus.DNF => $"Marked {rider.Label} as DNF",
        RiderStatus.DNS => $"Marked {rider.Label} as DNS",
        _ => $"Put {rider.Label} back in the race"
      };
    });

  /// <summary>
  /// Attaches an identity to a transponder that was not in the imported rider
  /// list, optionally merging its laps onto a rider already being tracked under
  /// a different transponder.
  /// </summary>
  public CorrectionResult AssignTag(string sourceTag, AssignTagRequest request, TimeSpan minimumLapTime)
  {
    CorrectionCommand command;

    lock (_ridersLock)
    {
      if (!_riders.TryGetValue(sourceTag, out var source))
        return CorrectionResult.Failure("That transponder is no longer in the race.");

      var before = new List<RiderSnapshot> { RiderSnapshot.Capture(source, sourceTag) };
      var after = new List<RiderSnapshot>();
      var aliases = new Dictionary<string, string>();
      string description;

      if (request.Mode == AssignTagMode.MergeIntoRider)
      {
        if (string.IsNullOrEmpty(request.MergeTargetTag))
          return CorrectionResult.Failure("No rider was chosen to merge into.");

        if (request.MergeTargetTag == sourceTag)
          return CorrectionResult.Failure("That is the same transponder.");

        if (!_riders.TryGetValue(request.MergeTargetTag, out var target))
          return CorrectionResult.Failure("That rider is no longer in the race.");

        before.Add(RiderSnapshot.Capture(target, request.MergeTargetTag));

        var brought = 0;
        var dropped = 0;

        foreach (var lap in source.Laps.Where(l => !l.IsDeleted).OrderBy(l => l.CrossingTime))
        {
          // Two crossings closer together than a lap can physically be are the
          // same pass seen twice. Merging without this check produces three
          // second laps that poison the pace, the standings and the detector.
          var clashes = target.Laps.Any(existing =>
            !existing.IsDeleted &&
            (existing.CrossingTime - lap.CrossingTime).Duration() < minimumLapTime);

          if (clashes && request.DropDuplicateCrossings)
          {
            dropped++;
            continue;
          }

          var copy = lap.Clone();
          copy.TagID = target.TagID;
          copy.Source = LapSource.Merged;
          target.Laps.Add(copy);
          brought++;
        }

        // Re-keying means remove and re-add: a dictionary key cannot be mutated.
        _riders.Remove(sourceTag);

        RecomputeRider(target, _getRaceStartTime());
        after.Add(RiderSnapshot.Capture(target, target.TagID));
        after.Add(new RiderSnapshot { TagID = sourceTag, Existed = false });

        if (request.RegisterAlias)
          aliases[sourceTag] = target.TagID;

        description = dropped > 0
          ? $"Merged {brought} lap(s) onto {target.Label} ({dropped} duplicate read(s) dropped)"
          : $"Merged {brought} lap(s) onto {target.Label}";
      }
      else
      {
        // Attaching an identity: nothing moves, so there is nothing to lose.
        source.RiderNumber = request.RiderNumber;
        source.FirstName = request.FirstName;
        source.LastName = request.LastName;
        source.Team = request.Team;
        source.Category = request.Category;
        source.Machine = request.Machine;
        source.Revision++;

        after.Add(RiderSnapshot.Capture(source, sourceTag));
        description = $"Identified transponder {sourceTag} as {source.Label}";
      }

      command = new CorrectionCommand
      {
        Kind = CorrectionKind.AssignTag,
        Description = description,
        Before = before,
        After = after,
        AliasesAdded = aliases
      };
    }

    History.Record(command);
    _log($"✏️ {command.Description}");
    NotifyApplied(command);
    return CorrectionResult.Success(command);
  }

  // ---- Undo / redo ---------------------------------------------------------

  public CorrectionResult Undo()
  {
    var command = History.PopUndo();
    if (command == null) return CorrectionResult.Failure("There is nothing to undo.");

    ApplySnapshots(command.Before);
    _log($"↩️ Undone: {command.Description}");
    NotifyApplied(command);
    return CorrectionResult.Success(command);
  }

  public CorrectionResult Redo()
  {
    var command = History.PopRedo();
    if (command == null) return CorrectionResult.Failure("There is nothing to redo.");

    ApplySnapshots(command.After);
    _log($"↪️ Redone: {command.Description}");
    NotifyApplied(command);
    return CorrectionResult.Success(command);
  }

  private void ApplySnapshots(IEnumerable<RiderSnapshot> snapshots)
  {
    lock (_ridersLock)
    {
      foreach (var snapshot in snapshots)
        snapshot.RestoreInto(_riders);
    }
  }

  // ---- Shared machinery ----------------------------------------------------

  /// <summary>
  /// Runs <paramref name="change"/> against one rider inside the lock, bracketed
  /// by snapshots, and records the result as an undoable command.
  /// </summary>
  private CorrectionResult Mutate(
    string tagId, int expectedRevision, CorrectionKind kind, Func<RiderInfo, string> change)
  {
    CorrectionCommand command;

    lock (_ridersLock)
    {
      if (!_riders.TryGetValue(tagId, out var rider))
        return CorrectionResult.Failure("That rider is no longer in the race.");

      // A crossing may have landed while the operator was deciding. Better to
      // ask them to look again than to apply a correction to stale data.
      if (expectedRevision >= 0 && rider.Revision != expectedRevision)
      {
        return CorrectionResult.Failure(
          $"{rider.Label} crossed the line while this window was open. " +
          "The lap list has been refreshed - please check it and try again.");
      }

      var before = RiderSnapshot.Capture(rider, tagId);

      string description;
      try
      {
        description = change(rider);
      }
      catch (CorrectionException ex)
      {
        // Put the rider back exactly as it was; a partial change is worse than none.
        before.RestoreInto(_riders);
        return CorrectionResult.Failure(ex.Message);
      }

      RecomputeRider(rider, _getRaceStartTime());

      command = new CorrectionCommand
      {
        Kind = kind,
        Description = description,
        Before = { before },
        After = { RiderSnapshot.Capture(rider, tagId) }
      };
    }

    History.Record(command);
    _log($"✏️ {command.Description}");
    NotifyApplied(command);
    return CorrectionResult.Success(command);
  }

  private void NotifyApplied(CorrectionCommand command)
    => CorrectionApplied?.Invoke(command.AffectedTags.ToList());
}

/// <summary>Thrown inside a correction when the change cannot be completed.</summary>
public sealed class CorrectionException : Exception
{
  public CorrectionException(string message) : base(message) { }
}

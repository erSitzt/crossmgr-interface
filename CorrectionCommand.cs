namespace CrossMgrInterface;

/// <summary>What kind of correction was applied. Used for the audit trail.</summary>
public enum CorrectionKind
{
  AddLap,
  EditLapTime,
  DeleteLap,
  SplitLap,
  DismissSuggestion,
  RestoreRejectedRead,
  SetStatus,
  AssignTag
}

/// <summary>
/// A complete copy of one rider at a point in time.
///
/// Undo works by restoring snapshots rather than by applying inverse operations,
/// because lap identity is (tag, lap number) and every insert, delete or split
/// renumbers everything after it - an inverse operation would have to reason
/// about that renumbering and would be wrong in the corner cases. A snapshot is
/// about 4 KB and is always exact.
/// </summary>
public sealed class RiderSnapshot
{
  public string TagID { get; init; } = "";

  /// <summary>False when the rider did not exist at all, so undo removes them again.</summary>
  public bool Existed { get; init; }

  public string RiderNumber { get; init; } = "";
  public string FirstName { get; init; } = "";
  public string LastName { get; init; } = "";
  public string Team { get; init; } = "";
  public string Category { get; init; } = "";
  public string Machine { get; init; } = "";
  public DateTime FirstCrossing { get; init; }
  public DateTime LastCrossing { get; init; }
  public DateTime LastCrossingTime { get; init; }
  public DateTime? RaceStartTime { get; init; }
  public DateTime? DNFTime { get; init; }
  public int FinalAllowedLap { get; init; }
  public bool IsDNF { get; init; }
  public bool IsDNS { get; init; }
  public bool StatusSetByOperator { get; init; }
  public string? StatusReason { get; init; }
  public int Revision { get; init; }
  public List<RiderLap> Laps { get; init; } = new();

  public static RiderSnapshot Capture(RiderInfo? rider, string tagId)
  {
    if (rider == null)
      return new RiderSnapshot { TagID = tagId, Existed = false };

    return new RiderSnapshot
    {
      TagID = rider.TagID,
      Existed = true,
      RiderNumber = rider.RiderNumber,
      FirstName = rider.FirstName,
      LastName = rider.LastName,
      Team = rider.Team,
      Category = rider.Category,
      Machine = rider.Machine,
      FirstCrossing = rider.FirstCrossing,
      LastCrossing = rider.LastCrossing,
      LastCrossingTime = rider.LastCrossingTime,
      RaceStartTime = rider.RaceStartTime,
      DNFTime = rider.DNFTime,
      FinalAllowedLap = rider.FinalAllowedLap,
      IsDNF = rider.IsDNF,
      IsDNS = rider.IsDNS,
      StatusSetByOperator = rider.StatusSetByOperator,
      StatusReason = rider.StatusReason,
      Revision = rider.Revision,
      Laps = rider.Laps.Select(l => l.Clone()).ToList()
    };
  }

  /// <summary>Writes this snapshot back over whatever is in the field now.</summary>
  public void RestoreInto(Dictionary<string, RiderInfo> riders)
  {
    if (!Existed)
    {
      riders.Remove(TagID);
      return;
    }

    if (!riders.TryGetValue(TagID, out var rider))
    {
      rider = new RiderInfo { TagID = TagID };
      riders[TagID] = rider;
    }

    rider.RiderNumber = RiderNumber;
    rider.FirstName = FirstName;
    rider.LastName = LastName;
    rider.Team = Team;
    rider.Category = Category;
    rider.Machine = Machine;
    rider.FirstCrossing = FirstCrossing;
    rider.LastCrossing = LastCrossing;
    rider.LastCrossingTime = LastCrossingTime;
    rider.RaceStartTime = RaceStartTime;
    rider.DNFTime = DNFTime;
    rider.FinalAllowedLap = FinalAllowedLap;
    rider.IsDNF = IsDNF;
    rider.IsDNS = IsDNS;
    rider.StatusSetByOperator = StatusSetByOperator;
    rider.StatusReason = StatusReason;
    rider.Revision = Revision;
    rider.Laps = Laps.Select(l => l.Clone()).ToList();
  }
}

/// <summary>One undoable correction, with the affected riders before and after.</summary>
public sealed class CorrectionCommand
{
  public Guid Id { get; } = Guid.NewGuid();
  public DateTime AppliedAt { get; init; } = DateTime.Now;
  public CorrectionKind Kind { get; init; }

  /// <summary>Human-readable, e.g. "Deleted lap 7 of #12 Max Mustermann".</summary>
  public string Description { get; init; } = "";

  public List<RiderSnapshot> Before { get; init; } = new();
  public List<RiderSnapshot> After { get; init; } = new();

  /// <summary>Transponders that were added to the ignore list by this command.</summary>
  public List<string> IgnoredTagsAdded { get; init; } = new();

  /// <summary>Alias entries this command created (stray tag -> canonical tag).</summary>
  public Dictionary<string, string> AliasesAdded { get; init; } = new();

  public IEnumerable<string> AffectedTags =>
    Before.Select(b => b.TagID).Concat(After.Select(a => a.TagID)).Distinct();
}

/// <summary>
/// Bounded undo/redo stack for corrections.
///
/// Depth is capped because each entry holds full rider snapshots; at roughly
/// 4 KB per command, fifty commands is about 200 KB, and a race rarely needs
/// more than a handful of corrections.
/// </summary>
public sealed class CorrectionHistory
{
  public const int MaxDepth = 50;

  private readonly List<CorrectionCommand> _undo = new();
  private readonly List<CorrectionCommand> _redo = new();

  public bool CanUndo => _undo.Count > 0;
  public bool CanRedo => _redo.Count > 0;

  /// <summary>Description of the command undo would reverse, for the menu label.</summary>
  public string? NextUndoDescription => CanUndo ? _undo[^1].Description : null;

  public string? NextRedoDescription => CanRedo ? _redo[^1].Description : null;

  public IReadOnlyList<CorrectionCommand> Commands => _undo;

  public void Record(CorrectionCommand command)
  {
    _undo.Add(command);
    // Anything previously undone is no longer reachable once history diverges.
    _redo.Clear();

    if (_undo.Count > MaxDepth)
      _undo.RemoveAt(0);
  }

  public CorrectionCommand? PopUndo()
  {
    if (!CanUndo) return null;

    var command = _undo[^1];
    _undo.RemoveAt(_undo.Count - 1);
    _redo.Add(command);
    return command;
  }

  public CorrectionCommand? PopRedo()
  {
    if (!CanRedo) return null;

    var command = _redo[^1];
    _redo.RemoveAt(_redo.Count - 1);
    _undo.Add(command);
    return command;
  }

  public void Clear()
  {
    _undo.Clear();
    _redo.Clear();
  }
}

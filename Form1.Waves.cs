namespace CrossMgrInterface;

/// <summary>
/// A staggered start: the classes leave the gate one after another and each
/// rider is timed from their own class's gate. See <see cref="WaveSchedule"/>.
///
/// The race engine is untouched by this. One clock runs from the first gate,
/// one flag ends the race for everyone, and the standings sort already works
/// from each rider's own start time - all this file does is decide when each
/// class has started and hold reads back until it has.
/// </summary>
public partial class Form1
{
  /// <summary>The start order and gaps, or null for a race with one start. Setup, not session state.</summary>
  private WaveSchedule? waves;

  /// <summary>The classes on the rider list, in the order the list has them.</summary>
  private List<string> RosterClasses() =>
    _riderDataImporter.GetAllRiderData().Values
      .Select(r => r.Category)
      .Where(c => !string.IsNullOrWhiteSpace(c))
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .ToList();

  /// <summary>
  /// The class a transponder belongs to, from the rider already known or from
  /// the rider list. Empty for an unidentified transponder, which the schedule
  /// puts with the first wave. The caller holds ridersLock.
  /// </summary>
  private string ClassOf(string tagID)
  {
    if (riders.TryGetValue(tagID, out var known) && !string.IsNullOrWhiteSpace(known.Category))
      return known.Category;
    return _riderDataImporter.GetRiderData(tagID)?.Category ?? "";
  }

  /// <summary>
  /// True when the rider's class has not left the gate yet, naming the wave
  /// they are waiting for. One class lookup: this runs under ridersLock on
  /// every read before the last wave has gone. The caller holds ridersLock.
  /// </summary>
  private bool WaitingForWave(string tagID, out StartWave wave)
  {
    wave = waves!.WaveFor(ClassOf(tagID));
    return wave.StartedAt == null;
  }

  /// <summary>
  /// Sends a class off at the given moment. Any rider of that class already
  /// known - after a restart, say - is re-timed from it.
  /// </summary>
  private void StartWave(StartWave wave, DateTime at)
  {
    if (waves == null) return;

    lock (ridersLock)
    {
      waves.Start(wave, at);
      foreach (var rider in riders.Values)
      {
        if (string.Equals(rider.Category, wave.Class, StringComparison.OrdinalIgnoreCase))
          rider.RaceStartTime = at;
      }
    }

    AnnounceWave(wave, at);
  }

  private void AnnounceWave(StartWave wave, DateTime at)
  {
    if (waves == null) return;

    var next = waves.Next;
    AddMessage(next == null
      ? $"🏁 {wave.Class} started at {at:HH:mm:ss} - every class is away."
      : $"🏁 {wave.Class} started at {at:HH:mm:ss} - {next.Class} in {WaveSchedule.FormatDelay(next.Delay)[1..]}.");
    RaiseNotice(NoticeLevel.Info, next == null
      ? $"{wave.Class} started - every class is away"
      : $"{wave.Class} started - {next.Class} next");

    if (currentRaceId.HasValue)
      Task.Run(() => SaveCurrentRaceState());

    _refresh.Invalidate(RaceViewKind.RaceDay);
  }

  /// <summary>Sends off every class whose time has come. Called from the clock tick.</summary>
  private void StartDueWaves()
  {
    if (waves == null || !raceStarted || raceFinished) return;

    // The scheduled moment rather than the tick's, so the class is timed from
    // when it was due to go, not from up to a second later.
    while (waves.NextDue(DateTime.Now) is { } due)
    {
      var at = waves.DueAt(due)!.Value;
      StartWave(due, at);
    }
  }

  /// <summary>The START <class> NOW button: the gate is dropping, so no dialog.</summary>
  private void StartNextWaveNow()
  {
    if (waves?.Next is not { } next || !raceStarted || raceFinished) return;
    StartWave(next, DateTime.Now);
  }

  /// <summary>One chip per class for the Race Day strip, or null when the race is not staggered.</summary>
  private List<WaveChip>? BuildWaveChips()
  {
    if (waves == null) return null;

    var now = DateTime.Now;
    var next = waves.Next;
    var chips = new List<WaveChip>(waves.Waves.Count);

    for (var i = 0; i < waves.Waves.Count; i++)
    {
      var wave = waves.Waves[i];
      if (wave.StartedAt.HasValue)
      {
        chips.Add(new WaveChip($"{wave.Class} ✓ {wave.StartedAt.Value:HH:mm:ss}", WaveChipState.Started));
        continue;
      }

      var due = waves.DueAt(wave);
      string text;
      if (!raceStarted)
        text = ReferenceEquals(wave, waves.First) ? $"{wave.Class} on START RACE" : $"{wave.Class} {WaveSchedule.FormatDelay(wave.Delay)}";
      else if (due.HasValue)
      {
        var left = due.Value - now;
        text = left > TimeSpan.Zero ? $"{wave.Class} in {left:m\\:ss}" : $"{wave.Class} now";
      }
      else
        text = $"{wave.Class} {WaveSchedule.FormatDelay(wave.Delay)} after {waves.Waves[i - 1].Class}";

      chips.Add(new WaveChip(text, ReferenceEquals(wave, next) ? WaveChipState.Next : WaveChipState.Waiting));
    }

    return chips;
  }
}

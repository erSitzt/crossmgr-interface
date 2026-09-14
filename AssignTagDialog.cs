namespace CrossMgrInterface;

/// <summary>
/// Resolves a transponder that is not in the imported rider list.
///
/// The common race-day case: a rider turns up with a spare transponder, laps
/// accumulate under a bare code, and the operator needs to attach a name to it -
/// or fold it onto the rider who is already being tracked under a different one -
/// without losing the laps already recorded.
/// </summary>
public sealed class AssignTagDialog : Form
{
  private readonly RadioButton _attach = new();
  private readonly RadioButton _merge = new();
  private readonly RadioButton _joinTeam = new();
  private readonly ComboBox _teamPicker = new();
  private readonly ComboBox _memberPicker = new();

  private readonly TextBox _number = new();
  private readonly TextBox _firstName = new();
  private readonly TextBox _lastName = new();
  private readonly TextBox _team = new();
  private readonly TextBox _category = new();

  private readonly ComboBox _rosterPicker = new();
  private readonly ComboBox _mergeTarget = new();
  private readonly CheckBox _dropDuplicates = new();
  private readonly Label _preview = new();

  private readonly IReadOnlyList<RiderInfo> _activeRiders;

  /// <summary>What the operator chose. Only meaningful after DialogResult.OK.</summary>
  public AssignTagRequest Request { get; private set; } = new();

  /// <param name="teams">In a team event, the teams the transponder could belong to; empty otherwise.</param>
  /// <param name="suggestion">The team, and which of its riders, the rider list says this transponder is.</param>
  public AssignTagDialog(
    string unknownTag,
    int lapsRecorded,
    IReadOnlyList<RiderImportRosterEntry> roster,
    IReadOnlyList<RiderInfo> activeRiders,
    IReadOnlyList<TeamJoinChoice>? teams = null,
    (string Key, int MemberIndex)? suggestion = null)
  {
    _activeRiders = activeRiders;
    teams ??= Array.Empty<TeamJoinChoice>();

    Text = "Identify transponder";
    FormBorderStyle = FormBorderStyle.FixedDialog;
    StartPosition = FormStartPosition.CenterParent;
    MinimizeBox = false;
    MaximizeBox = false;
    ClientSize = new Size(560, teams.Count > 0 ? 540 : 470);

    var header = new Label
    {
      Text = $"Transponder {unknownTag} has {lapsRecorded} recorded lap(s) but no rider.",
      Location = new Point(16, 14),
      Size = new Size(520, 24),
      Font = new Font(Font, FontStyle.Bold)
    };

    // ---- Option 1: attach an identity ----
    _attach.Text = "Give this transponder a rider";
    _attach.Location = new Point(16, 48);
    _attach.AutoSize = true;
    _attach.Checked = true;
    _attach.CheckedChanged += (_, _) => UpdateEnabledState();

    var rosterLabel = new Label { Text = "From the imported list:", Location = new Point(36, 80), AutoSize = true };
    _rosterPicker.Location = new Point(36, 102);
    _rosterPicker.Width = 490;
    _rosterPicker.DropDownStyle = ComboBoxStyle.DropDownList;
    _rosterPicker.Items.Add("(type the details in below)");
    foreach (var entry in roster)
      _rosterPicker.Items.Add(entry);
    _rosterPicker.SelectedIndex = 0;
    _rosterPicker.SelectedIndexChanged += (_, _) => FillFromRoster();

    var y = 136;
    Label FieldLabel(string text)
    {
      var label = new Label { Text = text, Location = new Point(36, y + 4), AutoSize = true };
      return label;
    }

    var numberLabel = FieldLabel("Number:");
    _number.Location = new Point(130, y); _number.Width = 80; y += 30;

    var firstLabel = FieldLabel("First name:");
    _firstName.Location = new Point(130, y); _firstName.Width = 180; y += 30;

    var lastLabel = FieldLabel("Last name:");
    _lastName.Location = new Point(130, y); _lastName.Width = 180; y += 30;

    var teamLabel = FieldLabel("Team:");
    _team.Location = new Point(130, y); _team.Width = 180; y += 30;

    var classLabel = FieldLabel("Class:");
    _category.Location = new Point(130, y); _category.Width = 180; y += 38;

    // ---- Option 3, team events: a team rider's transponder ----
    _joinTeam.Visible = _teamPicker.Visible = _memberPicker.Visible = teams.Count > 0;
    if (teams.Count > 0)
    {
      _joinTeam.Text = "This transponder belongs to a team";
      _joinTeam.Location = new Point(16, y);
      _joinTeam.AutoSize = true;
      _joinTeam.CheckedChanged += (_, _) => UpdateEnabledState();
      y += 30;

      _teamPicker.Location = new Point(36, y);
      _teamPicker.Width = 240;
      _teamPicker.DropDownStyle = ComboBoxStyle.DropDownList;
      foreach (var team in teams)
        _teamPicker.Items.Add(team);
      _teamPicker.SelectedIndexChanged += (_, _) => FillMembers();

      _memberPicker.Location = new Point(286, y);
      _memberPicker.Width = 240;
      _memberPicker.DropDownStyle = ComboBoxStyle.DropDownList;
      _memberPicker.SelectedIndexChanged += (_, _) => UpdatePreview();
      y += 40;
    }

    // ---- Option 2: merge ----
    _merge.Text = "These laps belong to a rider already in the race";
    _merge.Location = new Point(16, y);
    _merge.AutoSize = true;
    _merge.CheckedChanged += (_, _) => UpdateEnabledState();
    y += 30;

    _mergeTarget.Location = new Point(36, y);
    _mergeTarget.Width = 490;
    _mergeTarget.DropDownStyle = ComboBoxStyle.DropDownList;
    // Teams are joined above, where the rider can be named too.
    foreach (var rider in activeRiders.Where(r => r.TagID != unknownTag && !(teams.Count > 0 && r.IsTeam)))
      _mergeTarget.Items.Add(new MergeChoice(rider));
    if (_mergeTarget.Items.Count > 0) _mergeTarget.SelectedIndex = 0;
    _mergeTarget.SelectedIndexChanged += (_, _) => UpdatePreview();
    y += 32;

    _dropDuplicates.Text = "Drop reads that clash with a lap already recorded";
    _dropDuplicates.Location = new Point(36, y);
    _dropDuplicates.AutoSize = true;
    _dropDuplicates.Checked = true;
    y += 28;

    _preview.Location = new Point(36, y);
    _preview.Size = new Size(490, 36);
    _preview.ForeColor = Color.DimGray;

    var ok = new Button
    {
      Text = "Apply",
      DialogResult = DialogResult.OK,
      Location = new Point(ClientSize.Width - 200, ClientSize.Height - 44),
      Size = new Size(88, 30)
    };
    var cancel = new Button
    {
      Text = "Cancel",
      DialogResult = DialogResult.Cancel,
      Location = new Point(ClientSize.Width - 104, ClientSize.Height - 44),
      Size = new Size(88, 30)
    };

    ok.Click += (_, _) => Request = BuildRequest();

    Controls.AddRange(new Control[]
    {
      header, _attach, rosterLabel, _rosterPicker,
      numberLabel, _number, firstLabel, _firstName, lastLabel, _lastName,
      teamLabel, _team, classLabel, _category,
      _joinTeam, _teamPicker, _memberPicker,
      _merge, _mergeTarget, _dropDuplicates, _preview, ok, cancel
    });

    AcceptButton = ok;
    CancelButton = cancel;

    // No rider to merge into means only one sensible option.
    if (_mergeTarget.Items.Count == 0)
    {
      _merge.Enabled = false;
      _merge.Text += " (nobody else is being tracked)";
    }

    if (_teamPicker.Items.Count > 0)
    {
      // The rider list already says whose it is: offer that, rather than making
      // the operator find it.
      var suggested = suggestion is { } s ? teams.ToList().FindIndex(t => t.Key == s.Key) : -1;
      _teamPicker.SelectedIndex = Math.Max(0, suggested);
      if (suggested >= 0)
      {
        _joinTeam.Checked = true;
        if (suggestion!.Value.MemberIndex >= 0 && suggestion.Value.MemberIndex + 1 < _memberPicker.Items.Count)
          _memberPicker.SelectedIndex = suggestion.Value.MemberIndex + 1;
      }
    }

    UpdateEnabledState();
  }

  private void FillMembers()
  {
    _memberPicker.Items.Clear();
    _memberPicker.Items.Add("(not sure which rider)");
    if (_teamPicker.SelectedItem is TeamJoinChoice team && team.Template.Members is { } members)
      foreach (var member in members)
        _memberPicker.Items.Add(member.Label);
    _memberPicker.SelectedIndex = 0;
    UpdatePreview();
  }

  private void FillFromRoster()
  {
    if (_rosterPicker.SelectedItem is not RiderImportRosterEntry entry) return;

    _number.Text = entry.RiderNumber;
    _firstName.Text = entry.FirstName;
    _lastName.Text = entry.LastName;
    _team.Text = entry.Team;
    _category.Text = entry.Category;
  }

  private void UpdateEnabledState()
  {
    var attaching = _attach.Checked;

    _rosterPicker.Enabled = attaching;
    _number.Enabled = _firstName.Enabled = _lastName.Enabled = attaching;
    _team.Enabled = _category.Enabled = attaching;

    _mergeTarget.Enabled = _merge.Checked;
    _teamPicker.Enabled = _memberPicker.Enabled = _joinTeam.Checked;
    _dropDuplicates.Enabled = !attaching;

    UpdatePreview();
  }

  private void UpdatePreview()
  {
    if (_attach.Checked)
    {
      _preview.Text = "The laps already recorded stay exactly as they are.";
      return;
    }

    if (_joinTeam.Checked)
    {
      if (_teamPicker.SelectedItem is not TeamJoinChoice team) return;
      var rider = _memberPicker.SelectedIndex > 0 ? $", as {_memberPicker.SelectedItem}'s transponder" : "";
      _preview.Text = team.Racing
        ? $"{team.Template.Label} has {team.Template.TotalLaps} lap(s). This transponder's laps are added to theirs{rider}."
        : $"{team.Template.Label} has not crossed the line yet. It starts with this transponder's laps{rider}.";
      return;
    }

    if (_mergeTarget.SelectedItem is MergeChoice choice)
    {
      _preview.Text =
        $"{choice.Rider.Label} currently has {choice.Rider.TotalLaps} lap(s). " +
        "The laps from this transponder will be added to theirs.";
    }
  }

  private AssignTagRequest BuildRequest()
  {
    if (_joinTeam.Checked && _teamPicker.SelectedItem is TeamJoinChoice team)
    {
      return new AssignTagRequest
      {
        Mode = AssignTagMode.JoinTeam,
        TeamKey = team.Key,
        TeamTemplate = team.Racing ? null : team.Template,
        MemberIndex = _memberPicker.SelectedIndex > 0 ? _memberPicker.SelectedIndex - 1 : null,
        DropDuplicateCrossings = _dropDuplicates.Checked
      };
    }

    if (_merge.Checked && _mergeTarget.SelectedItem is MergeChoice choice)
    {
      return new AssignTagRequest
      {
        Mode = AssignTagMode.MergeIntoRider,
        MergeTargetTag = choice.Rider.TagID,
        DropDuplicateCrossings = _dropDuplicates.Checked
      };
    }

    return new AssignTagRequest
    {
      Mode = AssignTagMode.AttachIdentity,
      RiderNumber = _number.Text.Trim(),
      FirstName = _firstName.Text.Trim(),
      LastName = _lastName.Text.Trim(),
      Team = _team.Text.Trim(),
      Category = _category.Text.Trim()
    };
  }

  /// <summary>Combo entry for a rider already being tracked.</summary>
  private sealed record MergeChoice(RiderInfo Rider)
  {
    public override string ToString() => $"{Rider.Label} - {Rider.TotalLaps} lap(s)";
  }
}

/// <summary>
/// A team a transponder can be joined to. <paramref name="Template"/> is the
/// team's entry when it is racing, or the entry it would start with when not.
/// </summary>
public sealed record TeamJoinChoice(string Key, RiderInfo Template, bool Racing)
{
  public override string ToString() => Racing
    ? $"{Template.Label} - {Template.TotalLaps} lap(s)"
    : $"{Template.Label}  (no laps yet)";
}

/// <summary>
/// One entry from the imported rider list, shown in the picker. Kept separate
/// from the importer's own type so the dialog can render it sensibly.
/// </summary>
public sealed record RiderImportRosterEntry(
  string TagID, string RiderNumber, string FirstName, string LastName, string Team, string Category)
{
  /// <summary>True when no laps have been recorded against this entry yet.</summary>
  public bool Unused { get; init; }

  public override string ToString()
  {
    var name = $"{FirstName} {LastName}".Trim();
    var label = string.IsNullOrEmpty(RiderNumber) ? name : $"#{RiderNumber} {name}";
    if (string.IsNullOrWhiteSpace(label)) label = TagID;
    return Unused ? $"{label}  (no laps yet)" : label;
  }
}

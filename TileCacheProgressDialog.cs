namespace CrossMgrInterface;

/// <summary>
/// Downloads a circuit's map tiles, with a progress bar and a Cancel that works.
///
/// Built in code like the other dialogs here. It quotes the tile count and a size
/// BEFORE starting, because the tile usage policy makes this something you do
/// deliberately once, not something that should happen because a button was
/// nearby.
/// </summary>
public sealed class TileCacheProgressDialog : Form
{
  private readonly MapTilePrefetcher _prefetcher;
  private readonly GeoBounds _bounds;

  private readonly NumericUpDown _minZoom;
  private readonly NumericUpDown _maxZoom;
  private readonly Label _estimate;
  private readonly Label _status;
  private readonly ProgressBar _bar;
  private readonly Button _start;
  private readonly Button _close;

  private CancellationTokenSource? _cts;
  private bool _running;

  public TileCacheProgressDialog(MapTilePrefetcher prefetcher, GeoBounds bounds)
  {
    _prefetcher = prefetcher;
    _bounds = bounds;

    Text = "Download map for offline use";
    FormBorderStyle = FormBorderStyle.FixedDialog;
    StartPosition = FormStartPosition.CenterParent;
    MinimizeBox = false;
    MaximizeBox = false;
    ClientSize = new Size(460, 250);

    var intro = new Label
    {
      Text = "Race venues often have no usable internet. Downloading the map now\n" +
             "means the circuit still draws on race day.",
      Location = new Point(16, 14),
      Size = new Size(430, 36)
    };

    var zoomLabel = new Label { Text = "Detail levels:", Location = new Point(16, 62), AutoSize = true };

    _minZoom = new NumericUpDown
    {
      Minimum = 10, Maximum = TileMath.MaxZoom, Value = MapTilePrefetcher.DefaultMinZoom,
      Location = new Point(110, 60), Width = 60
    };

    var toLabel = new Label { Text = "to", Location = new Point(178, 62), AutoSize = true };

    _maxZoom = new NumericUpDown
    {
      Minimum = 10, Maximum = TileMath.MaxZoom, Value = MapTilePrefetcher.DefaultMaxZoom,
      Location = new Point(202, 60), Width = 60
    };

    _minZoom.ValueChanged += (_, _) => Requote();
    _maxZoom.ValueChanged += (_, _) => Requote();

    _estimate = new Label { Location = new Point(16, 96), Size = new Size(430, 20), ForeColor = Color.DimGray };
    _bar = new ProgressBar { Location = new Point(16, 126), Size = new Size(430, 20) };
    _status = new Label { Location = new Point(16, 154), Size = new Size(430, 36) };

    _start = new Button { Text = "Download", Location = new Point(272, 202), Size = new Size(88, 28) };
    _start.Click += async (_, _) => await StartOrCancel();

    _close = new Button { Text = "Close", Location = new Point(366, 202), Size = new Size(80, 28) };
    _close.Click += (_, _) => Close();

    Controls.AddRange(new Control[]
    {
      intro, zoomLabel, _minZoom, toLabel, _maxZoom, _estimate, _bar, _status, _start, _close
    });

    AcceptButton = _start;
    CancelButton = _close;

    Requote();
  }

  private void Requote()
  {
    if (_running) return;

    var min = (int)_minZoom.Value;
    var max = Math.Max(min, (int)_maxZoom.Value);

    var estimate = MapTilePrefetcher.Estimate(_bounds, min, max);
    var cached = _prefetcher.CountCached(_bounds, min, max);

    var percent = estimate.TileCount == 0 ? 100 : cached * 100 / estimate.TileCount;
    _estimate.Text = $"{estimate.Describe()}  {percent}% already on this computer.";

    var tooMany = estimate.TileCount > MapTilePrefetcher.HardTileLimit;
    _start.Enabled = estimate.TileCount > 0 && !tooMany;

    if (tooMany)
      _status.Text = $"That is more than {MapTilePrefetcher.HardTileLimit:N0} tiles. " +
                     "Reduce the top detail level.";
  }

  private async Task StartOrCancel()
  {
    if (_running)
    {
      _cts?.Cancel();
      _start.Enabled = false;
      return;
    }

    var min = (int)_minZoom.Value;
    var max = Math.Max(min, (int)_maxZoom.Value);

    _running = true;
    _start.Text = "Stop";
    _minZoom.Enabled = _maxZoom.Enabled = false;
    _cts = new CancellationTokenSource();

    // Progress<T> captures this thread's SynchronizationContext, so the callback
    // lands back on the UI thread without the prefetcher knowing anything about it.
    var progress = new Progress<PrefetchProgress>(p =>
    {
      _bar.Maximum = Math.Max(1, p.Total);
      _bar.Value = Math.Min(p.Completed, _bar.Maximum);
      _status.Text = $"Zoom {p.CurrentZoom}:  {p.Completed:N0} of {p.Total:N0}  " +
                     $"({p.Downloaded:N0} downloaded, {p.AlreadyCached:N0} already here" +
                     (p.Failed > 0 ? $", {p.Failed:N0} failed" : "") + ")";
    });

    try
    {
      var result = await _prefetcher.DownloadAsync(_bounds, min, max, progress, _cts.Token);
      _status.Text = result.Describe();
    }
    catch (InvalidOperationException ex)
    {
      _status.Text = ex.Message;
    }
    catch (Exception ex)
    {
      _status.Text = $"The download stopped: {ex.Message}";
    }
    finally
    {
      _running = false;
      _start.Text = "Download";
      _start.Enabled = true;
      _minZoom.Enabled = _maxZoom.Enabled = true;
      _cts?.Dispose();
      _cts = null;
    }
  }

  protected override void OnFormClosing(FormClosingEventArgs e)
  {
    _cts?.Cancel();
    base.OnFormClosing(e);
  }
}

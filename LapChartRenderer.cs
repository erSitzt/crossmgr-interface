using System.Drawing.Drawing2D;
using System.Text;

namespace CrossMgrInterface;

/// <summary>
/// Handles the visualization and interaction for the lap chart
/// </summary>
public class LapChartRenderer
{
  private readonly List<LapChartElement> _lapChartElements = new();
  private string? _selectedRiderId = null;
  private string? _hoveredLapInfo = null;

  public string? SelectedRiderId => _selectedRiderId;
  public string? HoveredLapInfo => _hoveredLapInfo;

  /// <summary>
  /// Draws the complete lap chart
  /// </summary>
  public void DrawLapChart(Graphics g, Rectangle bounds, Dictionary<string, RiderInfo> riders,
      DateTime? raceStartTime, DateTime? raceEndTime, TimeSpan raceDuration, Panel panelLapChart)
  {
    if (bounds.Width <= 0 || bounds.Height <= 0)
      return;

    // Set graphics quality settings for better performance
    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
    g.SmoothingMode = SmoothingMode.HighSpeed;

    // Account for scroll position
    var scrollOffset = panelLapChart.AutoScrollPosition;
    g.TranslateTransform(scrollOffset.X, scrollOffset.Y);

    // Clear previous clickable elements
    _lapChartElements.Clear();

    if (riders.Count == 0 || !raceStartTime.HasValue || !raceEndTime.HasValue)
    {
      DrawNoDataMessage(g, bounds);
      return;
    }

    // Calculate race duration and timing
    var raceDurationMs = raceDuration.TotalMilliseconds;
    var extendedDurationMs = CalculateExtendedChartDuration(raceDurationMs);
    var raceElapsedMs = (DateTime.Now - raceStartTime.Value).TotalMilliseconds;
    var raceProgressPercent = Math.Min(raceElapsedMs / extendedDurationMs, 1.0);

    // Sort riders by position (same as leaderboard): finishing riders first, then DNF riders
    var sortedRiders = riders.Values
        .OrderBy(r => r.IsDNF ? 1 : 0)
        .ThenByDescending(r => r.TotalLaps)
        .ThenBy(r => r.TotalTime)
        .ToList();

    // Chart layout parameters
    const int margin = 20;
    const int riderBarHeight = 40;
    const int riderSpacing = 5;
    const int labelWidth = 200; // Increased to accommodate up to 24-character tag IDs
    var chartWidth = bounds.Width - margin * 2 - labelWidth;
    var chartHeight = sortedRiders.Count * (riderBarHeight + riderSpacing);

    // Set auto-scroll minimum size
    var minContentHeight = chartHeight + margin * 2 + 50;
    var minContentWidth = margin * 2 + labelWidth + chartWidth;
    panelLapChart.AutoScrollMinSize = new Size(minContentWidth, minContentHeight);

    // Draw title
    DrawTitle(g, margin, raceElapsedMs, raceDuration);

    var chartTop = margin + 60;
    var barFont = new Font("Arial", 10);

    // Draw time scale at top
    DrawTimeScale(g, new Rectangle(margin + labelWidth, chartTop - 25, chartWidth, 20), extendedDurationMs, raceDurationMs);

    // Draw each rider's bar
    for (int i = 0; i < sortedRiders.Count; i++)
    {
      var rider = sortedRiders[i];
      var y = chartTop + i * (riderBarHeight + riderSpacing);
      var barRect = new Rectangle(margin + labelWidth, y, chartWidth, riderBarHeight);

      DrawRiderLapBar(g, rider, barRect, extendedDurationMs, raceDurationMs, i + 1, raceStartTime.Value);

      // Draw rider label
      DrawRiderLabel(g, rider, margin, y, labelWidth, riderBarHeight, i);
    }

    barFont.Dispose();

    // Draw progress and time lines on top
    DrawProgressAndTimeLines(g, margin, labelWidth, chartWidth, chartTop, chartHeight,
        raceProgressPercent, extendedDurationMs, raceDurationMs, raceElapsedMs, raceDuration);

    // Draw hover tooltip if there's hovered lap info
    if (!string.IsNullOrEmpty(_hoveredLapInfo))
    {
      var mousePos = panelLapChart.PointToClient(Cursor.Position);
      DrawTooltip(g, _hoveredLapInfo, mousePos);
    }
  }

  /// <summary>
  /// Handles mouse click events on the lap chart
  /// </summary>
  public bool HandleMouseClick(Point adjustedLocation, Action<string> showRiderDetailsCallback, Action invalidateCallback)
  {
    var clickedElement = _lapChartElements.FirstOrDefault(elem => elem.Bounds.Contains(adjustedLocation));
    if (clickedElement != null && clickedElement.IsRider)
    {
      _selectedRiderId = clickedElement.RiderId;
      showRiderDetailsCallback(clickedElement.RiderId);
      invalidateCallback(); // Redraw to show selection
      return true;
    }
    return false;
  }

  /// <summary>
  /// Handles mouse move events on the lap chart
  /// </summary>
  public bool HandleMouseMove(Point adjustedLocation, Action invalidateCallback)
  {
    var hoveredElement = _lapChartElements.FirstOrDefault(elem => elem.Bounds.Contains(adjustedLocation));

    string? newHoverInfo = null;
    if (hoveredElement != null && !hoveredElement.IsRider && hoveredElement.LapTime.HasValue)
    {
      newHoverInfo = $"Lap {hoveredElement.LapNumber}: {hoveredElement.LapTime.Value:mm\\:ss\\.fff}";
    }

    if (newHoverInfo != _hoveredLapInfo)
    {
      _hoveredLapInfo = newHoverInfo;
      invalidateCallback(); // Redraw to show/hide tooltip
      return true;
    }

    return hoveredElement != null;
  }

  /// <summary>
  /// Handles mouse leave events
  /// </summary>
  public void HandleMouseLeave(Action invalidateCallback)
  {
    if (_hoveredLapInfo != null)
    {
      _hoveredLapInfo = null;
      invalidateCallback(); // Hide tooltip
    }
  }

  private void DrawNoDataMessage(Graphics g, Rectangle bounds)
  {
    var font = new Font("Arial", 16, FontStyle.Bold);
    var text = "No race data available";
    var textSize = g.MeasureString(text, font);
    var x = (bounds.Width - textSize.Width) / 2;
    var y = (bounds.Height - textSize.Height) / 2;
    g.DrawString(text, font, Brushes.Gray, x, y);
    font.Dispose();
  }

  private void DrawTitle(Graphics g, int margin, double raceElapsedMs, TimeSpan raceDuration)
  {
    var titleFont = new Font("Arial", 14, FontStyle.Bold);
    var raceTimeElapsed = TimeSpan.FromMilliseconds(raceElapsedMs);
    var title = $"Lap Visualization - Race: {raceTimeElapsed:mm\\:ss} / {raceDuration:mm\\:ss}";
    g.DrawString(title, titleFont, Brushes.Black, margin, margin);
    titleFont.Dispose();
  }

  private void DrawRiderLabel(Graphics g, RiderInfo rider, int margin, int y, int labelWidth, int riderBarHeight, int position)
  {
    var labelRect = new Rectangle(margin, y, labelWidth - 10, riderBarHeight);
    var labelText = $"#{position + 1}: {rider.TagID}";
    var labelBrush = GetPositionBrush(position);

    // Highlight if this rider is selected
    if (_selectedRiderId == rider.TagID)
    {
      var highlightRect = new Rectangle(labelRect.X - 3, labelRect.Y - 3,
          labelRect.Width + 6, labelRect.Height + 6);
      g.FillRectangle(Brushes.Yellow, highlightRect);
      g.DrawRectangle(new Pen(Color.Orange, 3), highlightRect);
    }

    g.FillRectangle(labelBrush, labelRect);
    g.DrawRectangle(Pens.Black, labelRect);

    // Add rider label as clickable element
    _lapChartElements.Add(new LapChartElement
    {
      Bounds = labelRect,
      RiderId = rider.TagID,
      IsRider = true
    });

    var textBrush = position < 3 ? Brushes.Black : Brushes.White;

    // Use adaptive font sizing to fit longer tag IDs
    var fontSize = 10;
    Font font = new Font("Arial", fontSize, FontStyle.Bold);
    var textSize = g.MeasureString(labelText, font);

    // If text doesn't fit, reduce font size
    while (textSize.Width > labelRect.Width - 4 && fontSize > 6)
    {
      font.Dispose();
      fontSize--;
      font = new Font("Arial", fontSize, FontStyle.Bold);
      textSize = g.MeasureString(labelText, font);
    }

    // If still too long, try splitting into two lines
    if (textSize.Width > labelRect.Width - 4)
    {
      font.Dispose();
      font = new Font("Arial", 8, FontStyle.Bold);

      // Split text: position on first line, tag ID on second line
      var positionText = $"#{position + 1}:";
      var tagText = rider.TagID;

      var positionSize = g.MeasureString(positionText, font);
      var tagSize = g.MeasureString(tagText, font);

      // Draw position text centered on first line
      var positionY = labelRect.Y + 2;
      var positionX = labelRect.X + (labelRect.Width - positionSize.Width) / 2;
      g.DrawString(positionText, font, textBrush, positionX, positionY);

      // Draw tag ID centered on second line
      var tagY = labelRect.Y + labelRect.Height / 2 + 2;
      var tagX = labelRect.X + (labelRect.Width - tagSize.Width) / 2;
      g.DrawString(tagText, font, textBrush, tagX, tagY);
    }
    else
    {
      // Single line - center the text
      var textX = labelRect.X + (labelRect.Width - textSize.Width) / 2;
      var textY = labelRect.Y + (labelRect.Height - textSize.Height) / 2;
      g.DrawString(labelText, font, textBrush, textX, textY);
    }

    font.Dispose();
    labelBrush.Dispose();
  }

  private void DrawProgressAndTimeLines(Graphics g, int margin, int labelWidth, int chartWidth,
      int chartTop, int chartHeight, double raceProgressPercent, double extendedDurationMs,
      double raceDurationMs, double raceElapsedMs, TimeSpan raceDuration)
  {
    // Draw race progress line - thick and prominent
    var progressX = margin + labelWidth + (int)(chartWidth * raceProgressPercent);
    var progressPen = new Pen(Color.Red, 4) { DashStyle = DashStyle.Solid };

    // Draw progress line from top of time scale to bottom of chart
    g.DrawLine(progressPen, progressX, chartTop - 25, progressX, chartTop + chartHeight);

    // Add current time indicator at the top
    var currentTimeFont = new Font("Arial", 10, FontStyle.Bold);
    var elapsedTime = TimeSpan.FromMilliseconds(raceElapsedMs);
    var currentTimeText = $"NOW: {elapsedTime:mm\\:ss}";
    var timeTextSize = g.MeasureString(currentTimeText, currentTimeFont);
    var timeTextX = progressX - timeTextSize.Width / 2;
    var timeTextY = chartTop - 45;

    // Draw background for current time text
    var timeTextRect = new Rectangle((int)timeTextX - 3, (int)timeTextY - 2,
        (int)timeTextSize.Width + 6, (int)timeTextSize.Height + 4);
    g.FillRectangle(Brushes.Red, timeTextRect);
    g.DrawRectangle(Pens.Black, timeTextRect);
    g.DrawString(currentTimeText, currentTimeFont, Brushes.White, timeTextX, timeTextY);
    currentTimeFont.Dispose();

    // Add a semi-transparent overlay for future time
    if (progressX < margin + labelWidth + chartWidth)
    {
      var futureRect = new Rectangle(progressX, chartTop,
          margin + labelWidth + chartWidth - progressX, chartHeight);
      var futureBrush = new SolidBrush(Color.FromArgb(30, 255, 0, 0));
      g.FillRectangle(futureBrush, futureRect);
      futureBrush.Dispose();
    }

    // Draw race end time line
    var raceEndX = margin + labelWidth + (int)(chartWidth * (raceDurationMs / extendedDurationMs));
    if (raceEndX != progressX)
    {
      var raceEndPen = new Pen(Color.Orange, 3) { DashStyle = DashStyle.Dash };
      g.DrawLine(raceEndPen, raceEndX, chartTop - 25, raceEndX, chartTop + chartHeight);

      // Add race end time indicator
      var raceEndTimeFont = new Font("Arial", 9, FontStyle.Bold);
      var raceEndTimeText = $"TIME: {raceDuration:mm\\:ss}";
      var raceEndTextSize = g.MeasureString(raceEndTimeText, raceEndTimeFont);
      var raceEndTextX = raceEndX - raceEndTextSize.Width / 2;
      var raceEndTextY = chartTop - 45;

      // Offset if too close to current time indicator
      if (Math.Abs(raceEndTextX - timeTextX) < raceEndTextSize.Width)
      {
        raceEndTextY = chartTop - 25;
      }

      // Draw background for race end time text
      var raceEndTextRect = new Rectangle((int)raceEndTextX - 3, (int)raceEndTextY - 2,
          (int)raceEndTextSize.Width + 6, (int)raceEndTextSize.Height + 4);
      g.FillRectangle(Brushes.Orange, raceEndTextRect);
      g.DrawRectangle(Pens.Black, raceEndTextRect);
      g.DrawString(raceEndTimeText, raceEndTimeFont, Brushes.Black, raceEndTextX, raceEndTextY);

      raceEndPen.Dispose();
      raceEndTimeFont.Dispose();
    }

    progressPen.Dispose();
  }

  private void DrawTimeScale(Graphics g, Rectangle bounds, double extendedDurationMs, double raceDurationMs)
  {
    var font = new Font("Arial", 10, FontStyle.Bold);
    var pen = new Pen(Color.Black, 2);
    var lightPen = new Pen(Color.LightGray, 1);
    var extendedPen = new Pen(Color.Gray, 1) { DashStyle = DashStyle.Dot };

    // Draw background for better contrast
    g.FillRectangle(Brushes.White, bounds);
    g.DrawRectangle(Pens.Black, bounds);

    // Choose appropriate interval based on extended duration
    var totalMinutes = extendedDurationMs / 60000.0;
    double majorIntervalMs;
    double minorIntervalMs;

    if (totalMinutes <= 5)
    {
      majorIntervalMs = 1 * 60 * 1000; // 1 minute major, 30 second minor
      minorIntervalMs = 30 * 1000;
    }
    else if (totalMinutes <= 15)
    {
      majorIntervalMs = 2 * 60 * 1000; // 2 minute major, 1 minute minor
      minorIntervalMs = 1 * 60 * 1000;
    }
    else
    {
      majorIntervalMs = 5 * 60 * 1000; // 5 minute major, 1 minute minor
      minorIntervalMs = 1 * 60 * 1000;
    }

    // Draw major tick marks
    var majorIntervals = (int)(extendedDurationMs / majorIntervalMs) + 1;
    for (int i = 0; i <= majorIntervals; i++)
    {
      var timeMs = i * majorIntervalMs;
      if (timeMs > extendedDurationMs) break;

      var x = bounds.X + (int)(bounds.Width * (timeMs / extendedDurationMs));
      var penToUse = timeMs <= raceDurationMs ? pen : extendedPen;

      g.DrawLine(penToUse, x, bounds.Y, x, bounds.Bottom);

      // Draw time label
      var timeSpan = TimeSpan.FromMilliseconds(timeMs);
      var timeText = timeSpan.ToString(@"mm\:ss");
      var textSize = g.MeasureString(timeText, font);
      var textX = x - textSize.Width / 2;
      var textY = bounds.Y + 2;
      g.DrawString(timeText, font, Brushes.Black, textX, textY);
    }

    // Draw minor tick marks
    var minorIntervals = (int)(extendedDurationMs / minorIntervalMs) + 1;
    for (int i = 0; i <= minorIntervals; i++)
    {
      var timeMs = i * minorIntervalMs;
      if (timeMs > extendedDurationMs) break;
      if (timeMs % majorIntervalMs == 0) continue; // Skip major intervals

      var x = bounds.X + (int)(bounds.Width * (timeMs / extendedDurationMs));
      var penToUse = timeMs <= raceDurationMs ? lightPen : extendedPen;

      g.DrawLine(penToUse, x, bounds.Y + bounds.Height - 5, x, bounds.Bottom);
    }

    font.Dispose();
    pen.Dispose();
    lightPen.Dispose();
    extendedPen.Dispose();
  }

  private void DrawRiderLapBar(Graphics g, RiderInfo rider, Rectangle bounds, double extendedDurationMs,
      double raceDurationMs, int position, DateTime raceStartTime)
  {
    // Background
    g.FillRectangle(Brushes.LightGray, bounds);
    g.DrawRectangle(Pens.Black, bounds);

    if (rider.Laps.Count == 0) return;

    var lapColors = GetLapColors();

    // Draw completed laps based on actual race timeline
    for (int i = 0; i < rider.Laps.Count; i++)
    {
      var lap = rider.Laps[i];

      // Calculate when this lap started and ended in race time
      DateTime lapStartTime;
      TimeSpan? lapDuration;

      if (i == 0)
      {
        // First lap starts at race start
        lapStartTime = raceStartTime;
        if (lap.LapTime == null)
        {
          // Calculate first lap time from race start to crossing
          lapDuration = lap.CrossingTime - raceStartTime;
        }
        else
        {
          lapDuration = lap.LapTime;
        }
      }
      else
      {
        // Subsequent laps start when previous lap ended
        lapStartTime = rider.Laps[i - 1].CrossingTime;
        lapDuration = lap.LapTime;
      }

      if (!lapDuration.HasValue || lapDuration.Value.TotalMilliseconds <= 0)
        continue;

      // Calculate position in race timeline using extended duration
      var lapStartMs = (lapStartTime - raceStartTime).TotalMilliseconds;
      var lapDurationMs = lapDuration.Value.TotalMilliseconds;

      var lapStartX = bounds.X + (int)(bounds.Width * (lapStartMs / extendedDurationMs));
      var lapWidth = (int)(bounds.Width * (lapDurationMs / extendedDurationMs));

      var lapRect = new Rectangle(
          lapStartX,
          bounds.Y + 2,
          lapWidth,
          bounds.Height - 4
      );

      if (lapRect.Width > 0 && lapRect.X < bounds.Right && lapRect.Right > bounds.X)
      {
        var colorIndex = i % lapColors.Length;
        g.FillRectangle(new SolidBrush(lapColors[colorIndex]), lapRect);
        g.DrawRectangle(Pens.Black, lapRect);

        // Add lap rectangle as hoverable element
        _lapChartElements.Add(new LapChartElement
        {
          Bounds = lapRect,
          RiderId = rider.TagID,
          LapNumber = i + 1,
          LapTime = lapDuration,
          IsRider = false
        });

        // Draw lap number if there's space
        if (lapRect.Width > 20)
        {
          var lapText = (i + 1).ToString();
          var font = new Font("Arial", 8, FontStyle.Bold);
          var textSize = g.MeasureString(lapText, font);
          var textX = lapRect.X + (lapRect.Width - textSize.Width) / 2;
          var textY = lapRect.Y + (lapRect.Height - textSize.Height) / 2;
          g.DrawString(lapText, font, Brushes.Black, textX, textY);
          font.Dispose();
        }
      }
    }

    // Draw predicted future laps
    DrawPredictedLaps(g, rider, bounds, extendedDurationMs, raceDurationMs, raceStartTime, lapColors);

    // Draw statistics text
    DrawRiderStats(g, rider, bounds);
  }

  private void DrawPredictedLaps(Graphics g, RiderInfo rider, Rectangle bounds, double extendedDurationMs,
      double raceDurationMs, DateTime raceStartTime, Color[] lapColors)
  {
    if (!rider.PredictedLapTime.HasValue || rider.Laps.Count == 0) return;

    var lastLapEndTime = rider.Laps.Last().CrossingTime;
    var predictedLapMs = rider.PredictedLapTime.Value.TotalMilliseconds;
    var lapNumber = rider.TotalLaps + 1;
    var currentPredictedTime = lastLapEndTime;

    // Calculate maximum laps to display based on race completion rules
    int maxLapsToShow = CalculateMaxLapsForRaceCompletion(rider);

    while (currentPredictedTime < raceStartTime.AddMilliseconds(extendedDurationMs) && lapNumber <= maxLapsToShow)
    {
      var lapStartMs = (currentPredictedTime - raceStartTime).TotalMilliseconds;

      var lapStartX = bounds.X + (int)(bounds.Width * (lapStartMs / extendedDurationMs));
      var lapWidth = (int)(bounds.Width * (predictedLapMs / extendedDurationMs));

      var lapRect = new Rectangle(
          lapStartX,
          bounds.Y + 2,
          lapWidth,
          bounds.Height - 4
      );

      if (lapRect.Width > 0 && lapRect.X < bounds.Right && lapRect.Right > bounds.X)
      {
        // Use different styling for laps before and after original race time
        var baseColor = lapColors[(lapNumber - 1) % lapColors.Length];
        var isAfterRaceTime = lapStartMs > raceDurationMs;

        Color lapColor = isAfterRaceTime ?
            Color.FromArgb(80, baseColor.R, baseColor.G, baseColor.B) :
            Color.FromArgb(128, baseColor.R, baseColor.G, baseColor.B);

        var brush = new SolidBrush(lapColor);
        g.FillRectangle(brush, lapRect);

        // Dashed border for predicted laps
        var pen = new Pen(baseColor, 1) { DashStyle = DashStyle.Dash };
        g.DrawRectangle(pen, lapRect);

        brush.Dispose();
        pen.Dispose();

        // Draw predicted lap number
        if (lapRect.Width > 20)
        {
          var lapText = lapNumber.ToString();
          var font = new Font("Arial", 8, FontStyle.Italic);
          var textSize = g.MeasureString(lapText, font);
          var textX = lapRect.X + (lapRect.Width - textSize.Width) / 2;
          var textY = lapRect.Y + (lapRect.Height - textSize.Height) / 2;
          g.DrawString(lapText, font, Brushes.Gray, textX, textY);
          font.Dispose();
        }
      }

      currentPredictedTime = currentPredictedTime.AddMilliseconds(predictedLapMs);
      lapNumber++;
    }
  }

  private void DrawRiderStats(Graphics g, RiderInfo rider, Rectangle bounds)
  {
    var statsFont = new Font("Arial", 8);
    var stats = $"Laps: {rider.TotalLaps}";
    if (rider.BestLapTime.HasValue)
      stats += $" | Best: {rider.BestLapTime.Value:mm\\:ss}";
    if (rider.PredictedLapTime.HasValue)
      stats += $" | Pred: {rider.PredictedLapTime.Value:mm\\:ss}";

    g.DrawString(stats, statsFont, Brushes.Black, bounds.X + 5, bounds.Y + bounds.Height + 2);
    statsFont.Dispose();
  }

  private void DrawTooltip(Graphics g, string text, Point mousePosition)
  {
    if (string.IsNullOrEmpty(text)) return;

    var font = new Font("Arial", 10, FontStyle.Bold);
    var textSize = g.MeasureString(text, font);

    // Position tooltip near mouse but ensure it stays within bounds
    var tooltipX = mousePosition.X + 10;
    var tooltipY = mousePosition.Y - 30;

    var tooltipRect = new Rectangle(
        tooltipX - 5,
        tooltipY - 3,
        (int)textSize.Width + 10,
        (int)textSize.Height + 6);

    // Draw tooltip background with shadow
    var shadowRect = new Rectangle(tooltipRect.X + 2, tooltipRect.Y + 2,
        tooltipRect.Width, tooltipRect.Height);
    g.FillRectangle(Brushes.Gray, shadowRect);

    g.FillRectangle(Brushes.LightYellow, tooltipRect);
    g.DrawRectangle(Pens.Black, tooltipRect);

    // Draw text
    g.DrawString(text, font, Brushes.Black, tooltipX, tooltipY);

    font.Dispose();
  }

  private Color[] GetLapColors()
  {
    return new Color[]
    {
            Color.FromArgb(70, 130, 180),   // Steel Blue
            Color.FromArgb(255, 165, 0),    // Orange
            Color.FromArgb(50, 205, 50),    // Lime Green
            Color.FromArgb(255, 69, 0),     // Red Orange
            Color.FromArgb(138, 43, 226),   // Blue Violet
            Color.FromArgb(255, 215, 0),    // Gold
            Color.FromArgb(220, 20, 60),    // Crimson
            Color.FromArgb(0, 191, 255),    // Deep Sky Blue
            Color.FromArgb(154, 205, 50),   // Yellow Green
            Color.FromArgb(255, 20, 147)    // Deep Pink
    };
  }

  private Brush GetPositionBrush(int position)
  {
    return position switch
    {
      0 => new SolidBrush(Color.Gold),
      1 => new SolidBrush(Color.Silver),
      2 => new SolidBrush(Color.FromArgb(205, 127, 50)), // Bronze
      _ => new SolidBrush(Color.DarkGray)
    };
  }

  private double CalculateExtendedChartDuration(double raceDurationMs)
  {
    // Extend the chart duration beyond race time to show predicted finishes
    // Add approximately 25% more time or at least 5 minutes, whichever is greater
    var extensionMs = Math.Max(raceDurationMs * 0.25, 5 * 60 * 1000); // 25% or 5 minutes minimum
    return raceDurationMs + extensionMs;
  }

  private int CalculateMaxLapsForRaceCompletion(RiderInfo rider)
  {
    // If race has finished and this rider has a final allowed lap, use that
    if (rider.FinalAllowedLap != int.MaxValue)
    {
      return rider.FinalAllowedLap;
    }

    // If race is still ongoing or no specific limit set, allow reasonable prediction
    // Limit to current laps + a reasonable number of additional laps (e.g., 10)
    return rider.TotalLaps + 10;
  }
}

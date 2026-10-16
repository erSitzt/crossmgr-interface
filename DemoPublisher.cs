namespace CrossMgrInterface;

/// <summary>
/// The website a demo publishes to: none. It says what would have gone up and
/// that it went, so the demo shows the whole day working - the outbox, the
/// overall waiting for its motos, a correction going out again - without a
/// single fictional rider reaching the club's real site.
/// </summary>
public sealed class DemoPublisher : IResultsPublisher
{
  private const string Site = "https://results.example.invalid";
  private readonly Action<string> _say;

  public DemoPublisher(Action<string> say) => _say = say;

  public bool IsConfigured => true;

  public Task<PublishOutcome> PublishAsync(PublishedSession session, IProgress<PublishProgress>? progress,
    CancellationToken cancellationToken)
  {
    _say($"🎬 Demo: '{session.Session.Title}' would now go up on the results website " +
         $"({session.Entries.Count} riders). Nothing is sent in a demo.");
    return Task.FromResult(new PublishOutcome(PublishStatus.Published, "Published.", $"{Site}/races/demo"));
  }

  public Task<PublishOutcome> PublishOverallAsync(PublishedOverall overall, IProgress<PublishProgress>? progress,
    CancellationToken cancellationToken)
  {
    _say($"🎬 Demo: the overall '{overall.Overall.Title}' would now go up on the results website.");
    return Task.FromResult(new PublishOutcome(PublishStatus.Published, "Published.", $"{Site}/overall/demo"));
  }

  public Task<PublishOutcome> PublishPlanAsync(PublishedPlan plan, CancellationToken cancellationToken) =>
    Task.FromResult(new PublishOutcome(PublishStatus.Published, "Published.", $"{Site}/live"));

  public Task<PublishOutcome> TestAsync(CancellationToken cancellationToken) =>
    Task.FromResult(new PublishOutcome(PublishStatus.Published, "The demo has no website."));
}

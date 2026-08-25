namespace CrossMgrInterface;

/// <summary>
/// A line in the Tag Events list that carries the transponder it refers to.
///
/// The tag used to be recovered by pulling the substring between "Tag: " and the
/// first " (" in the rendered text - which landed on the lap-time parenthesis, so
/// it only ever returned a usable tag on a rider's very first lap. Keeping the
/// value alongside the text removes the guesswork.
/// </summary>
public sealed class TagEventItem
{
  public TagEventItem(string text, string? tagId = null)
  {
    Text = text;
    TagId = tagId;
  }

  public string Text { get; }
  public string? TagId { get; }

  public override string ToString() => Text;
}

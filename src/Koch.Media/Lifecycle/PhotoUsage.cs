using System.Globalization;
using Koch.Recipes.Events;

namespace Koch.Media.Lifecycle;

/// <summary>
/// Whether a recipe shows a photo, kept as blob index tags on each of its renditions: <c>state</c>, <c>seq</c> (the
/// sequence of the event that set it) and <c>since</c> (that event's day, UTC). A rendition starts as
/// <see cref="New"/> with sequence 0; an event changes the tags only if its sequence is higher than the stored one, so
/// late and repeated events can't undo newer ones (DDIA ch9 p.375-376, fencing tokens; docs/adr/0008).
/// </summary>
public sealed record PhotoUsage(string State, string Sequence, DateOnly Since)
{
    public const string New = "new", Attached = "attached", Detached = "detached";

    /// <summary>How long a photo no recipe shows is kept: long enough to undo, and for a phone that was offline.</summary>
    public const int GraceDays = 30;

    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>
    /// Just made, not yet attached: never deleted by the sweep. A photo whose recipe was saved offline for weeks must
    /// survive; an abandoned upload costs a few hundred kilobytes.
    /// </summary>
    public static PhotoUsage Fresh(DateOnly today) => new(New, PhotoEvents.Sequence(0), today);

    public IDictionary<string, string> ToTags() => new Dictionary<string, string>
    {
        ["state"] = State,
        ["seq"] = Sequence,
        ["since"] = Since.ToString(DateFormat, CultureInfo.InvariantCulture),
    };

    /// <summary>The condition for changing a rendition's tags: what it has is older. Tags compare as strings.</summary>
    public string IfNewer => $"\"seq\" < '{Sequence}'";

    /// <summary>Renditions detached before <paramref name="day"/>; for finding them and as the condition for deleting.</summary>
    public static string DetachedBefore(DateOnly day) =>
        $"\"state\" = '{Detached}' AND \"since\" < '{day.ToString(DateFormat, CultureInfo.InvariantCulture)}'";
}

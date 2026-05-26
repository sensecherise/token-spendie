namespace TokenSpendie.Windows.Models;

public record UsageSnapshot(
    UsageWindow Session,
    UsageWindow Weekly,
    IReadOnlyList<ModelWeekly> ModelWeeklies,
    DateTimeOffset FetchedAt)
{
    public virtual bool Equals(UsageSnapshot? other) =>
        other is not null
        && Session == other.Session
        && Weekly == other.Weekly
        && ModelWeeklies.SequenceEqual(other.ModelWeeklies)
        && FetchedAt == other.FetchedAt;

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Session);
        hash.Add(Weekly);
        foreach (var model in ModelWeeklies) hash.Add(model);
        hash.Add(FetchedAt);
        return hash.ToHashCode();
    }
}

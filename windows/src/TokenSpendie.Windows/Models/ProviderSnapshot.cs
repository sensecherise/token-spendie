namespace TokenSpendie.Windows.Models;

public record ProviderSnapshot(
    ProviderID Id,
    string? Plan,
    LabeledWindow Headline,
    IReadOnlyList<LabeledWindow> Windows,
    DateTimeOffset FetchedAt,
    string? Note = null)
{
    public virtual bool Equals(ProviderSnapshot? other) =>
        other is not null
        && Id == other.Id
        && Plan == other.Plan
        && Headline == other.Headline
        && Windows.SequenceEqual(other.Windows)
        && FetchedAt == other.FetchedAt
        && Note == other.Note;

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id); hash.Add(Plan); hash.Add(Headline);
        foreach (var window in Windows) hash.Add(window);
        hash.Add(FetchedAt); hash.Add(Note);
        return hash.ToHashCode();
    }
}

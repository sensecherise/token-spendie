namespace TokenSpendie.Windows.Models;

public abstract record LoadState
{
    public sealed record LoadingState : LoadState;
    public sealed record OkState : LoadState;
    public sealed record StaleState : LoadState;
    public sealed record ErrorState(UsageErrorKind Kind) : LoadState;

    public static readonly LoadState Loading = new LoadingState();
    public static readonly LoadState Ok = new OkState();
    public static readonly LoadState Stale = new StaleState();
    public static LoadState Error(UsageErrorKind kind) => new ErrorState(kind);
}

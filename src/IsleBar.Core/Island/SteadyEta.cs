namespace IsleBar.Core.Island;

/// <summary>
/// Time left, shown only once it has been there for <see cref="Settle"/> without a break (TODO ⑤ 掀起卡片适配, user 10-11): a
/// packer's estimate jumps about in its first seconds. Till then the card says only the speed.
/// </summary>
public sealed class SteadyEta
{
    public static readonly TimeSpan Settle = TimeSpan.FromSeconds(3);

    private DateTimeOffset? _since;

    /// <summary>Takes this second's estimate (null = none now); returns it once settled, else null.</summary>
    public TimeSpan? See(TimeSpan? left, DateTimeOffset now)
    {
        if (left is not { } l || l < TimeSpan.Zero)
        {
            _since = null;
            return null;
        }

        _since ??= now;
        return now - _since.Value >= Settle ? l : null;
    }

    /// <summary>Bytes still to come at this speed; null when either isn't known.</summary>
    public static TimeSpan? From(long? left, double? speed)
        => left is { } bytes and >= 0 && speed is > 0 ? TimeSpan.FromSeconds(bytes / speed.Value) : null;
}

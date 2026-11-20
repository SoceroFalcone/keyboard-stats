namespace KeyTrail.Core;

// History retains these small rows instead of every day's key/hour/minute collections.
public sealed record DaySummary(DateOnly Date, long Total, int UsedKeys, int ActiveMinutes, string? FavoriteKey, long MouseTotal = 0)
{
    public long KeyboardTotal => Total - MouseTotal;
    public static DaySummary From(DateOnly date, DayStats stats) => new(date, stats.Total, stats.UsedKeys,
        stats.ActiveMinutes.Count, stats.Keys.Where(x => !MouseCatalog.IsMouse(x.Key)).OrderByDescending(x => x.Value).ThenBy(x => x.Key).FirstOrDefault().Key, stats.MouseTotal);
}

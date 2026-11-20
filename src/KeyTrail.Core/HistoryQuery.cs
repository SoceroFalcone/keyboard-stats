namespace KeyTrail.Core;

public enum InputKind { All, Keyboard, Mouse }
public enum HistoryRange { All, Today, Yesterday, Last7Days, Last30Days, Custom }

// Inclusive local dates, shared by the history list and its CSV export.
public readonly record struct HistoryQuery(InputKind Device, DateOnly? Start, DateOnly End)
{
    public long Count(DaySummary day) => Device switch
    {
        InputKind.Keyboard => day.KeyboardTotal,
        InputKind.Mouse => day.MouseTotal,
        _ => day.Total
    };

    public bool Includes(DaySummary day) => day.Date <= End && (Start is null || day.Date >= Start) && Count(day) > 0;
    public static bool IncludesKey(InputKind device, string key) => device == InputKind.All ||
        (device == InputKind.Mouse) == MouseCatalog.IsMouse(key);

    public static HistoryQuery Create(HistoryRange range, InputKind device, DateOnly today,
        DateOnly? customStart = null, DateOnly? customEnd = null)
    {
        if (range == HistoryRange.Custom)
        {
            if (customStart is null || customEnd is null || customStart > customEnd || customEnd > today)
                throw new ArgumentException("请选择完整的日期范围，开始日期不能晚于结束日期，结束日期不能晚于今天。");
            return new(device, customStart, customEnd.Value);
        }
        return range switch
        {
            HistoryRange.Today => new(device, today, today),
            HistoryRange.Yesterday => new(device, today.AddDays(-1), today.AddDays(-1)),
            HistoryRange.Last7Days => new(device, today.AddDays(-6), today),
            HistoryRange.Last30Days => new(device, today.AddDays(-29), today),
            _ => new(device, null, today)
        };
    }
}

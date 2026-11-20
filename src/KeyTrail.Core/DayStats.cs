using System.Globalization;
using System.Text.Json.Serialization;

namespace KeyTrail.Core;

public sealed class DayStats
{
    public int SchemaVersion { get; set; } = 1;
    public string Date { get; set; } = "";
    public Dictionary<string, long> Keys { get; set; } = new(StringComparer.Ordinal);
    public long[] Hours { get; set; } = new long[24];
    public HashSet<int> ActiveMinutes { get; set; } = new();
    [JsonIgnore] public long Total => Keys.Values.Sum();
    [JsonIgnore] public long MouseTotal => Keys.Where(x => MouseCatalog.IsMouse(x.Key)).Sum(x => x.Value);
    [JsonIgnore] public long KeyboardTotal => Keys.Where(x => !MouseCatalog.IsMouse(x.Key)).Sum(x => x.Value);
    [JsonIgnore] public int UsedKeyboardKeys => Keys.Count(x => x.Value > 0 && !MouseCatalog.IsMouse(x.Key));
    [JsonIgnore] public int UsedKeys => Keys.Count(x => x.Value > 0);

    public static DayStats Empty(DateOnly date) => new() { Date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
    public DayStats Copy() => new()
    {
        SchemaVersion = SchemaVersion, Date = Date,
        Keys = new(Keys, StringComparer.Ordinal), Hours = (long[])Hours.Clone(), ActiveMinutes = new(ActiveMinutes)
    };

    public void Add(string key, DateTime time)
    {
        Keys[key] = Keys.GetValueOrDefault(key) + 1;
        Hours[time.Hour]++;
        ActiveMinutes.Add(time.Hour * 60 + time.Minute);
    }

    public void Merge(DayStats delta)
    {
        if (Date != delta.Date) throw new ArgumentException("Cannot merge different calendar days.");
        foreach (var (key, value) in delta.Keys) Keys[key] = checked(Keys.GetValueOrDefault(key) + value);
        for (var i = 0; i < 24; i++) Hours[i] = checked(Hours[i] + delta.Hours[i]);
        ActiveMinutes.UnionWith(delta.ActiveMinutes);
    }

    public void Validate(DateOnly expected)
    {
        if (SchemaVersion != 1 || Date != expected.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            || Keys is null || Hours is null || Hours.Length != 24 || ActiveMinutes is null
            || Keys.Any(x => string.IsNullOrWhiteSpace(x.Key) || x.Key.Length > 64 || x.Value < 0)
            || Hours.Any(x => x < 0) || ActiveMinutes.Any(x => x is < 0 or > 1439)
            || Keys.Values.Sum() != Hours.Sum())
            throw new InvalidDataException("统计文件的格式或校验不正确。");
    }
}

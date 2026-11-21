using System.Globalization;
using System.Text;
using System.Text.Json;

namespace KeyTrail.Core;

public sealed class StatsStore
{
    private readonly object _gate = new();
    private readonly object _writeGate = new();
    private readonly Dictionary<DateOnly, DayStats> _cache = new();
    private readonly Dictionary<DateOnly, long> _versions = new();
    private readonly HashSet<DateOnly> _dirty = new();
    private readonly Dictionary<DateOnly, long> _access = new();
    private readonly Dictionary<DateOnly, DaySummary> _summaries = new();
    private readonly HashSet<DateOnly> _knownDates = new();
    private IReadOnlyList<DateOnly>? _sortedDates;
    private long _accessClock;
    private long _revision;
    private readonly int _cacheLimit;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public string DirectoryPath { get; }
    public string? RecoveryNotice { get; private set; }
    public long Revision => Interlocked.Read(ref _revision);
    public int CachedDayCount { get { lock (_gate) return _cache.Count; } }

    public StatsStore(string directory, int cacheLimit = 32)
    {
        if (cacheLimit < 1) throw new ArgumentOutOfRangeException(nameof(cacheLimit));
        _cacheLimit = cacheLimit;
        DirectoryPath = Path.GetFullPath(directory);
        Directory.CreateDirectory(DirectoryPath);
        foreach (var path in Directory.EnumerateFiles(DirectoryPath, "*.json*"))
        {
            var name = Path.GetFileName(path);
            if (name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
            if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                && DateOnly.TryParseExact(name.AsSpan(0, name.Length - 5), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                _knownDates.Add(date);
        }
    }

    public DayStats GetDay(DateOnly date) { lock (_gate) return Load(date).Copy(); }
    public DaySummary GetSummary(DateOnly date)
    {
        lock (_gate)
        {
            if (_summaries.TryGetValue(date, out var summary)) return summary;
            var loaded = DaySummary.From(date, Load(date));
            if (loaded.Total > 0) _summaries[date] = loaded;
            return loaded;
        }
    }
    public IReadOnlyList<DaySummary> GetSummaries(CancellationToken cancellationToken = default)
    {
        var result = new List<DaySummary>();
        foreach (var date in GetDates())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var summary = GetSummary(date);
            if (summary.Total > 0) result.Add(summary);
        }
        return result;
    }
    public void ReleaseReadCache()
    {
        lock (_gate)
        {
            foreach (var date in _cache.Keys.Where(d => !_dirty.Contains(d)).ToArray()) Evict(date);
            _summaries.Clear();
        }
    }
    private string FileFor(DateOnly date) => Path.Combine(DirectoryPath, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".json");

    private DayStats Load(DateOnly date)
    {
        if (_cache.TryGetValue(date, out var cached)) { _access[date] = ++_accessClock; return cached; }
        var path = FileFor(date);
        if (!File.Exists(path) && !File.Exists(path + ".bak")) return Remember(date, DayStats.Empty(date));
        try { return Remember(date, ReadValid(path, date)); }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or OverflowException)
        {
            try
            {
                var recovered = ReadValid(path + ".bak", date);
                var hadPrimary = File.Exists(path);
                if (hadPrimary) File.Copy(path, path + ".corrupt-" + Guid.NewGuid().ToString("N"), false);
                // Restore the last verified backup without replacing it with the damaged primary.
                AtomicWrite(path, JsonSerializer.Serialize(recovered, JsonOptions), false);
                RecoveryNotice = hadPrimary
                    ? $"{date:yyyy-MM-dd} 的统计已从备份恢复，损坏的原文件已保留。"
                    : $"{date:yyyy-MM-dd} 的统计文件缺失，已从备份恢复。";
                return Remember(date, recovered);
            }
            catch (Exception backupEx) when (backupEx is IOException or JsonException or InvalidDataException or OverflowException)
            {
                throw new InvalidDataException($"无法读取 {date:yyyy-MM-dd} 的统计文件，已保留原文件。请从备份恢复，或将该日文件移出数据目录后重新启动。", ex);
            }
        }
    }

    private DayStats Remember(DateOnly date, DayStats stats)
    {
        _cache[date] = stats;
        _access[date] = ++_accessClock;
        TrimCache();
        return stats;
    }
    private void TrimCache()
    {
        while (_cache.Count > _cacheLimit)
        {
            var candidate = _access.Where(x => !_dirty.Contains(x.Key)).OrderBy(x => x.Value).Select(x => (DateOnly?)x.Key).FirstOrDefault();
            if (candidate is null) break; // Never evict an unsaved day, even when a large batch exceeds the limit.
            Evict(candidate.Value);
        }
    }
    private void Evict(DateOnly date) { _cache.Remove(date); _access.Remove(date); _versions.Remove(date); }

    private static DayStats ReadValid(string path, DateOnly date)
    {
        var day = JsonSerializer.Deserialize<DayStats>(File.ReadAllText(path)) ?? throw new InvalidDataException("统计文件为空。");
        day.Validate(date);
        return day;
    }

    public void Merge(IEnumerable<DayStats> batches)
    {
        lock (_gate)
        {
            // Load every affected day before mutating any so a failure cannot partially merge a batch.
            var loadedDays = new Dictionary<DateOnly, DayStats>();
            var batch = batches.Select(day =>
            {
                var date = DateOnly.ParseExact(day.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (!loadedDays.TryGetValue(date, out var current)) loadedDays[date] = current = Load(date);
                return (Date: date, Delta: day, Current: current);
            }).ToArray();
            foreach (var (date, delta, current) in batch)
            {
                current.Merge(delta);
                _cache[date] = current;
                _access[date] = ++_accessClock;
                _versions[date] = _versions.GetValueOrDefault(date) + 1;
                _dirty.Add(date);
                _summaries.Remove(date);
                if (_knownDates.Add(date)) _sortedDates = null;
            }
            if (batch.Length > 0) Interlocked.Increment(ref _revision);
            TrimCache();
        }
    }

    public void Flush()
    {
        lock (_writeGate)
        {
            (DateOnly Date, DayStats Stats, long Version)[] snapshots;
            lock (_gate)
            {
                if (_dirty.Count == 0) return;
                snapshots = _dirty.Select(d => (d, _cache[d].Copy(), _versions[d])).ToArray();
            }
            foreach (var entry in snapshots)
            {
                AtomicWrite(FileFor(entry.Date), JsonSerializer.Serialize(entry.Stats, JsonOptions), true);
                lock (_gate) if (_versions[entry.Date] == entry.Version) _dirty.Remove(entry.Date);
            }
            lock (_gate) TrimCache();
        }
    }

    public IReadOnlyList<DateOnly> GetDates()
    {
        lock (_gate) return _sortedDates ??= Array.AsReadOnly(_knownDates.OrderDescending().ToArray());
    }

    public static void AtomicWrite(string path, string contents, bool backup)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        // The first successful save must have a backup even if no second save is needed.
        if (backup && !File.Exists(path) && !File.Exists(path + ".bak"))
            AtomicWrite(path + ".bak", contents, false);
        var temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
            writer.Write(contents);
            writer.Flush();
            stream.Flush(true);
        }
        if (File.Exists(path)) File.Replace(temp, path, backup ? path + ".bak" : null);
        else File.Move(temp, path);
    }

    public static string ToCsv(IEnumerable<DayStats> days)
    {
        var text = new StringBuilder("日期,按键,按键名称,分类,次数\r\n");
        foreach (var day in days.OrderBy(x => x.Date))
            foreach (var (id, count) in day.Keys.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.Ordinal))
            {
                var key = KeyCatalog.Get(id);
                text.AppendLine(string.Join(',', Csv(day.Date), Csv(id), Csv(key.Name), Csv(key.Category), count.ToString(CultureInfo.InvariantCulture)));
            }
        return text.ToString();
    }

    public void ExportCsv(string path, IEnumerable<DateOnly> dates, InputKind device = InputKind.All)
    {
        var temporary = path + ".keytrail-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using var writer = new StreamWriter(stream, new UTF8Encoding(true), leaveOpen: true);
                writer.WriteLine("日期,按键,按键名称,分类,次数");
                foreach (var date in dates.Order())
                {
                    var day = GetDay(date);
                    foreach (var (id, count) in day.Keys.Where(x => HistoryQuery.IncludesKey(device, x.Key)).OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.Ordinal))
                    {
                        var key = KeyCatalog.Get(id);
                        writer.WriteLine(string.Join(',', Csv(day.Date), Csv(id), Csv(key.Name), Csv(key.Category), count.ToString(CultureInfo.InvariantCulture)));
                    }
                }
                writer.Flush(); stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    // Quoting alone does not prevent spreadsheet formula interpretation.
    private static string Csv(string value)
    {
        if (value.Length > 0 && "=+-@\t\r\n".Contains(value[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}

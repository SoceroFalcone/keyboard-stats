using System.Runtime.InteropServices;
using System.Text.Json;
using KeyTrail.Desktop.Native;

var testRoot = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--")) ?? "artifacts/test-data");
Directory.CreateDirectory(testRoot);
var today = new DateTime(2026, 11, 14, 9, 23, 0, DateTimeKind.Local);
var date = DateOnly.FromDateTime(today);
var passed = 0;
var failures = new List<string>();
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
void Test(string name, Action action)
{
    try { action(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures.Add(name + ": " + ex.Message); Console.WriteLine("FAIL " + name + ": " + ex.Message); }
}
DayStats Batch(int count, DateOnly? forDate = null, string key = "A")
{
    var day = DayStats.Empty(forDate ?? date);
    for (var i = 0; i < count; i++) day.Add(key, (forDate ?? date).ToDateTime(new TimeOnly(9, 23)));
    return day;
}
string Fresh(string name) => Path.Combine(testRoot, name + "-" + Guid.NewGuid().ToString("N"));

Test("Physical press excludes held repeats, counts releases and both modifier keys", () =>
{
    var accumulator = new KeyAccumulator();
    accumulator.Handle(65, 30, false, true, false, today);
    accumulator.Handle(65, 30, false, true, false, today);
    accumulator.Handle(65, 30, false, false, false, today);
    accumulator.Handle(65, 30, false, true, false, today);
    accumulator.Handle(0x11, 29, false, true, false, today);
    accumulator.Handle(0x11, 29, true, true, false, today);
    var day = accumulator.Drain().Single();
    Check(day.Total == 4 && day.Keys["A"] == 2 && day.Keys["LControl"] == 1 && day.Keys["RControl"] == 1, "Press counts or modifiers differ");
    Check(day.ActiveMinutes.Count == 1 && day.Hours[9] == 4, "Minute or hour aggregate differs");
    Check(accumulator.Drain().Count == 0, "Drain must be exactly once");
});
Test("Repeat preference counts each key-down", () =>
{
    var a = new KeyAccumulator { CountRepeats = true };
    for (var i = 0; i < 5; i++) a.Handle(65, 30, false, true, false, today);
    Check(a.Drain().Single().Total == 5, "Held input not counted");
});
Test("Pause tracks held state and does not count paused or synthetic input", () =>
{
    var a = new KeyAccumulator { Paused = true };
    a.Handle(65, 30, false, true, false, today);
    a.Paused = false;
    a.Handle(65, 30, false, true, false, today);
    a.Handle(65, 30, false, false, false, today);
    a.Handle(66, 48, false, true, true, today);
    a.Handle(65, 30, false, true, false, today);
    Check(a.Drain().Single().Total == 1, "Pause or injected filter leaked input");
});
Test("Local midnight splits daily records without restarting", () =>
{
    var a = new KeyAccumulator { CountRepeats = true };
    a.Handle(65, 30, false, true, false, new DateTime(2026, 11, 14, 23, 59, 59));
    a.Handle(65, 30, false, true, false, new DateTime(2026, 11, 15, 0, 0, 0));
    var days = a.Drain().OrderBy(d => d.Date).ToArray();
    Check(days.Length == 2 && days[0].Hours[23] == 1 && days[1].Hours[0] == 1, "Midnight boundary wrong");
});
Test("Keypad, right Shift and extended Enter resolve distinctly", () =>
{
    Check(KeyCatalog.Resolve(0x10, 0x36, false) == "RShift", "RShift");
    Check(KeyCatalog.Resolve(0x0D, 28, true) == "NumEnter", "Keypad Enter");
    Check(KeyCatalog.Resolve(0x26, 0x48, false) == "Num8", "NumLock-off keypad");
    Check(KeyCatalog.Resolve(0x26, 0x48, true) == "Up", "Navigation arrow");
    Check(KeyCatalog.Resolve(0x0C, 0x4C, false) == "Num5", "NumLock-off keypad 5");
    Check(KeyCatalog.Resolve(0xFF, 0, false) is null, "Invalid VK must be ignored");
});
Test("Pause key without a release never gets permanently held", () =>
{
    var a = new KeyAccumulator();
    a.Handle(0x13, 0x45, false, true, false, today);
    a.Handle(0x13, 0x45, false, true, false, today);
    Check(a.Drain().Single().Keys["Pause"] == 2, "Pause key stuck");
});
Test("Save/restart preserves aggregates; snapshots cannot mutate live data", () =>
{
    var path = Fresh("restart"); var s = new StatsStore(path);
    s.Merge(new[] { Batch(20) }); s.Flush();
    var copy = s.GetDay(date); copy.Keys["A"] = 999;
    s.Merge(new[] { Batch(3) }); s.Flush();
    var reloaded = new StatsStore(path).GetDay(date);
    Check(reloaded.Total == 23 && reloaded.Hours[9] == 23, "Saved value lost or snapshot mutated store");
    reloaded.Validate(date);
    Check(!File.ReadAllText(Path.Combine(path, "2026-11-14.json")).Contains("scanCode"), "Unexpected raw data");
});
Test("Concurrent merging and flushing never loses increments", () =>
{
    var s = new StatsStore(Fresh("concurrency"));
    var writers = Enumerable.Range(0, 4).Select(_ => Task.Run(() => { for (var i = 0; i < 100; i++) s.Merge(new[] { Batch(10) }); })).ToArray();
    var saver = Task.Run(() => { for (var i = 0; i < 20; i++) s.Flush(); });
    Task.WaitAll(writers.Append(saver).ToArray()); s.Flush();
    Check(new StatsStore(s.DirectoryPath).GetDay(date).Total == 4000, "Lost update during concurrent save");
});
Test("Damaged primary recovers validated backup and preserves corrupt original", () =>
{
    var path = Fresh("recover"); var s = new StatsStore(path);
    s.Merge(new[] { Batch(5) }); s.Flush(); s.Merge(new[] { Batch(2) }); s.Flush();
    File.WriteAllText(Path.Combine(path, "2026-11-14.json"), "{broken");
    var recovered = new StatsStore(path);
    Check(recovered.GetDay(date).Total == 5 && recovered.RecoveryNotice is not null, "Backup not restored visibly");
    Check(Directory.GetFiles(path, "*.corrupt-*").Length == 1, "Corrupt original not retained");
    Check(new StatsStore(path).GetDay(date).Total == 5, "Recovered primary not valid");
});
Test("Invalid primary without backup is never overwritten", () =>
{
    var path = Fresh("unrecoverable"); Directory.CreateDirectory(path);
    var file = Path.Combine(path, "2026-11-14.json"); File.WriteAllText(file, "corrupt evidence");
    var s = new StatsStore(path); var threw = false;
    try { s.Merge(new[] { Batch(10) }); } catch (InvalidDataException) { threw = true; }
    Check(threw && File.ReadAllText(file) == "corrupt evidence", "Invalid data silently overwritten");
});
Test("CSV includes stable names and neutralizes formula-like key labels", () =>
{
    var day = Batch(3, key: "Equal"); day.Merge(Batch(2, key: "NumAdd"));
    var csv = StatsStore.ToCsv(new[] { day });
    Check(csv.StartsWith("日期,按键,按键名称,分类,次数"), "Missing CSV header");
    Check(csv.Contains("\"'=\""), "Equals label could be interpreted as a formula");
    Check(csv.Contains("\"2026-11-14\""), "Missing calendar date");
});
Test("Validation rejects inconsistent totals and unsupported schemas", () =>
{
    var day = Batch(4); day.Hours[9] = 3; var threw = false;
    try { day.Validate(date); } catch (InvalidDataException) { threw = true; }
    Check(threw, "Inconsistent aggregates accepted");
    day = Batch(2); day.SchemaVersion = 99; threw = false;
    try { day.Validate(date); } catch (InvalidDataException) { threw = true; }
    Check(threw, "Unsupported schema accepted");
});

Test("Bounded cache preserves all dates, summaries and dirty data", () =>
{
    var s = new StatsStore(Fresh("bounded-cache"), cacheLimit: 4);
    var dates = Enumerable.Range(0, 80).Select(i => date.AddDays(-i)).ToArray();
    s.Merge(dates.Select((d, i) => Batch(i + 1, d))); s.Flush();
    Check(s.CachedDayCount <= 4, "Saved days were retained past cache capacity");
    var rows = s.GetSummaries();
    Check(rows.Count == 80 && rows.Sum(x => x.Total) == 3240, "History was lost after eviction");
    Check(s.CachedDayCount <= 4, "Reading history expanded full-data cache");
    var revision = s.Revision;
    s.GetDay(date.AddYears(-2)); s.GetSummary(date);
    Check(s.Revision == revision, "Reading statistics incorrectly marks them changed");
    s.Merge(new[] { Batch(3) }); s.ReleaseReadCache();
    Check(s.GetDay(date).Total == 4, "Releasing read cache lost unflushed counts");
    Check(s.GetSummary(date).Total == 4, "Summary remained stale after new input");
    s.Flush(); Check(new StatsStore(s.DirectoryPath).GetDay(date).Total == 4, "Dirty counts failed to persist");
});
Test("Repeated dates in a batch survive intervening cache evictions", () =>
{
    var s = new StatsStore(Fresh("batch-eviction"), cacheLimit: 1);
    s.Merge(new[] { Batch(2), Batch(5, date.AddDays(-1)), Batch(3) }); s.Flush();
    var reload = new StatsStore(s.DirectoryPath, cacheLimit: 1);
    Check(reload.GetDay(date).Total == 5 && reload.GetDay(date.AddDays(-1)).Total == 5, "A repeated day lost a delta");
});
Test("Streaming CSV matches aggregate export without retaining all full days", () =>
{
    var s = new StatsStore(Fresh("stream-export"), cacheLimit: 3);
    var days = Enumerable.Range(0, 25).Select(i => Batch(i + 1, date.AddDays(-i), i % 2 == 0 ? "Equal" : "A")).ToArray();
    s.Merge(days); s.Flush();
    var output = Path.Combine(s.DirectoryPath, "export.csv"); File.WriteAllText(output, "previous export");
    s.ExportCsv(output, s.GetDates());
    Check(File.ReadAllText(output) == StatsStore.ToCsv(days), "Streaming CSV differs");
    Check(File.ReadAllBytes(output).Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }), "CSV lacks Excel-compatible UTF-8 BOM");
    Check(s.CachedDayCount <= 3 && Directory.GetFiles(s.DirectoryPath, "*.tmp").Length == 0, "Export leaked cache or temporary files");
});

Test("All five mouse buttons count presses, double clicks count twice and holds never repeat", () =>
{
    var a = new KeyAccumulator { CountRepeats = true };
    foreach (var (down, up, data) in new (int, int, uint)[] { (0x201, 0x202, 0), (0x204, 0x205, 0), (0x207, 0x208, 0), (0x20B, 0x20C, 0x10000), (0x20B, 0x20C, 0x20000) })
    {
        a.HandleMouse(down, data, false, today); a.HandleMouse(down, data, false, today);
        a.HandleMouse(up, data, false, today);
    }
    a.HandleMouse(0x201, 0, false, today); a.HandleMouse(0x202, 0, false, today);
    a.Handle(65, 30, false, true, false, today);
    var day = a.Drain().Single();
    Check(day.Keys["MouseLeft"] == 2 && day.Keys["MouseRight"] == 1 && day.Keys["MouseMiddle"] == 1 && day.Keys["MouseX1"] == 1 && day.Keys["MouseX2"] == 1, "Mouse button mapping or double-click count differs");
    Check(day.MouseTotal == 6 && day.KeyboardTotal == 1 && day.Total == 7 && day.Hours[9] == 7 && day.ActiveMinutes.Count == 1, "Mouse/keyboard breakdown or active-minute union differs");
});
Test("Mouse pause, injection filtering, motion exclusion, release and midnight boundaries are independent", () =>
{
    var a = new KeyAccumulator { Paused = true };
    a.HandleMouse(0x201, 0, false, today); a.Paused = false;
    a.HandleMouse(0x201, 0, false, today); a.HandleMouse(0x202, 0, false, today);
    a.HandleMouse(0x201, 0, true, today); a.HandleMouse(0x202, 0, true, today);
    foreach (var message in new[] { 0x200, 0x20A, 0x20E, 0x203, 0x999 }) a.HandleMouse(message, 0, false, today);
    a.HandleMouse(0x20B, 0x30000, false, today);
    Check(a.Drain().Count == 0, "Pause, injected input, motion, wheel or unknown button leaked into counts");
    a.HandleMouse(0x201, 0, false, today.Date.AddHours(23).AddMinutes(59).AddSeconds(59));
    a.HandleMouse(0x202, 0, false, today.Date.AddDays(1));
    a.HandleMouse(0x201, 0, false, today.Date.AddDays(1));
    var days = a.Drain().OrderBy(d => d.Date).ToArray();
    Check(days.Length == 2 && days[0].MouseTotal == 1 && days[1].MouseTotal == 1, "Mouse midnight boundary is incorrect");
    a.ResetPressed(); a.HandleMouse(0x201, 0, false, today);
    Check(a.Drain().Single().MouseTotal == 1, "Reset did not clear mouse held state");
});
Test("Existing keyboard records accept mouse counts without changing schema and export named buttons", () =>
{
    var path = Fresh("mouse-legacy"); var legacy = new StatsStore(path);
    legacy.Merge(new[] { Batch(3) }); legacy.Flush();
    var updated = new StatsStore(path); updated.Merge(new[] { Batch(2, key: "MouseX2") }); updated.Flush();
    var reloaded = new StatsStore(path); var day = reloaded.GetDay(date); day.Validate(date);
    var summary = reloaded.GetSummary(date);
    Check(day.SchemaVersion == 1 && day.KeyboardTotal == 3 && day.MouseTotal == 2 && day.Total == 5, "Existing counts or schema changed");
    Check(summary.KeyboardTotal == 3 && summary.MouseTotal == 2 && summary.Total == 5 && summary.FavoriteKey == "A", "History breakdown differs");
    var csv = StatsStore.ToCsv(new[] { day });
    Check(csv.Contains("鼠标侧键 2") && csv.Contains("鼠标按键") && csv.Contains("\"A\""), "CSV omits the named mouse or keyboard record");
});
Test("Native mouse packets decode buttons and reject injected flags without storing positions", () =>
{
    var a = new KeyAccumulator(); using var monitor = new InputMonitor(a);
    monitor.ProcessMouseMessage(-1, 0x201, IntPtr.Zero);
    monitor.ProcessMouseMessage(0, 0x200, IntPtr.Zero); // Must return before dereferencing high-frequency motion.
    monitor.ProcessMouseMessage(0, 0x20A, IntPtr.Zero);
    foreach (var (down, up, data) in new (int, int, uint)[] { (0x201, 0x202, 0), (0x204, 0x205, 0), (0x207, 0x208, 0), (0x20B, 0x20C, 0x10000), (0x20B, 0x20C, 0x20000) })
    {
        NativeInput.DeliverMousePacket(monitor, down, data, 0); NativeInput.DeliverMousePacket(monitor, up, data, 0);
    }
    NativeInput.DeliverMousePacket(monitor, 0x201, 0, 1); NativeInput.DeliverMousePacket(monitor, 0x202, 0, 1);
    NativeInput.DeliverMousePacket(monitor, 0x201, 0, 2); NativeInput.DeliverMousePacket(monitor, 0x202, 0, 2);
    var day = a.Drain().Single();
    Check(day.MouseTotal == 5 && day.Keys.Count == 5 && monitor.LastError is null, "Native packet parsing or injected flags are incorrect");
    var json = JsonSerializer.Serialize(day);
    Check(!json.Contains("54321") && !json.Contains("ExtraInfo"), "Position or raw native packet was persisted");
});

Test("History presets are inclusive across year boundaries and leap day", () =>
{
    var anchor = new DateOnly(2027, 1, 3);
    var week = HistoryQuery.Create(HistoryRange.Last7Days, InputKind.All, anchor);
    Check(week.Start == new DateOnly(2026, 12, 28) && week.End == anchor, "Week crosses year incorrectly");
    var first = week.Start ?? throw new Exception("Week has no start");
    Check(week.Includes(new DaySummary(first, 1, 1, 1, "A")) && !week.Includes(new DaySummary(first.AddDays(-1), 1, 1, 1, "A")), "Inclusive start is wrong");
    Check(!week.Includes(new DaySummary(anchor.AddDays(1), 1, 1, 1, "A")), "Future date leaked");
    var leapYesterday = HistoryQuery.Create(HistoryRange.Yesterday, InputKind.Mouse, new DateOnly(2024, 3, 1));
    Check(leapYesterday.Start == new DateOnly(2024, 2, 29) && leapYesterday.End == leapYesterday.Start, "Yesterday skips leap day");
    var month = HistoryQuery.Create(HistoryRange.Last30Days, InputKind.All, anchor);
    Check(month.End.DayNumber - month.Start!.Value.DayNumber == 29, "Last 30 days has wrong duration");
});
Test("History filters use the selected device and reject invalid custom ranges", () =>
{
    var mixed = new DaySummary(date, 50, 2, 2, "A", 15);
    var mouseOnly = new DaySummary(date, 12, 1, 1, null, 12);
    var keys = HistoryQuery.Create(HistoryRange.All, InputKind.Keyboard, date);
    Check(keys.Count(mixed) == 35 && !keys.Includes(mouseOnly), "Keyboard filter includes mouse-only records");
    var mouse = HistoryQuery.Create(HistoryRange.Custom, InputKind.Mouse, date, date, date);
    Check(mouse.Includes(mouseOnly) && mouse.Count(mixed) == 15, "Mouse custom range has wrong counts");
    foreach (var invalid in new (DateOnly? Start, DateOnly? End)[] { (null, date), (date, null), (date, date.AddDays(-1)), (date, date.AddDays(1)) })
    {
        var rejected = false;
        try { HistoryQuery.Create(HistoryRange.Custom, InputKind.All, date, invalid.Start, invalid.End); }
        catch (ArgumentException) { rejected = true; }
        Check(rejected, "Invalid custom range was accepted");
    }
});
Test("Filtered CSV matches selected dates and device without changing saved statistics", () =>
{
    var store = new StatsStore(Fresh("filtered-export"));
    store.Merge([Batch(10), Batch(4, key: "MouseLeft"), Batch(3, date.AddDays(-1), "MouseX2"), Batch(9, date.AddDays(-8), "MouseRight")]);
    store.Flush();
    var query = HistoryQuery.Create(HistoryRange.Last7Days, InputKind.Mouse, date);
    var path = Path.Combine(Fresh("filtered-file"), "mouse.csv"); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    store.ExportCsv(path, store.GetSummaries().Where(query.Includes).Select(d => d.Date), query.Device);
    var csv = File.ReadAllText(path);
    Check(csv.Contains("MouseLeft") && csv.Contains("MouseX2") && !csv.Contains("MouseRight") && !csv.Contains("\"A\""), "Export differs from filters");
    Check(File.ReadAllLines(path).Length == 3 && store.GetSummary(date).Total == 14, "CSV size or original data changed");
    store.ExportCsv(path, [date], InputKind.Keyboard);
    Check(File.ReadAllText(path).Contains("\"A\"") && !File.ReadAllText(path).Contains("Mouse"), "Keyboard export contains mouse input");
});

if (args.Contains("--native"))
{
    Test("Keyboard and mouse hooks install together, receive key messages and unhook cleanly", () =>
    {
        var a = new KeyAccumulator();
        using (var hook = new InputMonitor(a, allowInjectedForTest: true))
        {
            hook.Start(); Check(hook.IsRunning, "Native hook failed to install");
            NativeInput.SendF24(down: true); NativeInput.SendF24(down: true); NativeInput.SendF24(down: false);
            NativeInput.SendF24(down: true); NativeInput.SendF24(down: false);
            Thread.Sleep(150);
            var total = a.Drain().Sum(d => d.Keys.GetValueOrDefault("F24"));
            Check(total == 2, $"Native callback delivered {total}, expected 2");
        }
    });
    Test("Production Windows hook ignores injected keyboard messages", () =>
    {
        var a = new KeyAccumulator(); using var hook = new InputMonitor(a);
        hook.Start(); NativeInput.SendF24(true); NativeInput.SendF24(false); Thread.Sleep(150);
        Check(a.Drain().Sum(d => d.Keys.GetValueOrDefault("F24")) == 0, "Synthetic event leaked into production stats");
    });
}

Console.WriteLine($"\n{passed} passed, {failures.Count} failed.");
foreach (var failure in failures) Console.Error.WriteLine(failure);
Environment.ExitCode = failures.Count == 0 ? 0 : 1;

static class NativeInput
{
    // Exercise the actual packet decoder in this process only. Never click the user's desktop.
    public static void DeliverMousePacket(InputMonitor monitor, int message, uint data, uint flags)
    {
        var packet = new MouseHookPacket { X = 54321, Y = 54321, Data = data, Flags = flags };
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<MouseHookPacket>());
        try { Marshal.StructureToPtr(packet, memory, false); monitor.ProcessMouseMessage(0, message, memory); }
        finally { Marshal.FreeHGlobal(memory); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct MouseHookPacket { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
    // F24 has no printable text and is used only in the explicitly requested native smoke test.
    public static void SendF24(bool down)
    {
        var input = new Input { Type = 1, Data = new Union { Keyboard = new Keyboard { Vk = 0x87, Flags = down ? 0u : 2u } } };
        if (SendInput(1, new[] { input }, Marshal.SizeOf<Input>()) != 1) throw new Exception("SendInput failed: " + Marshal.GetLastWin32Error());
    }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public Union Data; }
    [StructLayout(LayoutKind.Explicit)] private struct Union
    { [FieldOffset(0)] public Keyboard Keyboard; [FieldOffset(0)] public Mouse Mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct Keyboard { public ushort Vk, Scan; public uint Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct Mouse { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] input, int size);
}

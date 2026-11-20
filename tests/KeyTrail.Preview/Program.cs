using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Input;
using System.Reflection;
using KeyTrail.Desktop;
using KeyTrail.Desktop.Controls;
using KeyTrail.Core;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--asset-lifecycle")) return ArtworkLifecycleProbe.Run(args.FirstOrDefault(a => !a.StartsWith("--")) ?? "artifacts/performance");
        if (args.Contains("--benchmark")) return PerformanceProbe.Run(args.FirstOrDefault(a => !a.StartsWith("--")) ?? "artifacts/performance");
        var root = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/preview");
        Directory.CreateDirectory(root);
        var data = Path.Combine(root, "fixture-" + Guid.NewGuid().ToString("N"));
        Console.WriteLine("Starting isolated WPF rendering test");
        var app = new App(renderOnly: true);
        app.InitializeComponent();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        using var controller = new StatsController(data, demo: true);
        app.AttachController(controller);
        var window = new MainWindow(controller) { Width = 1280, Height = 900 };
        app.MainWindow = window;
        window.Show();
        Pump(window);
        VerifyOverviewLayout(window, hasRanking: true);
        Capture(window, root, "overview-1280.png");
        VerifyMaximizedOverview(window, root);
        var pageScroll = Find<ScrollViewer>(window, "PageScroll");
        if (Find<TextBlock>(window, "TotalValue").Text != "15,128" || Find<TextBlock>(window, "KeyboardTotalValue").Text != "12,846" || Find<TextBlock>(window, "MouseTotalValue").Text != "2,282") throw new Exception("Keyboard/mouse overview totals were not rendered independently");
        var mouseCells = Descendants<Button>(Find<MouseHeatmap>(window, "MouseButtons")).ToArray();
        if (mouseCells.Length != 5) throw new Exception("Mouse heatmap does not expose all five buttons");
        VerifyMouseMaterials(Find<MouseHeatmap>(window, "MouseButtons"), mouseCells, controller.Store.GetDay(DateOnly.FromDateTime(DateTime.Today)));
        VerifyMouseHitTargets(window, mouseCells);
        VerifyMouseHandedness(window, mouseCells);
        VerifyKeyboardModel(window, controller.Store.GetDay(DateOnly.FromDateTime(DateTime.Today)));
        VerifyKeyHover(window, controller, root);
        VerifyMouseHover(window, controller, mouseCells, root);
        foreach (var button in mouseCells)
        {
            button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            var id = (string)button.Tag;
            var count = controller.Store.GetDay(DateOnly.FromDateTime(DateTime.Today)).Keys[id];
            if (!Find<TextBlock>(window, "MouseDetail").Text.Contains($"{count:N0} 次") || ((MouseRegionButton)button).IsSelected != true)
                throw new Exception("A mouse model part does not select its own count: " + id);
        }
        var mouseSide = mouseCells.Single(b => b.Tag as string == "MouseX2");
        mouseSide.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        if (!Find<TextBlock>(window, "MouseDetail").Text.Contains("32 次")) throw new Exception("Mouse selection has the wrong count");
        mouseSide.Focus();
        ShowMouse(window);
        Capture(window, root, "mouse-selected-1280.png");
        var keyboardMax = controller.Store.GetDay(DateOnly.FromDateTime(DateTime.Today)).Keys.Where(k => !MouseCatalog.IsMouse(k.Key)).MaxBy(k => k.Value).Key;
        var maxButton = Descendants<Button>(Find<KeyboardHeatmap>(window, "Keyboard")).Single(b => b.Tag as string == keyboardMax);
        if (maxButton.Background is not SolidColorBrush fill || fill.Color != (Color)ColorConverter.ConvertFromString("#1F3A5C")) throw new Exception("High mouse counts changed the keyboard heat scale");
        var space = Descendants<Button>(window).Single(b => b.Tag as string == "Space");
        space.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        if (!Find<TextBlock>(window, "KeyDetail").Text.Contains("空格")) throw new Exception("Heatmap selection is not functional");
        space.Focus(); Pump(window);
        Capture(window, root, "selected-key-1280.png");
        Find<RadioButton>(window, "OverviewNav").Focus(); Pump(window);
        Capture(window, root, "focused-nav-1280.png");

        Find<RadioButton>(window, "HistoryNav").IsChecked = true;
        WaitUntil(window, () => Find<DataGrid>(window, "HistoryTable").Items.Count == 14);
        if (Find<DataGrid>(window, "HistoryTable").Items.Count != 14) throw new Exception("History is incomplete");
        VerifyHistoryFilters(window, controller, root);
        var firstRow = Find<DataGrid>(window, "HistoryTable").Items[0];
        if ((long)firstRow.GetType().GetProperty("KeyboardTotal")!.GetValue(firstRow)! != 12846 || (long)firstRow.GetType().GetProperty("MouseTotal")!.GetValue(firstRow)! != 2282) throw new Exception("History keyboard/mouse breakdown is incorrect");
        Capture(window, root, "history-1280.png");
        var calendar = Find<ActivityCalendar>(window, "ActivityChart");
        var today = DateOnly.FromDateTime(DateTime.Today);
        var summaries = controller.Store.GetSummaries();
        var displayedTotal = summaries.Where(d => d.Date >= today.AddDays(-364) && d.Date <= today).Sum(d => d.Total).ToString("N0");
        if (Find<TextBlock>(window, "ActivityTotal").Text != displayedTotal) throw new Exception("Rolling activity total is incorrect");
        VerifyRollingActivity(calendar, summaries, today);
        VerifyActivityHover(window, calendar, summaries, today, root);
        var chosen = today.AddDays(-3);
        calendar.SelectDate(chosen); calendar.Focus(); Pump(window);
        Capture(window, root, "history-focused-1280.png");
        var bounds = calendar.GetCellBounds(chosen);
        if (calendar.DateAt(new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2)) != chosen) throw new Exception("Calendar date hit testing is incorrect");
        if (calendar.DateAt(new Point(0, 0)) is not null) throw new Exception("Calendar labels are treated as dates");
        calendar.SelectDate(today.AddDays(1));
        if (calendar.FocusedDate != chosen) throw new Exception("Calendar selected a future date");
        calendar.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
        Pump(window);
        if (Find<DatePicker>(window, "DayPicker").SelectedDate != chosen.ToDateTime(TimeOnly.MinValue) || Find<RadioButton>(window, "OverviewNav").IsChecked != true) throw new Exception("Calendar keyboard activation did not open the day");
        var hourCells = Descendants<Button>(Find<HourlyHeatmap>(window, "HourlyChart")).ToArray();
        if (hourCells.Length != 24) throw new Exception("Hourly heatmap does not have 24 cells");
        hourCells.Single(b => b.Tag is int h && h == 14).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        if (!Find<TextBlock>(window, "HourlyEmpty").Text.Contains("14:00–15:00")) throw new Exception("Hourly selection did not show the exact interval");
        Find<RadioButton>(window, "HistoryNav").IsChecked = true; Pump(window);
        window.Width = 1060; window.Height = 700; Pump(window);
        Capture(window, root, "history-1060.png");
        pageScroll.ScrollToBottom(); Pump(window); Capture(window, root, "history-table-1060.png");
        pageScroll.ScrollToTop(); Pump(window);
        window.Width = 1280; window.Height = 900;

        Find<RadioButton>(window, "SettingsNav").IsChecked = true;
        Pump(window);
        Capture(window, root, "settings-1280.png");
        var repeats = Find<CheckBox>(window, "RepeatsCheck");
        repeats.IsChecked = true;
        repeats.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        if (!AppSettings.Load(data).CountRepeats) throw new Exception("Setting did not persist");
        repeats.IsChecked = false;
        repeats.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        controller.SetPaused(true);
        if (!AppSettings.Load(data).Paused || !controller.Accumulator.Paused) throw new Exception("Pause is not immediate and persistent");
        controller.SetPaused(false);

        Find<RadioButton>(window, "OverviewNav").IsChecked = true;
        var mouseOnlyDate = DateTime.Today.AddDays(-30);
        for (var i = 0; i < 3; i++) { controller.Accumulator.HandleMouse(0x207, 0, false, mouseOnlyDate); controller.Accumulator.HandleMouse(0x208, 0, false, mouseOnlyDate); }
        controller.Collect();
        Find<DatePicker>(window, "DayPicker").SelectedDate = mouseOnlyDate; Pump(window);
        if (Find<TextBlock>(window, "KeyboardTotalValue").Text != "0" || Find<TextBlock>(window, "MouseTotalValue").Text != "3" || Find<TextBlock>(window, "TotalValue").Text != "3" || Find<TextBlock>(window, "RankEmpty").Visibility != Visibility.Visible)
            throw new Exception($"Mouse-only day is not represented correctly. Keyboard: {Find<TextBlock>(window, "KeyboardTotalValue").Text}, mouse: {Find<TextBlock>(window, "MouseTotalValue").Text}, total: {Find<TextBlock>(window, "TotalValue").Text}, rank empty: {Find<TextBlock>(window, "RankEmpty").Visibility}, date: {Find<DatePicker>(window, "DayPicker").SelectedDate}, page: {Find<TextBlock>(window, "PageTitle").Text}.");
        mouseCells.Single(b => b.Tag as string == "MouseMiddle").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        if (!Find<TextBlock>(window, "MouseDetail").Text.Contains("100.0%")) throw new Exception("Mouse-only share is incorrect");
        ShowMouse(window); Capture(window, root, "mouse-only-1280.png");
        CaptureMouseSides(window, controller, mouseCells, root);
        VerifyHiddenNumpad(window, controller, root);
        CaptureKeyboardStates(window, controller, root);
        var paletteDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-31));
        var paletteDay = DayStats.Empty(paletteDate);
        var paletteCounts = new[] { 0, 1, 16, 50, 100 };
        for (var button = 0; button < 5; button++)
            for (var count = 0; count < paletteCounts[button]; count++) paletteDay.Add(MouseCatalog.All[button].Id, paletteDate.ToDateTime(new TimeOnly(12, 0)));
        controller.Store.Merge([paletteDay]);
        Find<DatePicker>(window, "DayPicker").SelectedDate = paletteDate.ToDateTime(TimeOnly.MinValue); Pump(window);
        foreach (var cell in mouseCells) ForceVisualState(cell, typeof(UIElement), "IsMouseOverPropertyKey", true);
        ShowMouse(window);
        VerifyMouseContrast(Find<MouseHeatmap>(window, "MouseButtons"), mouseCells, "#6B87B0"); Capture(window, root, "mouse-hover-five-levels-1280.png");
        foreach (var cell in mouseCells) ForceVisualState(cell, typeof(System.Windows.Controls.Primitives.ButtonBase), "IsPressedPropertyKey", true);
        Pump(window); VerifyMouseContrast(Find<MouseHeatmap>(window, "MouseButtons"), mouseCells, "#B8694A"); Capture(window, root, "mouse-pressed-five-levels-1280.png");
        foreach (var cell in mouseCells)
        {
            ForceVisualState(cell, typeof(System.Windows.Controls.Primitives.ButtonBase), "IsPressedPropertyKey", false);
            ForceVisualState(cell, typeof(UIElement), "IsMouseOverPropertyKey", false);
        }
        var largeDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-32));
        var largeDay = DayStats.Empty(largeDate);
        largeDay.Keys["MouseLeft"] = 1_234_567; largeDay.Hours[12] = 1_234_567; largeDay.ActiveMinutes.Add(720);
        controller.Store.Merge([largeDay]);
        Find<DatePicker>(window, "DayPicker").SelectedDate = largeDate.ToDateTime(TimeOnly.MinValue); Pump(window);
        mouseCells.Single(b => b.Tag as string == "MouseLeft").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        if (!Find<TextBlock>(window, "MouseDetail").Text.Contains("1,234,567 次")) throw new Exception("Compact model label lost the exact count in details");
        ShowMouse(window); Capture(window, root, "mouse-large-count-1280.png");
        mouseCells.Single(b => b.Tag as string == "MouseMiddle").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Find<DatePicker>(window, "DayPicker").SelectedDate = DateTime.Today.AddDays(-40);
        Pump(window);
        if (Find<TextBlock>(window, "TotalValue").Text != "0") throw new Exception("Empty date was not cleared");
        VerifyOverviewLayout(window, hasRanking: false);
        Capture(window, root, "empty-1280.png");
        ShowMouse(window); Capture(window, root, "mouse-empty-1280.png");

        Find<DatePicker>(window, "DayPicker").SelectedDate = DateTime.Today;
        window.Width = window.MinWidth; window.Height = window.MinHeight;
        Pump(window);
        VerifyOverviewLayout(window, hasRanking: true);
        Capture(window, root, "overview-1060x650.png");
        ShowMouse(window); VerifyMouseHitTargets(window, mouseCells); VerifyDevicePlacement(window, mouseCells);
        var narrowRight = (MouseRegionButton)mouseCells.Single(button => button.Tag as string == "MouseRight");
        var narrowBounds = narrowRight.Shape.Bounds;
        var narrowPointer = new Point(narrowBounds.X + narrowBounds.Width / 2, narrowBounds.Y + narrowBounds.Height / 2);
        window.ShowKeyHover("MouseRight", narrowRight.TranslatePoint(narrowPointer, Find<Canvas>(window, "KeyHoverLayer"))); Pump(window);
        Capture(window, root, "mouse-key-hover-1060.png");
        window.HideKeyHover();
        Pump(window);
        VerifyAllKeysPaging(window, root);
        VerifyActivityPeriods(window, controller, today, root);
        window.WindowState = WindowState.Minimized; Pump(window);
        if (window.IsRefreshTimerRunning) throw new Exception("Minimized UI is still polling");
        window.WindowState = WindowState.Normal; Pump(window);
        if (!window.IsRefreshTimerRunning) throw new Exception("Restored UI did not resume");
        Find<DatePicker>(window, "DayPicker").SelectedDate = DateTime.Today.AddDays(-1);
        Find<RadioButton>(window, "MouseFilter").IsChecked = true;
        Find<RadioButton>(window, "Range7Days").IsChecked = true;
        window.Hide(); Pump(window);
        if (window.IsRefreshTimerRunning) throw new Exception("Hidden UI is still polling");
        app.ReleaseDashboard(); Pump(window);
        if (app.MainWindow is not null) throw new Exception("Background UI was not retired");
        var previousTotal = controller.Store.GetSummary(today).Total;
        controller.Accumulator.Handle(0x41, 0x1e, false, true, false, DateTime.Now);
        controller.Accumulator.Handle(0x41, 0x1e, false, false, false, DateTime.Now);
        controller.Accumulator.HandleMouse(0x201, 0, false, DateTime.Now);
        controller.Accumulator.HandleMouse(0x202, 0, false, DateTime.Now);
        WaitUntil(window, () => new StatsStore(Path.Combine(data, "days")).GetSummary(today).Total == previousTotal + 2, seconds: 8);
        var reopened = app.OpenDashboard(); reopened.Show(); Pump(reopened);
        if (Find<DatePicker>(reopened, "DayPicker").SelectedDate != DateTime.Today.AddDays(-1)) throw new Exception("Date selection was lost after unloading UI");
        if (Find<MouseHeatmap>(reopened, "MouseButtons").SelectedButton != "MouseMiddle") throw new Exception("Mouse selection was lost after unloading UI");
        if (Find<RadioButton>(reopened, "MouseFilter").IsChecked != true || Find<RadioButton>(reopened, "Range7Days").IsChecked != true) throw new Exception("History filters were lost after unloading UI");
        if (Find<RadioButton>(reopened, "Activity180Days").IsChecked != true
            || Find<ActivityCalendar>(reopened, "ActivityChart").FirstDate != today.AddDays(-179))
            throw new Exception("The rolling activity period was lost after unloading UI");
        reopened.AllowClose = true; reopened.Close();
        Console.WriteLine("PASS WPF overview, hourly heat cells, rolling activity totals, date hit-testing, keyboard activation, leap day, key inspection, asynchronous history, settings persistence, pause/resume, date selection, empty state, small-window render, hidden/minimized timer suspension, UI unloading, background collection and autosave, state restoration");
        return 0;
    }

    private static void VerifyHistoryFilters(MainWindow window, StatsController controller, string root)
    {
        var table = Find<DataGrid>(window, "HistoryTable");
        var today = DateOnly.FromDateTime(DateTime.Today);
        var summaries = controller.Store.GetSummaries().Where(d => d.Date >= today.AddDays(-364) && d.Date <= today).ToArray();
        Find<RadioButton>(window, "Range7Days").IsChecked = true; Pump(window);
        if (table.Items.Count != 7) throw new Exception("Last-seven-days preset is not inclusive");
        Find<RadioButton>(window, "MouseFilter").IsChecked = true; Pump(window);
        var expectedMouse = summaries.Sum(d => d.MouseTotal).ToString("N0");
        if (Find<TextBlock>(window, "ActivityTotal").Text != expectedMouse || !Find<TextBlock>(window, "ActivityTotalLabel").Text.Contains("鼠标"))
            throw new Exception("Rolling mouse totals incorrectly follow the record date filter");
        var selectedTotal = (long)table.Items[0].GetType().GetProperty("SelectedTotal")!.GetValue(table.Items[0])!;
        if (selectedTotal != 2282 || !Find<TextBlock>(window, "HistoryRangeSummary").Text.Contains("鼠标")) throw new Exception("Filtered list still reports combined counts");
        var calendar = Find<ActivityCalendar>(window, "ActivityChart");
        calendar.SelectDate(today);
        if (!calendar.TryGetDay(today, out var calendarClicks, out _) || calendarClicks != 2282)
            throw new Exception("The rolling activity chart does not follow the device filter");
        Find<RadioButton>(window, "KeyboardFilter").IsChecked = true; Pump(window);
        if (Find<TextBlock>(window, "ActivityTotal").Text != summaries.Sum(d => d.KeyboardTotal).ToString("N0")) throw new Exception("Keyboard rolling totals are incorrect");
        Find<RadioButton>(window, "MouseFilter").IsChecked = true;
        var scroll = Find<ScrollViewer>(window, "PageScroll");
        scroll.ScrollToBottom(); Pump(window); Capture(window, root, "history-mouse-7days-1280.png");
        window.Width = 1060; window.Height = 700; Pump(window); scroll.ScrollToBottom(); Pump(window);
        var records = Find<Border>(window, "HistoryRecordsPanel");
        if (records.ActualHeight > scroll.ViewportHeight + 4) throw new Exception("History filters and table exceed the viewport at minimum width");
        Capture(window, root, "history-mouse-7days-1060.png");
        Find<RadioButton>(window, "RangeToday").IsChecked = true; Pump(window);
        if (table.Items.Count != 1) throw new Exception("Today preset failed");
        Find<RadioButton>(window, "RangeYesterday").IsChecked = true; Pump(window);
        if (table.Items.Count != 1 || (DateOnly)table.Items[0].GetType().GetProperty("Date")!.GetValue(table.Items[0])! != today.AddDays(-1)) throw new Exception("Yesterday preset failed");
        Find<RadioButton>(window, "Range30Days").IsChecked = true; Pump(window);
        if (table.Items.Count != 14) throw new Exception("Thirty-day preset hid recent records");
        Find<RadioButton>(window, "RangeCustom").IsChecked = true;
        Find<DatePicker>(window, "RangeStartPicker").SelectedDate = DateTime.Today.AddDays(-5);
        Find<DatePicker>(window, "RangeEndPicker").SelectedDate = DateTime.Today.AddDays(-3); Pump(window);
        if (table.Items.Count != 3) throw new Exception("Custom range endpoints are not inclusive");
        scroll.ScrollToBottom(); Pump(window); Capture(window, root, "history-custom-1060.png");
        Find<DatePicker>(window, "RangeStartPicker").SelectedDate = DateTime.Today;
        Pump(window);
        if (table.Items.Count != 0 || Find<Button>(window, "ExportButton").IsEnabled || Find<TextBlock>(window, "RangeError").Visibility != Visibility.Visible) throw new Exception("Invalid range allows a stale export");
        scroll.ScrollToBottom(); Pump(window); Capture(window, root, "history-invalid-range-1060.png");
        Find<DatePicker>(window, "RangeStartPicker").SelectedDate = DateTime.Today.AddYears(-1).AddDays(-2);
        Find<DatePicker>(window, "RangeEndPicker").SelectedDate = DateTime.Today.AddYears(-1); Pump(window);
        if (table.Items.Count != 0 || Find<TextBlock>(window, "HistoryEmpty").Visibility != Visibility.Visible) throw new Exception("Filtered empty state is missing");
        scroll.ScrollToBottom(); Pump(window); Capture(window, root, "history-filter-empty-1060.png");
        Find<RadioButton>(window, "AllDevicesFilter").IsChecked = true;
        Find<RadioButton>(window, "RangeAll").IsChecked = true;
        Find<RadioButton>(window, "OverviewNav").IsChecked = true;
        Find<RadioButton>(window, "YesterdayButton").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); Pump(window);
        if (Find<DatePicker>(window, "DayPicker").SelectedDate != DateTime.Today.AddDays(-1) || Find<RadioButton>(window, "YesterdayButton").IsChecked != true) throw new Exception("Yesterday toolbar shortcut failed");
        var picker = Find<DatePicker>(window, "DayPicker");
        var popupButton = (Button)picker.Template.FindName("PART_Button", picker);
        popupButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); Pump(window);
        var popup = (System.Windows.Controls.Primitives.Popup)picker.Template.FindName("PART_Popup", picker);
        if (!popup.IsOpen || popup.Child is not Calendar popupCalendar) throw new Exception("Restyled date picker no longer opens a native calendar");
        CaptureVisual(popupCalendar, root, "date-calendar.png");
        popupCalendar.SelectedDate = DateTime.Today.AddDays(-2); Pump(window);
        if (picker.SelectedDate != DateTime.Today.AddDays(-2)) throw new Exception("Popup calendar selection is disconnected");
        picker.IsDropDownOpen = false;
        picker.SelectedDate = null; Pump(window);
        if (picker.SelectedDate is null) throw new Exception("Cleared overview date leaves stale undisclosed statistics");
        Find<RadioButton>(window, "TodayButton").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); Pump(window);
        if (Find<Button>(window, "NextDayButton").IsEnabled || Find<RadioButton>(window, "TodayButton").IsChecked != true) throw new Exception("Today toolbar state is incorrect");
        window.Width = 1280; window.Height = 900;
        Find<RadioButton>(window, "HistoryNav").IsChecked = true; Pump(window); scroll.ScrollToTop(); Pump(window);
        Console.WriteLine("PASS history device/preset/custom filters, independent rolling scope, invalid/empty ranges, small-window records, native date popup, toolbar shortcuts and clear-date recovery");
    }

    private static void VerifyRollingActivity(ActivityCalendar calendar, IReadOnlyList<DaySummary> summaries, DateOnly today)
    {
        if (calendar.FirstDate != today.AddDays(-364) || calendar.LastDate != today
            || calendar.GetCellBounds(today).X <= calendar.GetCellBounds(today.AddDays(-7)).X
            || !calendar.GetCellBounds(today.AddDays(1)).IsEmpty)
            throw new Exception("The rolling chart must end today in the rightmost week");

        var sunday = new DateOnly(2025, 1, 5);
        var monday = sunday.AddDays(-6);
        var leapDay = new DateOnly(2024, 2, 29);
        var rangeStart = sunday.AddDays(-364);
        DaySummary[] fixture =
        [
            new(rangeStart.AddDays(-1), 8_000, 1, 1, "A"),
            new(rangeStart, 6_228, 1, 1, "A"),
            new(leapDay, 6_228, 1, 1, "A"),
            new(monday, 10_129, 1, 1, "A"),
            new(sunday.AddDays(-1), 14_768, 1, 1, "A"),
            new(sunday, 15_491, 1, 1, "A"),
            new(sunday.AddDays(1), 90_000, 1, 1, "A")
        ];
        try
        {
            calendar.SetData(fixture, sunday);
            var sundayBounds = calendar.GetCellBounds(sunday);
            var mondayBounds = calendar.GetCellBounds(monday);
            var januaryFirst = calendar.GetCellBounds(new DateOnly(2025, 1, 1));
            var leapBounds = calendar.GetCellBounds(leapDay);
            if (sundayBounds.IsEmpty || mondayBounds.IsEmpty || januaryFirst.IsEmpty || leapBounds.IsEmpty
                || sundayBounds.X != mondayBounds.X || januaryFirst.X != mondayBounds.X
                || !(mondayBounds.Y < januaryFirst.Y && januaryFirst.Y < sundayBounds.Y)
                || calendar.DateAt(CellCenter(leapBounds)) != leapDay)
                throw new Exception("Week columns or the leap-day cell broke across the new year");
            if (!calendar.TryGetDay(rangeStart, out var count, out var prior) || count != 6_228 || prior != 8_000
                || calendar.TryGetDay(sunday.AddDays(1), out _, out _))
                throw new Exception("Daily comparison lost its previous day at the start or included a future date");
            var heatLevels = new[] { rangeStart, monday, sunday.AddDays(-1), sunday }.Select(calendar.GetHeatLevel).ToArray();
            if (heatLevels[0] <= 0 || !heatLevels.SequenceEqual(heatLevels.Order())
                || heatLevels.Distinct().Count() != 4 || calendar.GetHeatLevel(leapDay) != heatLevels[0]
                || calendar.GetHeatLevel(sunday.AddDays(-2)) != 0)
                throw new Exception("Rolling heat colors did not separate close totals, distinguish zero, and preserve ties");

            calendar.SetData(fixture, sunday.AddDays(1));
            var latestMonday = calendar.GetCellBounds(sunday.AddDays(1));
            var futureTuesday = new Point(CellCenter(latestMonday).X, CellCenter(calendar.GetCellBounds(sunday.AddDays(-5))).Y);
            if (calendar.FirstDate != rangeStart.AddDays(1) || calendar.LastDate != sunday.AddDays(1)
                || latestMonday.X != sundayBounds.X || calendar.GetCellBounds(sunday).X >= sundayBounds.X
                || calendar.DateAt(futureTuesday) is not null)
                throw new Exception("Starting a new Monday did not shift the weeks left and extend the chart");
            calendar.SetData(fixture, sunday.AddDays(2));
            var latestTuesday = calendar.GetCellBounds(sunday.AddDays(2));
            if (latestTuesday.X != latestMonday.X || latestTuesday.Y <= latestMonday.Y)
                throw new Exception("The newest day must continue down the same rightmost week");

            calendar.SetData([], sunday);
            if (!calendar.TryGetDay(sunday, out count, out prior) || count != 0 || prior != 0 || calendar.GetHeatLevel(sunday) != 0
                || calendar.DateAt(CellCenter(calendar.GetCellBounds(sunday))) != sunday)
                throw new Exception("An empty window should still show selectable zero-activity days");
        }
        finally
        {
            calendar.SetData(summaries, today);
        }
        Console.WriteLine("PASS rolling activity: latest week, cross-year geometry, leap day, prior-day comparison, increasing color contrast and zero activity");
    }

    private static void VerifyActivityHover(MainWindow window, ActivityCalendar calendar, IReadOnlyList<DaySummary> summaries,
        DateOnly today, string root)
    {
        var layer = Find<Canvas>(window, "KeyHoverLayer");
        var card = Find<Border>(window, "KeyHoverCard");
        var count = summaries.Single(day => day.Date == today).Total;
        var previous = summaries.Single(day => day.Date == today.AddDays(-1)).Total;
        var change = count - previous;
        var difference = change.ToString("+#,0;-#,0;0");
        var percentage = $"{(change < 0 ? "-" : "+")}{Math.Abs((double)change / previous):P1}";
        var point = calendar.TranslatePoint(CellCenter(calendar.GetCellBounds(today)), layer);
        window.ShowActivityHover(today, point);
        Pump(window);
        var comparison = Find<TextBlock>(window, "KeyHoverShare").Text.Replace('−', '-');
        var dateLabel = Find<TextBlock>(window, "KeyHoverDate").Text.Replace('/', '-');
        if (card.Visibility != Visibility.Visible || Find<TextBlock>(window, "KeyHoverCount").Text != count.ToString("N0")
            || !dateLabel.Contains(today.ToString("yyyy-MM-dd"))
            || Find<TextBlock>(window, "KeyHoverShareLabel").Text != "较前一天"
            || (change == 0 ? comparison != "持平" : !comparison.Contains(difference) || !comparison.Contains(percentage)))
            throw new Exception($"The activity hover card lost the exact day total or comparison. Comparison: {comparison}");
        if (Find<Border>(window, "KeyHoverBackdrop").Background is not VisualBrush brush
            || !ReferenceEquals(brush.Visual, Find<Grid>(window, "DashboardSurface")))
            throw new Exception("The date hover card did not reuse the live frosted-glass backdrop");
        EnsureWithin(card, layer);
        var placement = LayoutBounds(card, layer);
        if (placement.Right > point.X)
            throw new Exception("The card did not flip to stay visible beside the newest column");
        Capture(window, root, "history-hover-1280.png");
        var movement = new Vector(-2, -2);
        window.ShowActivityHover(today, point + movement);
        Pump(window);
        if ((LayoutBounds(card, layer).Location - placement.Location - movement).Length > 2)
            throw new Exception("The history hover card stopped following the pointer inside a date cell");
        calendar.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
        Pump(window);
        if (card.Visibility != Visibility.Collapsed)
            throw new Exception("The activity hover card stayed open after leaving the chart");

        window.WindowState = WindowState.Maximized;
        Pump(window);
        var latestCell = CellCenter(calendar.GetCellBounds(today));
        if (calendar.DateAt(latestCell) != today || latestCell.X < calendar.ActualWidth - 35)
            throw new Exception("The newest date did not stay in the rightmost column when maximized");
        window.ShowActivityHover(today, calendar.TranslatePoint(latestCell, layer));
        Pump(window);
        EnsureWithin(card, layer);
        Capture(window, root, "history-hover-maximized.png");
        window.HideKeyHover();
        window.WindowState = WindowState.Normal;
        Pump(window);
        Console.WriteLine("PASS rolling activity hover: shared glass, exact count, prior-day change and right-edge placement");
    }

    private static Point CellCenter(Rect bounds) => new(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);

    private static void VerifyActivityPeriods(MainWindow window, StatsController controller, DateOnly today, string root)
    {
        var boundaries = new[] { (Age: 29, Keys: 401, Clicks: 21), (Age: 179, Keys: 502, Clicks: 32),
            (Age: 364, Keys: 603, Clicks: 43), (Age: 365, Keys: 704, Clicks: 54) };
        foreach (var item in boundaries)
        {
            var day = DayStats.Empty(today.AddDays(-item.Age));
            day.Keys["A"] = item.Keys;
            day.Keys["MouseLeft"] = item.Clicks;
            day.Hours[12] = item.Keys + item.Clicks;
            day.ActiveMinutes.Add(720);
            controller.Store.Merge([day]);
        }
        var summaries = controller.Store.GetSummaries();
        var calendar = Find<ActivityCalendar>(window, "ActivityChart");
        var total = Find<TextBlock>(window, "ActivityTotal");
        var table = Find<DataGrid>(window, "HistoryTable");

        DaySummary[] InPeriod(int days) => summaries.Where(d => d.Date >= today.AddDays(1 - days) && d.Date <= today).ToArray();
        long Count(DaySummary day, bool mouseOnly) => mouseOnly ? day.MouseTotal : day.Total;
        string PeriodTotal(int days, bool mouseOnly) => InPeriod(days).Sum(d => Count(d, mouseOnly)).ToString("N0");

        void VerifyPeriod(int days, bool mouseOnly)
        {
            var first = today.AddDays(1 - days);
            var firstCell = calendar.GetCellBounds(first);
            var lastCell = calendar.GetCellBounds(today);
            var sameWeek = days - 1 <= ((int)today.DayOfWeek + 6) % 7;
            if (calendar.FirstDate != first || calendar.LastDate != today || firstCell.IsEmpty || lastCell.IsEmpty
                || (sameWeek ? lastCell.X != firstCell.X : lastCell.X <= firstCell.X)
                || calendar.DateAt(CellCenter(lastCell)) != today
                || !calendar.GetCellBounds(first.AddDays(-1)).IsEmpty || !calendar.GetCellBounds(today.AddDays(1)).IsEmpty)
                throw new Exception($"The {days}-day window did not place today at the end of the inclusive activity range");
            if (total.Text != PeriodTotal(days, mouseOnly)
                || Find<TextBlock>(window, "ActivityDays").Text != InPeriod(days).Count(d => Count(d, mouseOnly) > 0).ToString("N0"))
                throw new Exception($"The {days}-day chart totals do not match the selected period and device");
        }

        Find<RadioButton>(window, "HistoryNav").IsChecked = true;
        WaitUntil(window, () => total.Text == PeriodTotal(365, mouseOnly: false));
        if (Find<RadioButton>(window, "ActivityYear").IsChecked != true)
            throw new Exception("The history chart should default to one year");
        Find<RadioButton>(window, "RangeToday").IsChecked = true;
        Pump(window);
        var layer = Find<Canvas>(window, "KeyHoverLayer");
        var scroll = Find<ScrollViewer>(window, "PageScroll");
        foreach (var (button, days) in new[] { ("Activity7Days", 7), ("Activity30Days", 30), ("Activity180Days", 180), ("ActivityYear", 365) })
        {
            Find<RadioButton>(window, button).IsChecked = true;
            Pump(window);
            VerifyPeriod(days, mouseOnly: false);
            if (table.Items.Count != 1 || Find<RadioButton>(window, "RangeToday").IsChecked != true)
                throw new Exception("Changing the activity period changed the separate daily record filter");
            if (days is 7 or 30)
            {
                scroll.ScrollToTop();
                Pump(window);
                if (days == 7) window.ShowActivityHover(today, calendar.TranslatePoint(CellCenter(calendar.GetCellBounds(today)), layer));
                Pump(window);
                Capture(window, root, $"history-{days}days.png");
                window.HideKeyHover();
            }
        }

        Find<RadioButton>(window, "Activity180Days").IsChecked = true;
        Find<RadioButton>(window, "Range7Days").IsChecked = true;
        Pump(window);
        if (table.Items.Count != 7) throw new Exception("The daily list did not independently switch to its seven-day filter");
        VerifyPeriod(180, mouseOnly: false);
        Find<RadioButton>(window, "MouseFilter").IsChecked = true;
        Pump(window);
        VerifyPeriod(180, mouseOnly: true);
        if (!calendar.TryGetDay(today, out var clicks, out _) || clicks != summaries.Single(d => d.Date == today).MouseTotal)
            throw new Exception("The shared device filter did not reach the period's activity cells");
        Find<RadioButton>(window, "OverviewNav").IsChecked = true;
        Pump(window);
        Console.WriteLine("PASS activity periods: inclusive 7, 30, 180 and 365 day windows, latest week, independent daily list filter and shared device filter");
    }

    private static T Find<T>(Window window, string name) where T : FrameworkElement => (T)window.FindName(name);

    private static void VerifyMaximizedOverview(Window window, string root)
    {
        var normalWidth = window.Width;
        var normalHeight = window.Height;
        window.Width = window.MinWidth;
        window.Height = window.MinHeight;
        Pump(window);
        var overview = Find<Grid>(window, "OverviewPage");
        var analytics = Find<Grid>(window, "OverviewAnalytics");
        var smallOverviewHeight = overview.ActualHeight;
        var smallAnalyticsHeight = analytics.ActualHeight;

        window.WindowState = WindowState.Maximized;
        Pump(window);
        VerifyOverviewLayout(window, hasRanking: true);
        Capture(window, root, "overview-maximized.png");

        window.WindowState = WindowState.Normal;
        Pump(window);
        if (Math.Abs(window.ActualWidth - window.MinWidth) > 2 || Math.Abs(window.ActualHeight - window.MinHeight) > 2)
            throw new Exception("Restoring after maximization did not return to the small window");
        VerifyOverviewLayout(window, hasRanking: true);

        // Native maximization depends on the test desktop. Also request the large layout directly.
        window.Width = 2048;
        window.Height = 1200;
        Pump(window);
        VerifyOverviewLayout(window, hasRanking: true);
        var overviewGrowth = overview.ActualHeight - smallOverviewHeight;
        if (overviewGrowth > 300 && analytics.ActualHeight - smallAnalyticsHeight < overviewGrowth / 5)
            throw new Exception("The lower analytics cards did not grow with the taller overview");
        Capture(window, root, "overview-wide.png");
        var notice = Find<Border>(window, "NoticeBanner");
        var noticeVisibility = notice.Visibility;
        notice.Visibility = Visibility.Collapsed;
        window.UpdateLayout();
        VerifyOverviewLayout(window, hasRanking: true);
        notice.Visibility = noticeVisibility;

        window.Width = normalWidth;
        window.Height = normalHeight;
        Pump(window);
        VerifyOverviewLayout(window, hasRanking: true);
        Console.WriteLine("PASS overview fills the workspace when maximized and wide, and fits after restoration");
    }

    private static void VerifyOverviewLayout(Window window, bool hasRanking)
    {
        var overview = Find<Grid>(window, "OverviewPage");
        var dashboard = Find<Grid>(window, "DashboardSurface");
        for (var parent = VisualTreeHelper.GetParent(overview); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is ScrollViewer) throw new Exception("Daily Overview still scrolls");
        if (Find<ScrollViewer>(window, "PageScroll").IsVisible)
            throw new Exception("Scrollable pages are visible on the overview");

        EnsureWithin(overview, dashboard);
        foreach (var name in new[] { "TotalValue", "KeyboardTotalValue", "MouseTotalValue", "MinutesValue", "MousePanel", "Keyboard",
            "MouseButtons", "MouseSummary", "KeyDetail", "MouseDetail", "AllKeyboardKeysButton", "HourlyChart", "HourlyEmpty",
            hasRanking ? "TopKeysPanel" : "RankEmpty" })
            EnsureWithin(Find<FrameworkElement>(window, name), overview);
        var header = new[] { "PageTitle", "DateControls", "ExportButton" }
            .Select(name => Find<FrameworkElement>(window, name))
            .ToList();
        var notice = Find<Border>(window, "NoticeBanner");
        if (notice.IsVisible) header.Add(notice);
        foreach (var element in header) EnsureWithin(element, dashboard);
        var footer = Find<TextBlock>(window, "FooterLabel");
        var saveLabel = Find<TextBlock>(window, "SaveLabel");
        EnsureWithin(footer, dashboard);
        EnsureWithin(saveLabel, dashboard);
        var overviewBounds = LayoutBounds(overview, dashboard);
        var headerBottom = header.Max(element => LayoutBounds(element, dashboard).Bottom);
        var topGap = overviewBounds.Top - headerBottom;
        if (topGap < -1 || topGap > 24)
            throw new Exception($"Overview has an unexpected {topGap:0.0} DIP gap below the header at {dashboard.ActualWidth:0} × {dashboard.ActualHeight:0}");

        const double tolerance = 2;
        const double horizontalInset = 28;
        const double bottomInset = 12;
        const double footerSpacing = 10;
        var analytics = Find<Grid>(window, "OverviewAnalytics");
        var analyticsBounds = LayoutBounds(analytics, dashboard);
        if (Math.Abs(analyticsBounds.Bottom - overviewBounds.Bottom) > tolerance)
            throw new Exception("The lower analytics row does not reach the bottom of the overview");
        foreach (var name in new[] { "HourlyPanel", "RankPanel" })
        {
            var panel = Find<Border>(window, name);
            EnsureWithin(panel, analytics);
            var panelBounds = LayoutBounds(panel, dashboard);
            if (Math.Abs(panelBounds.Top - analyticsBounds.Top) > tolerance || Math.Abs(panelBounds.Bottom - analyticsBounds.Bottom) > tolerance)
                throw new Exception($"{name} does not fill the height of the lower analytics row");
        }

        var sidebarWidth = dashboard.ColumnDefinitions[0].ActualWidth;
        var leftInset = overviewBounds.Left - sidebarWidth;
        var rightInset = dashboard.ActualWidth - overviewBounds.Right;
        if (Math.Abs(leftInset - horizontalInset) > tolerance || Math.Abs(rightInset - horizontalInset) > tolerance)
            throw new Exception($"Overview must span the workspace with {horizontalInset} DIP gutters. Actual gutters were {leftInset:0.0} and {rightInset:0.0} DIP.");

        var headerLeft = LayoutBounds(Find<TextBlock>(window, "PageTitle"), dashboard).Left;
        var headerRight = LayoutBounds(Find<Button>(window, "ExportButton"), dashboard).Right;
        var footerBounds = LayoutBounds(footer, dashboard);
        var saveBounds = LayoutBounds(saveLabel, dashboard);
        if (Math.Abs(overviewBounds.Left - headerLeft) > tolerance || Math.Abs(overviewBounds.Right - headerRight) > tolerance
            || Math.Abs(overviewBounds.Left - footerBounds.Left) > tolerance || Math.Abs(overviewBounds.Right - saveBounds.Right) > tolerance)
            throw new Exception("The header, overview and footer use different workspace edges");

        var bottomGap = dashboard.ActualHeight - Math.Max(footerBounds.Bottom, saveBounds.Bottom);
        var footerGap = footerBounds.Top - overviewBounds.Bottom;
        if (Math.Abs(bottomGap - bottomInset) > tolerance || Math.Abs(footerGap - footerSpacing) > tolerance)
            throw new Exception($"The footer has an unexpected {footerGap:0.0} DIP gap above and {bottomGap:0.0} DIP gap below.");
        Console.WriteLine($"PASS single-screen overview: all dashboard sections inside the {dashboard.ActualWidth:0} × {dashboard.ActualHeight:0} viewport");
    }

    private static Rect LayoutBounds(FrameworkElement element, FrameworkElement container) =>
        element.TransformToAncestor(container).TransformBounds(new Rect(element.RenderSize));

    private static void EnsureWithin(FrameworkElement element, FrameworkElement container)
    {
        if (!element.IsVisible || element.ActualWidth < 1 || element.ActualHeight < 1)
            throw new Exception($"{element.Name} has no visible content in the viewport");
        var bounds = LayoutBounds(element, container);
        var viewport = new Rect(container.RenderSize);
        viewport.Inflate(1, 1);
        if (!viewport.Contains(bounds))
            throw new Exception($"{element.Name} is clipped by {container.Name}: {bounds} vs {container.RenderSize}");
    }

    private static void ShowMouse(Window window)
    {
        Pump(window);
        EnsureWithin(Find<Border>(window, "MousePanel"), Find<Grid>(window, "OverviewPage"));
    }

    private static void VerifyAllKeysPaging(Window window, string root)
    {
        var dashboard = Find<Grid>(window, "DashboardSurface");
        var overview = Find<Grid>(window, "OverviewPage");
        var chart = Find<HourlyHeatmap>(window, "HourlyChart");
        var panel = Find<Border>(window, "AllKeysPanel");
        var rows = Find<WrapPanel>(window, "AllKeysList");
        var search = Find<TextBox>(window, "KeySearch");
        var previous = Find<Button>(window, "PreviousKeysPageButton");
        var next = Find<Button>(window, "NextKeysPageButton");
        var page = Find<TextBlock>(window, "KeyPageLabel");
        var chartPosition = chart.TranslatePoint(new Point(), overview);

        void Click(string name)
        {
            Find<Button>(window, name).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Pump(window);
        }

        Click("AllKeyboardKeysButton");
        EnsureWithin(panel, (FrameworkElement)window.Content);
        EnsureWithin(rows, panel);
        for (var parent = VisualTreeHelper.GetParent(rows); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is ScrollViewer) throw new Exception("The all-keys list still scrolls");
        var firstPage = page.Text;
        var firstRow = rows.Children.Cast<UIElement>().FirstOrDefault();
        if (previous.IsEnabled || !next.IsEnabled || firstRow is null || string.IsNullOrWhiteSpace(firstPage))
            throw new Exception("The all-keys list did not open at the first page");
        Click("NextKeysPageButton");
        if (!previous.IsEnabled || page.Text == firstPage || ReferenceEquals(firstRow, rows.Children.Cast<UIElement>().FirstOrDefault()))
            throw new Exception("The all-keys list did not advance to a different page");
        Click("PreviousKeysPageButton");
        if (previous.IsEnabled || page.Text != firstPage)
            throw new Exception("The all-keys list did not return to the first page");
        Click("NextKeysPageButton");
        search.Text = "空格"; Pump(window);
        if (previous.IsEnabled || next.IsEnabled || rows.Children.Count != 1
            || !Descendants<TextBlock>(rows).Any(text => text.Text.Contains("空格")))
            throw new Exception("Searching the all-keys list did not reset pagination and find the matching key");
        search.Clear(); Pump(window);
        Capture(window, root, "all-keys-1060x650.png");
        Click("CloseAllKeysButton");
        if (panel.IsVisible || (chart.TranslatePoint(new Point(), overview) - chartPosition).Length > 1)
            throw new Exception("The all-keys overlay changed the overview layout");
        EnsureWithin(overview, dashboard);
        Console.WriteLine("PASS paged all-keys overlay: bounded layout, previous/next, search reset, close");
    }

    private static void VerifyMouseHitTargets(Window window, Button[] buttons)
    {
        var model = Find<MouseHeatmap>(window, "MouseButtons");
        foreach (var button in buttons.Cast<MouseRegionButton>())
        {
            var bounds = button.Shape.Bounds;
            var point = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
            if (model.InputHitTest(button.TranslatePoint(point, model)) != button)
                throw new Exception("Mouse part does not receive its own visual hit: " + button.Tag);
            if (button.InputHitTest(new Point(0, 0)) == button)
                throw new Exception("Mouse part captures the empty corner outside its shape");
        }
    }
    private static void VerifyMouseHandedness(Window window, Button[] buttons)
    {
        // Independent landmarks read from the shipping plate, in its original 1536 x 1024 pixels.
        // Front is upper-left, heel lower-right: cross < 0 is physical left in image Y-down space.
        var nose = new Point(430, 110); var heel = new Point(930, 950); var forward = nose - heel;
        var probes = new Dictionary<string, Point>
        {
            ["MouseLeft"] = new(445, 290), ["MouseRight"] = new(726, 200),
            ["MouseMiddle"] = new(565, 205), ["MouseX1"] = new(562, 604), ["MouseX2"] = new(464, 477)
        };
        static double Cross(Vector a, Vector b) => a.X * b.Y - a.Y * b.X;
        foreach (var id in new[] { "MouseLeft", "MouseX1", "MouseX2" })
            if (Cross(forward, probes[id] - nose) >= 0) throw new Exception(id + " is on the physical right flank");
        if (Cross(forward, probes["MouseRight"] - nose) <= 0) throw new Exception("Right click is on the physical left flank");
        if (Vector.Multiply(forward, probes["MouseX2"] - probes["MouseX1"]) <= 0)
            throw new Exception("Front and rear thumb buttons are reversed");
        var model = Find<MouseHeatmap>(window, "MouseButtons");
        foreach (var button in buttons.Cast<MouseRegionButton>())
        {
            var canvas = (Canvas)VisualTreeHelper.GetParent(button);
            var probe = probes[(string)button.Tag];
            var point = new Point(probe.X * canvas.Width / 1536, probe.Y * canvas.Height / 1024);
            if (model.InputHitTest(canvas.TranslatePoint(point, model)) != button)
                throw new Exception("Image landmark selects the wrong stored mouse ID: " + button.Tag);
        }
        Console.WriteLine("PASS physical handedness: left main + both thumb buttons on left flank; X2 ahead of X1; all five image landmarks hit the correct stored ID");
        VerifyDevicePlacement(window, buttons);
    }
    private static void VerifyDevicePlacement(Window window, Button[] mouseButtons)
    {
        var keyboard = Find<KeyboardHeatmap>(window, "Keyboard"); var mouse = Find<MouseHeatmap>(window, "MouseButtons");
        var right = keyboard.TranslatePoint(new Point(keyboard.ActualWidth, 0), window);
        var left = mouse.TranslatePoint(new Point(), window);
        if (left.X < right.X + 10 || Math.Abs(mouse.ActualHeight - keyboard.ActualHeight) > 2)
            throw new Exception("Mouse is not beside the keyboard in the removed numpad area");
        var canvas = (Canvas)VisualTreeHelper.GetParent(mouseButtons[0]);
        Point ToModel(double x, double y) => canvas.TranslatePoint(new Point(x * canvas.Width / 1536, y * canvas.Height / 1024), mouse);
        var nose = ToModel(430, 110); var heel = ToModel(930, 950);
        if (Math.Abs(nose.X - heel.X) > 2 || nose.Y >= heel.Y) throw new Exception("Mouse nose is not upright");
    }
    private static void VerifyKeyboardModel(Window window, DayStats original)
    {
        var keyboard = Find<KeyboardHeatmap>(window, "Keyboard");
        var buttons = Descendants<KeyboardRegionButton>(keyboard).ToArray();
        if (buttons.Length != 87 || buttons.Select(b => b.Tag).Distinct().Count() != 87
            || buttons.Any(b => ((string)b.Tag).StartsWith("Num") || b.Tag as string is "Fn" or "RWin")
            || !buttons.Any(b => b.Tag as string == "VolumeMute")) throw new Exception("Keyboard does not match the user's 88-cap layout with hardware-only Fn");
        foreach (var button in buttons)
        {
            var b = button.Shape.Bounds; var point = new Point(b.X + b.Width / 2, b.Y + b.Height / 2);
            if (keyboard.InputHitTest(button.TranslatePoint(point, keyboard)) != button || button.InputHitTest(new Point()) == button)
                throw new Exception("Keyboard face has an incorrect hit region: " + button.Tag);
        }
        var canvas = (Canvas)VisualTreeHelper.GetParent(buttons[0]);
        var landmarks = new Dictionary<string, Point> { ["Escape"] = new(123,82), ["VolumeMute"] = new(1680,83), ["A"] = new(324,414),
            ["Space"] = new(836,616), ["Up"] = new(1945,514), ["Right"] = new(2054,616), ["PageDown"] = new(2054,310) };
        foreach (var (id, p) in landmarks)
            if (keyboard.InputHitTest(canvas.TranslatePoint(new Point(p.X * canvas.Width / 2172, p.Y * canvas.Height / 724), keyboard)) != buttons.Single(b => b.Tag as string == id))
                throw new Exception("Keyboard image landmark maps to the wrong key: " + id);
        var sample = DayStats.Empty(DateOnly.Parse(original.Date, System.Globalization.CultureInfo.InvariantCulture)); var counts = new long[] { 0, 1, 16, 50, 100 };
        for (var i = 0; i < buttons.Length; i++) sample.Keys[(string)buttons[i].Tag] = counts[i % 5];
        sample.Hours[12] = sample.Keys.Values.Sum(); keyboard.Update(sample);
        var materials = buttons.Select(b => b.HeatTexture).ToArray(); var minimum = double.MaxValue;
        foreach (var button in buttons)
        {
            var texture = button.HeatTexture;
            if (!texture.IsFrozen) throw new Exception("Keyboard texture is mutable");
            var pixels = new byte[texture.PixelWidth * texture.PixelHeight * 4]; texture.CopyPixels(pixels, texture.PixelWidth * 4, 0);
            var ink = Luminance(((SolidColorBrush)button.Foreground).Color);
            for (var p = 0; p < pixels.Length; p += 4)
            {
                if (pixels[p + 3] < 240) continue;
                var fill = Luminance(Color.FromRgb(pixels[p + 2], pixels[p + 1], pixels[p]));
                minimum = Math.Min(minimum, (Math.Max(ink, fill) + .05) / (Math.Min(ink, fill) + .05));
            }
        }
        if (minimum < 4.5) throw new Exception("Keyboard material/legend contrast below 4.5:1: " + minimum);
        var peak = sample.Keys.First(k => k.Value == 100).Key; sample.Keys[peak]++; sample.Hours[12]++; keyboard.Update(sample);
        for (var i = 0; i < buttons.Length; i++)
            if (!ReferenceEquals(materials[i], buttons[i].HeatTexture)) throw new Exception("Same-level key count regenerated its texture");
        keyboard.Update(original);
        Console.WriteLine($"PASS keyboard: 87 native hit targets + non-counted Fn, independent image landmarks, hidden numpad, frozen/reused textures; minimum opaque material/legend contrast {minimum:F2}:1");
    }

    private static void VerifyKeyHover(MainWindow window, StatsController controller, string root)
    {
        var layer = Find<Canvas>(window, "KeyHoverLayer");
        var card = Find<Border>(window, "KeyHoverCard");
        var dashboard = Find<Grid>(window, "DashboardSurface");
        var backdrop = Find<Border>(window, "KeyHoverBackdrop");
        var glass = Find<Grid>(window, "KeyHoverGlass");
        var name = Find<TextBlock>(window, "KeyHoverName");
        var dateLabel = Find<TextBlock>(window, "KeyHoverDate");
        var count = Find<TextBlock>(window, "KeyHoverCount");
        var share = Find<TextBlock>(window, "KeyHoverShare");
        var stats = Find<StackPanel>(window, "KeyHoverStats");
        var unavailable = Find<TextBlock>(window, "KeyHoverUnavailable");
        var picker = Find<DatePicker>(window, "DayPicker");
        var keyboard = Find<KeyboardHeatmap>(window, "Keyboard");
        var button = Descendants<KeyboardRegionButton>(keyboard).Single(b => b.Tag as string == "A");
        var bounds = button.Shape.Bounds;
        var pointer = button.TranslatePoint(new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2), layer);
        var today = DateOnly.FromDateTime(DateTime.Today);

        void ConfirmStats(string id, DayStats day)
        {
            var expectedCount = day.Keys.GetValueOrDefault(id);
            var expectedShare = day.KeyboardTotal == 0 ? 0 : (double)expectedCount / day.KeyboardTotal;
            if (card.Visibility != Visibility.Visible || stats.Visibility != Visibility.Visible || unavailable.Visibility == Visibility.Visible
                || !name.Text.Contains(KeyCatalog.Get(id).Name) || count.Text != expectedCount.ToString("N0") || share.Text != expectedShare.ToString("P1"))
                throw new Exception($"Wrong hover statistics for {id} on {day.Date}: '{name.Text}', '{count.Text}', '{share.Text}'");
        }

        if (card.Visibility != Visibility.Collapsed || layer.IsHitTestVisible) throw new Exception("Keyboard card starts open or intercepts the pointer");
        button.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
        ConfirmStats("A", controller.Store.GetDay(today));
        button.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
        if (card.Visibility != Visibility.Collapsed) throw new Exception("Leaving a key did not hide its card");

        window.ShowKeyHover("A", pointer); Pump(window);
        if (backdrop.Background is not VisualBrush brush || !ReferenceEquals(brush.Visual, dashboard)
            || brush.ViewboxUnits != BrushMappingMode.Absolute || dashboard.IsAncestorOf(card))
            throw new Exception("The hover glass must sample the live underlying dashboard without sampling its own card");
        if (backdrop.Effect is not BlurEffect { Radius: > 0 } || card.Effect is BlurEffect || glass.Effect is BlurEffect
            || backdrop.IsAncestorOf(count) || glass.Clip is not RectangleGeometry { RadiusX: > 0, RadiusY: > 0 })
            throw new Exception("Hover card blur must be confined to the rounded backdrop and keep the statistics sharp");
        var currentDate = dateLabel.Text;
        if (string.IsNullOrWhiteSpace(currentDate)) throw new Exception("Keyboard card omitted the selected date");
        var initial = card.TranslatePoint(new Point(), layer);
        var initialCrop = brush.Viewbox;
        if ((initialCrop.Location - initial - new Vector(-12, -12)).Length > 2
            || Math.Abs(initialCrop.Width - card.ActualWidth - 24) > 2 || Math.Abs(initialCrop.Height - card.ActualHeight - 24) > 2)
            throw new Exception("Hover glass sampled the wrong dashboard region");
        Capture(window, root, "keyboard-key-hover-1280.png");
        var movement = new Vector(19, 12);
        window.ShowKeyHover("A", pointer + movement); Pump(window);
        if ((card.TranslatePoint(new Point(), layer) - initial - movement).Length > 2)
            throw new Exception("The hover card did not follow movement inside a key");
        if (!ReferenceEquals(backdrop.Background, brush) || (brush.Viewbox.Location - initialCrop.Location - movement).Length > 2)
            throw new Exception("The glass did not reuse its brush and move the crop with the card");
        window.ShowKeyHover("Space", new Point(layer.ActualWidth - 1, layer.ActualHeight - 1)); Pump(window);
        var corner = card.TranslatePoint(new Point(), layer);
        if (corner.X < -1 || corner.Y < -1 || corner.X + card.ActualWidth > layer.ActualWidth + 1 || corner.Y + card.ActualHeight > layer.ActualHeight + 1)
            throw new Exception("The hover card was clipped at the bottom-right corner");
        ConfirmStats("Space", controller.Store.GetDay(today));

        var yesterday = today.AddDays(-1);
        picker.SelectedDate = yesterday.ToDateTime(TimeOnly.MinValue); Pump(window);
        window.ShowKeyHover("Space", pointer);
        ConfirmStats("Space", controller.Store.GetDay(yesterday));
        if (string.IsNullOrWhiteSpace(dateLabel.Text) || dateLabel.Text == currentDate) throw new Exception("Keyboard card date did not follow the selected day");

        var emptyDate = today.AddDays(-20);
        picker.SelectedDate = emptyDate.ToDateTime(TimeOnly.MinValue); Pump(window);
        window.ShowKeyHover("A", pointer);
        ConfirmStats("A", DayStats.Empty(emptyDate));
        window.ShowKeyHover("Fn", pointer);
        if (card.Visibility != Visibility.Visible || !name.Text.Contains("Fn") || stats.Visibility == Visibility.Visible
            || unavailable.Visibility != Visibility.Visible || string.IsNullOrWhiteSpace(unavailable.Text))
            throw new Exception("Hardware Fn was presented as a key with counted usage");

        window.HideKeyHover();
        picker.SelectedDate = today.ToDateTime(TimeOnly.MinValue); Pump(window);
        window.ShowKeyHover("A", pointer);
        ConfirmStats("A", controller.Store.GetDay(today));
        if (dateLabel.Text != currentDate) throw new Exception("Keyboard card date remained stale after returning to today");
        Find<RadioButton>(window, "SettingsNav").IsChecked = true; Pump(window);
        if (card.Visibility != Visibility.Collapsed) throw new Exception("Keyboard card remained open after leaving the overview");
        Find<RadioButton>(window, "OverviewNav").IsChecked = true; Pump(window);
        Console.WriteLine("PASS key hover: registered live backdrop blur, key entry and exit, pointer following, edge fit, date-specific counts and shares, zero and Fn states, navigation cleanup");
    }

    private static void VerifyMouseHover(MainWindow window, StatsController controller, Button[] buttons, string root)
    {
        var layer = Find<Canvas>(window, "KeyHoverLayer");
        var card = Find<Border>(window, "KeyHoverCard");
        var name = Find<TextBlock>(window, "KeyHoverName");
        var count = Find<TextBlock>(window, "KeyHoverCount");
        var share = Find<TextBlock>(window, "KeyHoverShare");
        var date = Find<TextBlock>(window, "KeyHoverDate");
        var countLabel = Find<TextBlock>(window, "KeyHoverCountLabel");
        var shareLabel = Find<TextBlock>(window, "KeyHoverShareLabel");
        var picker = Find<DatePicker>(window, "DayPicker");
        var today = DateOnly.FromDateTime(DateTime.Today);
        var day = controller.Store.GetDay(today);

        void ConfirmMouseStats(string id, DayStats expected)
        {
            var expectedCount = expected.Keys.GetValueOrDefault(id);
            var expectedShare = expected.MouseTotal == 0 ? 0 : (double)expectedCount / expected.MouseTotal;
            if (card.Visibility != Visibility.Visible || name.Text != KeyCatalog.Get(id).Name
                || count.Text != expectedCount.ToString("N0") || share.Text != expectedShare.ToString("P1")
                || date.Text != expected.Date || !countLabel.Text.Contains("点击") || !shareLabel.Text.Contains("鼠标点击"))
                throw new Exception($"Wrong mouse hover content for {id} on {expected.Date}: '{name.Text}', '{count.Text}', '{share.Text}'");
        }

        void RaiseMouse(FrameworkElement target, RoutedEvent routedEvent) =>
            target.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = routedEvent });

        foreach (var button in buttons)
        {
            var id = (string)button.Tag;
            RaiseMouse(button, Mouse.MouseEnterEvent);
            ConfirmMouseStats(id, day);
            RaiseMouse(button, Mouse.MouseLeaveEvent);
            if (card.Visibility != Visibility.Collapsed) throw new Exception("Leaving a mouse button did not hide its card");
        }

        var rightButton = (MouseRegionButton)buttons.Single(button => button.Tag as string == "MouseRight");
        var bounds = rightButton.Shape.Bounds;
        var pointer = rightButton.TranslatePoint(new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2), layer);
        RaiseMouse(rightButton, Mouse.MouseMoveEvent);
        ConfirmMouseStats("MouseRight", day);
        window.ShowKeyHover("MouseRight", pointer); Pump(window);
        var cardPosition = card.TranslatePoint(new Point(), layer);
        if (cardPosition.X < 0 || cardPosition.X + card.ActualWidth > pointer.X)
            throw new Exception("The mouse card did not flip to stay visible beside the right-side model");
        Capture(window, root, "mouse-key-hover-1280.png");

        var empty = today.AddDays(-20);
        picker.SelectedDate = empty.ToDateTime(TimeOnly.MinValue); Pump(window);
        ConfirmMouseStats("MouseRight", DayStats.Empty(empty));
        picker.SelectedDate = today.ToDateTime(TimeOnly.MinValue); Pump(window);
        RaiseMouse(rightButton, Mouse.MouseLeaveEvent);

        var summary = (Panel)Find<MouseHeatmap>(window, "MouseButtons").Summary;
        foreach (var key in MouseCatalog.All)
        {
            var row = summary.Children.Cast<Border>().Single(item => Descendants<TextBlock>(item).Any(label => label.Text == key.Label));
            RaiseMouse(row, Mouse.MouseEnterEvent);
            ConfirmMouseStats(key.Id, day);
            RaiseMouse(row, Mouse.MouseLeaveEvent);
            if (card.Visibility != Visibility.Collapsed) throw new Exception("Leaving a mouse summary did not hide its card");
        }

        Console.WriteLine("PASS mouse hover: five model buttons and summaries, mouse-specific counts and shares, date refresh, entry/move/exit and right-edge fit");
    }

    private static void VerifyHiddenNumpad(Window window, StatsController controller, string root)
    {
        var date = DateOnly.FromDateTime(DateTime.Today.AddDays(-36)); var day = DayStats.Empty(date);
        day.Keys["Num7"] = 37; day.Hours[12] = 37; day.ActiveMinutes.Add(720); controller.Store.Merge([day]);
        Find<DatePicker>(window, "DayPicker").SelectedDate = date.ToDateTime(TimeOnly.MinValue); Pump(window);
        if (Find<TextBlock>(window, "KeyboardTotalValue").Text != "37" || Descendants<KeyboardRegionButton>(Find<KeyboardHeatmap>(window, "Keyboard")).Any(b => b.HeatLevel != 0))
            throw new Exception("Removing numpad artwork changed recorded keyboard totals");
        Find<Button>(window, "AllKeyboardKeysButton").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Find<TextBox>(window, "KeySearch").Text = "Num7"; Pump(window);
        if (!Descendants<TextBlock>(Find<WrapPanel>(window, "AllKeysList")).Any(t => t.Text.Contains("小键盘 7") && t.Text.Contains("37")))
            throw new Exception("Hidden numpad count is not accessible from All keys");
        Capture(window, root, "numpad-details-1280.png");
        Find<Button>(window, "CloseAllKeysButton").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Find<TextBox>(window, "KeySearch").Clear(); Find<DatePicker>(window, "DayPicker").SelectedDate = DateTime.Today; Pump(window);
    }
    private static void CaptureKeyboardStates(Window window, StatsController controller, string root)
    {
        var date = DateOnly.FromDateTime(DateTime.Today.AddDays(-37)); var day = DayStats.Empty(date);
        var keyboard = Find<KeyboardHeatmap>(window, "Keyboard"); var buttons = Descendants<KeyboardRegionButton>(keyboard).ToArray();
        var counts = new long[] { 0, 1, 16, 50, 100 };
        for (var i = 0; i < buttons.Length; i++) day.Keys[(string)buttons[i].Tag] = counts[i % 5];
        day.Hours[12] = day.Keys.Values.Sum(); day.ActiveMinutes.Add(720); controller.Store.Merge([day]);
        Find<DatePicker>(window, "DayPicker").SelectedDate = date.ToDateTime(TimeOnly.MinValue); Pump(window); ShowMouse(window);
        foreach (var button in buttons) ForceVisualState(button, typeof(UIElement), "IsMouseOverPropertyKey", true);
        Pump(window); Capture(window, root, "keyboard-hover-five-levels-1280.png");
        foreach (var button in buttons) ForceVisualState(button, typeof(System.Windows.Controls.Primitives.ButtonBase), "IsPressedPropertyKey", true);
        Pump(window); Capture(window, root, "keyboard-pressed-five-levels-1280.png");
        foreach (var button in buttons)
        {
            var material = (Image)button.Template.FindName("Material", button);
            if (button.Opacity != 1 || material.Opacity != 1) throw new Exception("Keyboard interaction dims heat or text");
            ForceVisualState(button, typeof(System.Windows.Controls.Primitives.ButtonBase), "IsPressedPropertyKey", false);
            ForceVisualState(button, typeof(UIElement), "IsMouseOverPropertyKey", false);
        }
        Find<DatePicker>(window, "DayPicker").SelectedDate = DateTime.Today; Pump(window);
    }
    private static void CaptureMouseSides(Window window, StatsController controller, Button[] buttons, string root)
    {
        foreach (var (id, offset, file) in new[] { ("MouseLeft", -34, "mouse-left-only-1280.png"), ("MouseRight", -35, "mouse-right-only-1280.png") })
        {
            var date = DateOnly.FromDateTime(DateTime.Today.AddDays(offset));
            var day = DayStats.Empty(date); day.Add(id, date.ToDateTime(new TimeOnly(12, 0)));
            controller.Store.Merge([day]);
            Find<DatePicker>(window, "DayPicker").SelectedDate = date.ToDateTime(TimeOnly.MinValue); Pump(window);
            var selected = buttons.Single(b => b.Tag as string == id);
            selected.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            foreach (var button in buttons.Cast<MouseRegionButton>())
                if (button.HeatLevel != ((string)button.Tag == id ? 4 : 0)) throw new Exception("Single-side heat reached the wrong surface");
            if (!Find<TextBlock>(window, "MouseDetail").Text.Contains("100.0%")) throw new Exception("Single-side share is incorrect");
            ShowMouse(window); Capture(window, root, file);
        }
        Find<DatePicker>(window, "DayPicker").SelectedDate = DateTime.Today; Pump(window);
    }
    private static void ForceVisualState(DependencyObject control, Type owner, string keyName, bool value)
    {
        // Isolate visual-state testing inside this WPF process, without moving or clicking the OS pointer.
        var key = (DependencyPropertyKey)(owner.GetField(keyName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null)
            ?? throw new Exception("WPF visual-state key is unavailable: " + keyName));
        control.SetValue(key, value);
    }
    private static void VerifyMouseMaterials(MouseHeatmap model, Button[] buttons, DayStats original)
    {
        var sample = DayStats.Empty(DateOnly.FromDateTime(DateTime.Today));
        var values = new long[] { 100, 25, 4, 1, 0 }; var levels = new[] { 4, 2, 1, 1, 0 };
        for (var i = 0; i < values.Length; i++) sample.Keys[MouseCatalog.All[i].Id] = values[i];
        sample.Hours[12] = values.Sum(); model.Update(sample);
        var materials = buttons.Cast<MouseRegionButton>().Select(b => b.HeatTexture).ToArray();
        for (var i = 0; i < buttons.Length; i++)
        {
            var button = (MouseRegionButton)buttons[i]; var texture = button.HeatTexture;
            if (button.HeatLevel != levels[i] || !texture.IsFrozen) throw new Exception("The mouse material has an incorrect level or is mutable");
            var pixels = new byte[texture.PixelWidth * texture.PixelHeight * 4]; texture.CopyPixels(pixels, texture.PixelWidth * 4, 0);
            var tones = new HashSet<int>();
            for (var p = 0; p < pixels.Length; p += 4 * 13)
                if (pixels[p + 3] > 240) tones.Add(pixels[p] | (pixels[p + 1] << 8) | (pixels[p + 2] << 16));
            if (tones.Count < 16) throw new Exception("Mouse material lost its surface lighting and texture");
        }
        sample.Keys["MouseLeft"]++; sample.Hours[12]++; model.Update(sample);
        for (var i = 0; i < buttons.Length; i++)
            if (!ReferenceEquals(materials[i], ((MouseRegionButton)buttons[i]).HeatTexture)) throw new Exception("A count update unnecessarily regenerated a mouse texture");
        model.Update(original);
    }

    private static void VerifyMouseContrast(MouseHeatmap model, Button[] buttons, string expectedBorder)
    {
        foreach (var button in buttons)
        {
            var shell = (System.Windows.Shapes.Path)button.Template.FindName("Shell", button);
            if (shell.Opacity != 1 || button.Opacity != 1 || ((SolidColorBrush)shell.Stroke).Color != (Color)ColorConverter.ConvertFromString(expectedBorder)) throw new Exception("Mouse interaction state dims content or lacks border feedback");
            var material = (Image)button.Template.FindName("Material", button);
            if (material.Opacity != 1) throw new Exception("Mouse material is faded during interaction");
        }
        // Text sits beside the textured object. Check against the darker selected-row background.
        foreach (var label in Descendants<TextBlock>(model.Summary))
        {
            var a = Luminance(((SolidColorBrush)label.Foreground).Color);
            var b = Luminance((Color)ColorConverter.ConvertFromString("#D8E2EE"));
            if ((Math.Max(a, b) + .05) / (Math.Min(a, b) + .05) < 4.5) throw new Exception("Mouse label contrast is below 4.5:1");
        }
    }
    private static double Luminance(Color color)
    {
        static double Channel(byte value) { var c = value / 255d; return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4); }
        return .2126 * Channel(color.R) + .7152 * Channel(color.G) + .0722 * Channel(color.B);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static void Pump(Window window)
    {
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }
    private static void WaitUntil(Window window, Func<bool> ready, int seconds = 5)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        do { Pump(window); if (ready()) return; Thread.Sleep(5); } while (DateTime.UtcNow < deadline);
        throw new Exception($"Asynchronous history loading timed out; rows={Find<DataGrid>(window, "HistoryTable").Items.Count}; notice={Find<TextBlock>(window, "NoticeText").Text}");
    }
    private static void Capture(Window window, string directory, string file)
    {
        var content = (FrameworkElement)window.Content;
        CaptureVisual(content, directory, file);
    }
    private static void CaptureVisual(FrameworkElement content, string directory, string file)
    {
        // Render only this test process's own WPF visual. This never captures the user's desktop or other apps.
        var dpi = VisualTreeHelper.GetDpi(content);
        var image = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(content.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        image.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(Path.Combine(directory, file)); encoder.Save(stream);
        Console.WriteLine("RENDER " + file);
    }
}

using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using KeyTrail.Desktop.Controls;

namespace KeyTrail.Desktop;

public partial class MainWindow : Window
{
    private readonly StatsController _controller;
    private readonly DispatcherTimer _refresh;
    private DateOnly _date = DateOnly.FromDateTime(DateTime.Now);
    private DateOnly _lastToday = DateOnly.FromDateTime(DateTime.Now);
    private DayStats _day = DayStats.Empty(DateOnly.FromDateTime(DateTime.Now));
    private string? _selectedKey;
    private string? _hoveredKey;
    private DateOnly? _hoveredActivityDate;
    private bool _loaded;
    private bool _updatingSettings;
    private int _page;
    private readonly double[] _scrollOffsets = new double[3];
    private bool _closed;
    private long _renderedRevision = -1;
    private DateOnly _renderedDate;
    private long _historyRevision = -1;
    private DateOnly _historyDate;
    private long _lastHistoryRead;
    private bool _historyLoading;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ObservableCollection<HistoryRow> _historyRows = new();
    private readonly Dictionary<DateOnly, HistoryRow> _historyByDate = new();
    private readonly List<RankVisual> _rankRows = new();
    private readonly Dictionary<string, (Border Container, TextBlock Text, long Count)> _keyChips = new();
    private const int KeysPerPage = 20;
    private int _keysPage;
    private FrameworkElement? _allKeysTrigger;
    private bool _exporting;
    private IReadOnlyList<DaySummary> _activitySummaries = Array.Empty<DaySummary>();
    private int _activityRangeDays = 365;
    private string? _uiNotice;
    private DateTime _noticeUntil;
    public bool AllowClose { get; set; }

    public MainWindow(StatsController controller)
    {
        _controller = controller;
        InitializeComponent();
        KeyHoverBackdropBrush.Visual = DashboardSurface;
        Width = Math.Min(1280, SystemParameters.WorkArea.Width - 48);
        Height = Math.Min(900, SystemParameters.WorkArea.Height - 48);
        DayPicker.DisplayDateEnd = DateTime.Today;
        DayPicker.DisplayDateStart = new DateTime(1970, 1, 1);
        DayPicker.SelectedDate = DateTime.Today;
        RangeStartPicker.DisplayDateEnd = RangeEndPicker.DisplayDateEnd = DateTime.Today;
        RangeStartPicker.SelectedDate = DateTime.Today.AddDays(-6);
        RangeEndPicker.SelectedDate = DateTime.Today;
        Keyboard.KeySelected += id => { _selectedKey = id; UpdateKeyDetail(); };
        Keyboard.KeyHovered += OnKeyHovered;
        MouseButtons.ButtonHovered += OnKeyHovered;
        Deactivated += (_, _) => HideKeyHover();
        KeyHoverLayer.SizeChanged += (_, _) => HideKeyHover();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) HideKeyHover(); };
        MouseButtons.ButtonSelected += _ => UpdateMouseDetail();
        HourlyChart.HourSelected += (hour, count) => HourlyEmpty.Text = $"{hour:00}:00–{hour + 1:00}:00 · {count:N0} 次键鼠操作";
        ActivityChart.DetailChanged += detail => ActivityDetail.Text = detail;
        ActivityChart.DateSelected += OpenDate;
        ActivityChart.DateHovered += OnActivityHovered;
        ActivityLegend.ItemsSource = ActivityCalendar.ActivityColors;
        _refresh = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _refresh.Tick += (_, _) => Refresh();
        BuildRankRows();
        FitInputModels();
        _historyView = CollectionViewSource.GetDefaultView(_historyRows);
        _historyView.Filter = item => _historyRangeValid && item is HistoryRow row && _historyQuery.Includes(row.Summary);
        HistoryTable.ItemsSource = _historyView;
        PageScroll.SizeChanged += (_, _) => UpdateHistoryViewport();
        PageScroll.ScrollChanged += (_, e) => { if (_hoveredActivityDate is not null && e.VerticalChange != 0) HideKeyHover(); };
        HistoryRecordsPanel.SizeChanged += (_, _) => UpdateHistoryViewport();
        RenderActivity();
        _loaded = true;
        if (_controller.IsDemo) { Title += " · 演示数据"; PauseButton.IsEnabled = false; }
        Closing += OnClosing;
        IsVisibleChanged += (_, _) => UpdateActivity();
        StateChanged += (_, _) => UpdateActivity();
        Closed += (_, _) => { _closed = true; _refresh.Stop(); _lifetime.Cancel(); _lifetime.Dispose(); };
    }

    private void UpdateActivity()
    {
        if (_closed) return;
        if (IsVisible && WindowState != WindowState.Minimized) { Refresh(force: true); _refresh.Start(); }
        else
        {
            _refresh.Stop();
            HideKeyHover();
        }
    }

    public void Refresh(bool force = false)
    {
        if (!_loaded || _closed || !IsVisible || WindowState == WindowState.Minimized) return;
        _controller.Collect();
        var today = DateOnly.FromDateTime(DateTime.Now);
        var dateChanged = today != _lastToday;
        if (dateChanged)
        {
            if (_date == _lastToday) { _date = today; DayPicker.SelectedDate = today.ToDateTime(TimeOnly.MinValue); }
            _lastToday = today; DayPicker.DisplayDateEnd = today.ToDateTime(TimeOnly.MinValue);
        }
        var error = _controller.Error;
        StatusText.Text = _controller.IsDemo ? "演示模式" : error is not null ? "需要处理" : _controller.Accumulator.Paused ? "已暂停" : "正在统计";
        StatusDot.Fill = Brush(error is not null ? "#F4AA87" : _controller.Accumulator.Paused ? "#F0D692" : "#3B608F");
        PauseButton.Content = _controller.Accumulator.Paused ? "继续统计" : "暂停统计";
        var recoveryNotice = _controller.Store.RecoveryNotice ?? _controller.SettingsRecoveryNotice;
        NoticeText.Text = error ?? (DateTime.Now < _noticeUntil ? _uiNotice : null) ?? (_controller.IsDemo ? "演示数据 · 仅用于界面预览，未监听键盘或鼠标，数据与真实统计完全隔离。" : recoveryNotice ?? "");
        NoticeBanner.Visibility = NoticeText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        FooterLabel.Text = _controller.Accumulator.Paused && !_controller.IsDemo ? "统计已暂停 · 点击「继续统计」恢复" : "统计会自动跟随本机日期归档";
        UpdateDateToolbar(today);
        try
        {
            var revision = _controller.Store.Revision;
            if (_page == 0 && (force || _renderedRevision != revision || _renderedDate != _date))
            {
                _day = _controller.Store.GetDay(_date);
                RenderDay();
                _renderedRevision = revision; _renderedDate = _date;
            }
            if (_page == 1)
            {
                if (dateChanged) RenderActivity();
                if ((force || _historyRevision != revision || _historyDate != today)
                    && (force || Environment.TickCount64 - _lastHistoryRead >= 5000)) _ = RenderHistoryAsync();
            }
        }
        catch (Exception ex) { ShowInlineError(ex.Message); }
    }

    private void RenderDay()
    {
        TotalValue.Text = _day.Total.ToString("N0");
        KeyboardTotalValue.Text = _day.KeyboardTotal.ToString("N0");
        MouseTotalValue.Text = _day.MouseTotal.ToString("N0");
        KeyboardCountHint.Text = $"用过 {_day.UsedKeyboardKeys:N0} 种键盘按键";
        MinutesValue.Text = _day.ActiveMinutes.Count.ToString("N0");
        var isToday = _date == DateOnly.FromDateTime(DateTime.Now);
        long yesterday = 0;
        try { yesterday = _controller.Store.GetSummary(_date.AddDays(-1)).Total; }
        catch (InvalidDataException ex) { ShowInlineError(ex.Message); }
        CompareLabel.Text = _day.Total == 0 ? "这一天尚未记录操作" : yesterday == 0 ? "这一天的全部操作" : $"较前一天 {(_day.Total >= yesterday ? "+" : "")}{((double)_day.Total / yesterday - 1):P0}";
        Keyboard.Update(_day);
        MouseButtons.Update(_day);
        UpdateMouseDetail();
        var peak = Array.IndexOf(_day.Hours, _day.Hours.Max());
        HourlyEmpty.Text = _day.Total == 0 ? (isToday ? "开始使用键鼠后，热力格会逐渐点亮" : "当天没有记录。") : $"高峰 {peak:00}:00–{peak + 1:00}:00 · {_day.Hours[peak]:N0} 次键鼠操作";
        if (_day.Total > 0 && HourlyChart.SelectedHour is var selectedHour && selectedHour >= 0)
            HourlyEmpty.Text = $"{selectedHour:00}:00–{selectedHour + 1:00}:00 · {_day.Hours[selectedHour]:N0} 次键鼠操作";
        HourlyChart.SetData(_day.Hours);
        RankEmpty.Text = isToday ? "还没有键盘记录。打开任意应用，开始输入即可。" : "当天没有键盘记录。可选择其他日期查看。";
        RenderRank(); UpdateKeyDetail();
        UpdateKeyHoverStats();
        if (AllKeysOverlay.Visibility == Visibility.Visible) RenderAllKeys();
    }

    private void BuildRankRows()
    {
        for (var i = 0; i < 5; i++)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(23) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
            row.Children.Add(new TextBlock { Text = (i + 1).ToString(), Foreground = Brush("#5A6072"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            var name = new TextBlock { FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(name, 1); row.Children.Add(name);
            var share = new TextBlock { FontSize = 13, Foreground = Brush("#5A6072"), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(4, 0, 12, 0) };
            Grid.SetColumn(share, 2); row.Children.Add(share);
            var count = new TextBlock { FontSize = 13, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Segoe UI") };
            Grid.SetColumn(count, 3); row.Children.Add(count);
            var entry = new Border { Child = row, BorderBrush = Brush("#E8EAEE"), BorderThickness = new Thickness(0, 1, 0, 0) };
            TopKeysPanel.Children.Add(entry);
            _rankRows.Add(new(entry, name, count, share));
        }
    }

    private void FitInputModels()
    {
        var columns = InputModelPair.ColumnDefinitions;
        columns[0].Width = new GridLength(Keyboard.ArtworkAspectRatio, GridUnitType.Star);
        columns[2].Width = new GridLength(MouseButtons.ArtworkAspectRatio, GridUnitType.Star);
        var pairAspectRatio = Keyboard.ArtworkAspectRatio + MouseButtons.ArtworkAspectRatio;
        var gap = columns[1].Width.Value;
        InputModels.SizeChanged += (_, e) => InputModelPair.Width = Math.Min(e.NewSize.Width, e.NewSize.Height * pairAspectRatio + gap);
    }

    private void RenderRank()
    {
        RankEmpty.Visibility = _day.KeyboardTotal > 0 ? Visibility.Collapsed : Visibility.Visible;
        var top = _day.Keys.Where(x => !MouseCatalog.IsMouse(x.Key)).OrderByDescending(x => x.Value).ThenBy(x => x.Key).Take(5).ToArray();
        for (var i = 0; i < _rankRows.Count; i++)
        {
            var row = _rankRows[i];
            row.Container.Visibility = i < top.Length ? Visibility.Visible : Visibility.Collapsed;
            if (i >= top.Length) continue;
            row.Name.Text = KeyCatalog.Get(top[i].Key).Name;
            row.Count.Text = top[i].Value.ToString("N0");
            row.Share.Text = ((double)top[i].Value / Math.Max(1, _day.KeyboardTotal)).ToString("P1");
        }
    }

    private void UpdateKeyDetail()
    {
        if (_selectedKey is null) return;
        var count = _day.Keys.GetValueOrDefault(_selectedKey);
        KeyDetail.Text = $"{KeyCatalog.Get(_selectedKey).Name}   ·   {count:N0} 次   ·   占键盘敲击 {(_day.KeyboardTotal == 0 ? 0 : (double)count / _day.KeyboardTotal):P1}";
    }

    private void OnKeyHovered(string? id, MouseEventArgs e)
    {
        if (id is null) HideKeyHover();
        else ShowKeyHover(id, e.GetPosition(KeyHoverLayer));
    }

    internal void ShowKeyHover(string id, Point position)
    {
        if (!OverviewPage.IsVisible || WindowState == WindowState.Minimized) return;
        if (_hoveredKey != id)
        {
            _hoveredKey = id;
            _hoveredActivityDate = null;
            var isFn = id == "Fn";
            var isMouse = MouseCatalog.IsMouse(id);
            KeyHoverName.Text = isFn ? "Fn" : KeyCatalog.Get(id).Name;
            KeyHoverCountLabel.Text = isMouse ? "点击次数" : "按下次数";
            KeyHoverShareLabel.Text = isMouse ? "占当日鼠标点击" : "占当日键盘敲击";
            KeyHoverStats.Visibility = isFn ? Visibility.Collapsed : Visibility.Visible;
            KeyHoverUnavailable.Visibility = isFn ? Visibility.Visible : Visibility.Collapsed;
            UpdateKeyHoverStats();
        }
        PositionHoverCard(position);
    }

    private void OnActivityHovered(DateOnly? date, MouseEventArgs e)
    {
        if (date is { } day) ShowActivityHover(day, e.GetPosition(KeyHoverLayer));
        else if (_hoveredActivityDate is not null) HideKeyHover();
    }

    internal void ShowActivityHover(DateOnly date, Point position)
    {
        if (!HistoryPage.IsVisible || WindowState == WindowState.Minimized) return;
        if (_hoveredActivityDate != date)
        {
            _hoveredKey = null;
            _hoveredActivityDate = date;
            if (!UpdateActivityHoverStats(date)) { HideKeyHover(); return; }
            KeyHoverStats.Visibility = Visibility.Visible;
            KeyHoverUnavailable.Visibility = Visibility.Collapsed;
        }
        PositionHoverCard(position);
    }

    private bool UpdateActivityHoverStats(DateOnly date)
    {
        if (!ActivityChart.TryGetDay(date, out var count, out var previousCount)) return false;
        KeyHoverName.Text = date.ToString("ddd", Chinese);
        KeyHoverDate.Text = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        KeyHoverCountLabel.Text = _historyDevice switch { InputKind.Keyboard => "键盘敲击", InputKind.Mouse => "鼠标点击", _ => "键鼠操作" };
        KeyHoverCount.Text = count.ToString("N0");
        KeyHoverShareLabel.Text = "较前一天";
        var change = count - previousCount;
        var sign = change > 0 ? "+" : "-";
        KeyHoverShare.Text = change == 0 ? "持平" : previousCount == 0
            ? $"+{count:N0} 次 · 前日 0"
            : $"{sign}{Math.Abs(change):N0} 次 · {sign}{Math.Abs((double)change / previousCount):P1}";
        return true;
    }

    private void PositionHoverCard(Point position)
    {
        KeyHoverCard.Visibility = Visibility.Visible;
        KeyHoverCard.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = KeyHoverCard.DesiredSize;
        var x = PlaceHoverAxis(position.X, size.Width, KeyHoverLayer.ActualWidth);
        var y = PlaceHoverAxis(position.Y, size.Height, KeyHoverLayer.ActualHeight);
        Canvas.SetLeft(KeyHoverCard, x);
        Canvas.SetTop(KeyHoverCard, y);
        KeyHoverGlassOutline.Rect = new Rect(size);

        // Sample the dashboard beneath the card with room for the blur at its edges.
        const double blurPadding = 12;
        var backdrop = new Rect(new Point(x, y), size);
        backdrop.Inflate(blurPadding, blurPadding);
        KeyHoverBackdropBrush.Viewbox = backdrop;
    }

    private static double PlaceHoverAxis(double pointer, double length, double limit)
    {
        const double gap = 16;
        const double inset = 12;
        var afterPointer = pointer + gap;
        var end = Math.Max(inset, limit - length - inset);
        return Math.Clamp(afterPointer <= end ? afterPointer : pointer - gap - length, inset, end);
    }

    internal void HideKeyHover()
    {
        _hoveredKey = null;
        _hoveredActivityDate = null;
        KeyHoverCard.Visibility = Visibility.Collapsed;
    }

    private void UpdateKeyHoverStats()
    {
        if (_hoveredKey is not { } id) return;
        KeyHoverDate.Text = _day.Date;
        if (id == "Fn") return;
        var count = _day.Keys.GetValueOrDefault(id);
        var total = MouseCatalog.IsMouse(id) ? _day.MouseTotal : _day.KeyboardTotal;
        KeyHoverCount.Text = count.ToString("N0");
        KeyHoverShare.Text = (total == 0 ? 0 : (double)count / total).ToString("P1");
    }

    private void UpdateMouseDetail()
    {
        if (MouseButtons.SelectedButton is not { } id)
        { MouseDetail.Text = _day.MouseTotal > 0 ? "选择一个鼠标部位，查看次数与占比" : "当天没有鼠标记录 · 统计从启用鼠标功能后开始积累"; return; }
        var count = _day.Keys.GetValueOrDefault(id);
        MouseDetail.Text = $"{KeyCatalog.Get(id).Name}   ·   {count:N0} 次   ·   占鼠标点击 {(_day.MouseTotal == 0 ? 0 : (double)count / _day.MouseTotal):P1}";
    }

    private async Task RenderHistoryAsync()
    {
        if (_historyLoading || _closed) return;
        _historyLoading = true; _lastHistoryRead = Environment.TickCount64;
        var today = DateOnly.FromDateTime(DateTime.Now);
        var revision = _controller.Store.Revision;
        var token = _lifetime.Token;
        try
        {
            if (_historyRows.Count == 0) HistoryCount.Text = "正在读取…";
            var summaries = await Task.Run(() => _controller.Store.GetSummaries(token), token);
            if (_closed || _page != 1) return;
            _activitySummaries = summaries;
            RenderActivity();
            foreach (var summary in summaries)
            {
                if (_historyByDate.TryGetValue(summary.Date, out var existing)) existing.Update(summary);
                else
                {
                    var row = new HistoryRow(summary) { Device = _historyDevice }; _historyByDate[summary.Date] = row;
                    if (_historyRows.Count == 0 || _historyRows[^1].Date >= row.Date) _historyRows.Add(row);
                    else
                    {
                        var index = 0; while (index < _historyRows.Count && _historyRows[index].Date > row.Date) index++;
                        _historyRows.Insert(index, row);
                    }
                }
            }
            ApplyHistoryFilters();
            _historyRevision = revision; _historyDate = today;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_closed) ShowInlineError(ex.Message); }
        finally { _historyLoading = false; }
    }

    private void RenderActivity()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var kind = new HistoryQuery(_historyDevice, null, today);
        if (ActivityChart.LastDate != today && _hoveredActivityDate is not null) HideKeyHover();
        ActivityChart.SetData(_activitySummaries, today, _historyDevice, _activityRangeDays);
        var first = ActivityChart.FirstDate;
        var days = _activitySummaries.Where(d => d.Date >= first && d.Date <= today && kind.Count(d) > 0).OrderBy(d => d.Date).ToArray();
        ActivityRangeLabel.Text = $"{first:yyyy/MM/dd} — {today:yyyy/MM/dd}";
        ActivityTotal.Text = days.Sum(kind.Count).ToString("N0");
        ActivityTotalLabel.Text = $"区间{DeviceLabel}次数";
        ActivityDays.Text = days.Length.ToString("N0");
        var longest = 0; var streak = 0; DateOnly? previous = null;
        foreach (var day in days) { streak = previous?.AddDays(1) == day.Date ? streak + 1 : 1; longest = Math.Max(longest, streak); previous = day.Date; }
        ActivityStreak.Text = longest.ToString("N0");
        if (days.Length == 0) ActivityDetail.Text = $"这段时间还没有{DeviceLabel}记录";
        if (_hoveredActivityDate is { } date && !UpdateActivityHoverStats(date)) HideKeyHover();
    }

    private void Activity_Click(object sender, RoutedEventArgs e) { HistoryNav.IsChecked = true; PageScroll.ScrollToTop(); }
    private void OpenDate(DateOnly date) { DayPicker.SelectedDate = date.ToDateTime(TimeOnly.MinValue); OverviewNav.IsChecked = true; }

    private void RenderAllKeys()
    {
        var query = KeySearch.Text.Trim();
        var matches = _day.Keys
            .Where(entry => query.Length == 0 || MatchesKey(KeyCatalog.Get(entry.Key), query))
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .ToArray();
        var pageCount = Math.Max(1, (matches.Length + KeysPerPage - 1) / KeysPerPage);
        _keysPage = Math.Clamp(_keysPage, 0, pageCount - 1);
        AllKeysDate.Text = _day.Date;
        PreviousKeysPageButton.IsEnabled = _keysPage > 0;
        NextKeysPageButton.IsEnabled = _keysPage + 1 < pageCount;
        KeyPageLabel.Text = $"第 {_keysPage + 1} / {pageCount} 页 · {matches.Length:N0} 个按键";
        NoKeyResults.Visibility = matches.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        var visible = new List<UIElement>(KeysPerPage);
        foreach (var entry in matches.Skip(_keysPage * KeysPerPage).Take(KeysPerPage))
        {
            if (!_keyChips.TryGetValue(entry.Key, out var chip))
            {
                var label = new TextBlock { FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
                var item = new Border { Background = Brush("#EEF0F3"), CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(4), Child = label };
                chip = (item, label, -1);
            }
            if (chip.Count != entry.Value)
            {
                var name = KeyCatalog.Get(entry.Key).Name;
                chip.Text.Text = $"{name}\n{entry.Value:N0} 次";
                chip.Container.ToolTip = $"{name} · {entry.Value:N0} 次";
            }
            _keyChips[entry.Key] = (chip.Container, chip.Text, entry.Value);
            visible.Add(chip.Container);
        }
        if (AllKeysList.Children.Cast<UIElement>().SequenceEqual(visible)) return;
        AllKeysList.Children.Clear();
        foreach (var item in visible) AllKeysList.Children.Add(item);
    }

    private static bool MatchesKey(KeyDefinition key, string query) =>
        key.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
        || key.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
        || key.Category.Contains(query, StringComparison.OrdinalIgnoreCase);

    private void Navigate(int page)
    {
        if (!_loaded) return;
        if (_page != page)
        {
            if (_page != 0) _scrollOffsets[_page] = PageScroll.VerticalOffset;
            HideKeyHover();
        }
        _page = page;
        if (page != 0) CloseAllKeys(returnFocus: false);
        OverviewPage.Visibility = page == 0 ? Visibility.Visible : Visibility.Collapsed;
        PageScroll.Visibility = page == 0 ? Visibility.Collapsed : Visibility.Visible;
        HistoryPage.Visibility = page == 1 ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == 2 ? Visibility.Visible : Visibility.Collapsed;
        DateControls.Visibility = page == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryControls.Visibility = page == 1 ? Visibility.Visible : Visibility.Collapsed;
        ExportButton.Visibility = page == 2 ? Visibility.Collapsed : Visibility.Visible;
        ExportButton.Content = page == 1 ? "导出筛选结果" : "导出 CSV";
        ExportButton.IsEnabled = !_exporting && (page != 1 || _historyRangeValid);
        PageTitle.Text = new[] { "每日概览", "历史记录", "偏好设置" }[page];
        PageSubtitle.Visibility = page == 0 ? Visibility.Collapsed : Visibility.Visible;
        PageSubtitle.Text = page == 1 ? "每一天的积累，都有迹可循" : "让键盘统计器按你的习惯运行";
        try
        {
            Refresh(force: true);
            if (page == 2)
            {
                _updatingSettings = true;
                RepeatsCheck.IsChecked = _controller.Settings.CountRepeats;
                TrayCheck.IsChecked = _controller.Settings.CloseToTray;
                StartupCheck.IsChecked = !_controller.IsDemo && StartupRegistration.Enabled;
                StartupCheck.IsEnabled = !_controller.IsDemo;
                DataPathBox.Text = _controller.DataRoot;
                _updatingSettings = false;
            }
        }
        catch (Exception ex) { ShowInlineError(ex.Message); }
        finally { _updatingSettings = false; }
        if (page != 0) PageScroll.ScrollToVerticalOffset(_scrollOffsets[page]);
    }

    private void Overview_Click(object sender, RoutedEventArgs e) => Navigate(0);
    private void History_Click(object sender, RoutedEventArgs e) => Navigate(1);
    private void Settings_Click(object sender, RoutedEventArgs e) => Navigate(2);
    private void PreviousDay_Click(object sender, RoutedEventArgs e) { if (_date > new DateOnly(1970, 1, 1)) DayPicker.SelectedDate = _date.AddDays(-1).ToDateTime(TimeOnly.MinValue); }
    private void NextDay_Click(object sender, RoutedEventArgs e) { if (_date < DateOnly.FromDateTime(DateTime.Now)) DayPicker.SelectedDate = _date.AddDays(1).ToDateTime(TimeOnly.MinValue); }
    private void Today_Click(object sender, RoutedEventArgs e) => DayPicker.SelectedDate = DateTime.Today;
    private void Yesterday_Click(object sender, RoutedEventArgs e) => DayPicker.SelectedDate = DateTime.Today.AddDays(-1);
    private void DayPicker_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (!_loaded) return;
        if (DayPicker.SelectedDate is not DateTime selected) { DayPicker.SelectedDate = _date.ToDateTime(TimeOnly.MinValue); return; }
        _date = DateOnly.FromDateTime(selected > DateTime.Today ? DateTime.Today : selected); Refresh();
    }
    private void Pause_Click(object sender, RoutedEventArgs e)
    { try { _controller.SetPaused(!_controller.Accumulator.Paused); Refresh(); } catch (Exception ex) { ShowInlineError(ex.Message); } }

    private void AllKeys_Click(object sender, RoutedEventArgs e)
    {
        if (AllKeysOverlay.Visibility == Visibility.Visible) { CloseAllKeys(); return; }
        _allKeysTrigger = sender as FrameworkElement;
        ShowAllKeys();
    }

    private void ShowAllKeys()
    {
        HideKeyHover();
        _keysPage = 0;
        DashboardSurface.IsEnabled = false;
        AllKeysOverlay.Visibility = Visibility.Visible;
        RenderAllKeys();
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (AllKeysOverlay.IsVisible) KeySearch.Focus();
        }));
    }

    private void CloseAllKeys(bool returnFocus = true)
    {
        if (AllKeysOverlay.Visibility != Visibility.Visible) return;
        AllKeysOverlay.Visibility = Visibility.Collapsed;
        DashboardSurface.IsEnabled = true;
        if (returnFocus) (_allKeysTrigger ?? AllKeyboardKeysButton).Focus();
        _allKeysTrigger = null;
    }

    private void AllKeys_Close(object sender, RoutedEventArgs e) => CloseAllKeys();
    private void AllKeys_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        CloseAllKeys();
        e.Handled = true;
    }
    private void KeySearch_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_loaded || AllKeysOverlay.Visibility != Visibility.Visible) return;
        _keysPage = 0;
        RenderAllKeys();
    }
    private void PreviousKeysPage_Click(object sender, RoutedEventArgs e) { _keysPage--; RenderAllKeys(); }
    private void NextKeysPage_Click(object sender, RoutedEventArgs e) { _keysPage++; RenderAllKeys(); }

    private void History_Open(object sender, RoutedEventArgs e)
    {
        if (HistoryTable.SelectedItem is not HistoryRow row) return;
        OpenDate(row.Date);
    }
    private void History_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) History_Open(sender, e); }

    private void Settings_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingSettings) return;
        try
        {
            _controller.Settings.CountRepeats = RepeatsCheck.IsChecked == true;
            _controller.Settings.CloseToTray = TrayCheck.IsChecked == true;
            _controller.UpdateSettings(); SaveLabel.Text = "设置已保存";
        }
        catch (Exception ex) { ShowInlineError("设置未能保存：" + ex.Message); }
    }
    private void Startup_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingSettings) return;
        try { StartupRegistration.SetEnabled(StartupCheck.IsChecked == true, _controller.DataRoot); SaveLabel.Text = "开机启动设置已保存"; }
        catch (Exception ex) { StartupCheck.IsChecked = StartupRegistration.Enabled; ShowInlineError(ex.Message); }
    }
    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    { try { Process.Start(new ProcessStartInfo(_controller.DataRoot) { UseShellExecute = true }); } catch (Exception ex) { ShowInlineError(ex.Message); } }
    private void Export_Click(object sender, RoutedEventArgs e) => Export(_page == 1, _page == 1);
    private void ExportAll_Click(object sender, RoutedEventArgs e) => Export(true);
    private async void Export(bool all, bool filtered = false)
    {
        if (_exporting) return;
        try
        {
            if (filtered && !_historyRangeValid) return;
            _exporting = true; ExportButton.IsEnabled = false;
            _controller.Collect();
            var query = _historyQuery;
            var device = filtered ? query.Device : InputKind.All;
            var dates = filtered ? (await Task.Run(() => _controller.Store.GetSummaries(_lifetime.Token), _lifetime.Token)).Where(query.Includes).Select(d => d.Date).ToArray()
                : all ? _controller.Store.GetDates().ToArray() : new[] { _date };
            if (_closed) return;
            if (dates.Length == 0 || (!all && _controller.Store.GetSummary(_date).Total == 0)) { ShowInlineError("没有可导出的键鼠记录。使用键盘或鼠标后再试，或选择有记录的日期。"); return; }
            var dialog = new SaveFileDialog { Filter = "CSV 表格 (*.csv)|*.csv", FileName = filtered ? $"键盘统计器-{DeviceName(device)}-{dates.Min():yyyy-MM-dd}-{dates.Max():yyyy-MM-dd}.csv" : all ? "键盘统计器-全部记录.csv" : $"键盘统计器-{_date:yyyy-MM-dd}.csv", AddExtension = true };
            if (dialog.ShowDialog(this) != true) return;
            _exporting = true; ExportButton.IsEnabled = false; SaveLabel.Text = "正在导出…";
            await Task.Run(() => _controller.Store.ExportCsv(dialog.FileName, dates, device));
            if (!_closed) SaveLabel.Text = $"已导出 {Path.GetFileName(dialog.FileName)}";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_closed) ShowInlineError("导出未完成：" + ex.Message); }
        finally { _exporting = false; if (!_closed) ExportButton.IsEnabled = _page != 1 || _historyRangeValid; }
    }

    private void ShowInlineError(string message)
    { _uiNotice = message; _noticeUntil = DateTime.Now.AddSeconds(12); NoticeText.Text = message; NoticeBanner.Visibility = Visibility.Visible; }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (AllowClose) return;
        if (_controller.Settings.CloseToTray) { e.Cancel = true; Hide(); ((App)Application.Current).NotifyHidden(); }
        else { e.Cancel = true; ((App)Application.Current).ExitApplication(); }
    }
    internal WindowSession CaptureSession() => new(_date, _date == DateOnly.FromDateTime(DateTime.Now), _page, _selectedKey,
        AllKeysOverlay.Visibility == Visibility.Visible, KeySearch.Text, RestoreBounds.Width, RestoreBounds.Height, WindowState == WindowState.Maximized, MouseButtons.SelectedButton,
        _historyDevice, _activityRangeDays, _historyRange, RangeStartPicker.SelectedDate, RangeEndPicker.SelectedDate);
    internal void RestoreSession(WindowSession session)
    {
        _date = session.FollowToday ? DateOnly.FromDateTime(DateTime.Now) : session.Date;
        DayPicker.SelectedDate = _date.ToDateTime(TimeOnly.MinValue);
        _selectedKey = session.Key; Keyboard.SelectKey(session.Key);
        MouseButtons.SelectButton(session.MouseButton);
        KeySearch.Text = session.Search;
        RestoreHistoryFilters(session);
        RenderActivity();
        if (session.Page == 0 && session.AllKeys) ShowAllKeys();
        if (double.IsFinite(session.Width) && session.Width >= MinWidth) Width = Math.Min(session.Width, SystemParameters.WorkArea.Width);
        if (double.IsFinite(session.Height) && session.Height >= MinHeight) Height = Math.Min(session.Height, SystemParameters.WorkArea.Height);
        if (session.Maximized) WindowState = WindowState.Maximized;
        if (session.Page == 1) HistoryNav.IsChecked = true;
        else if (session.Page == 2) SettingsNav.IsChecked = true;
    }
    internal bool IsRefreshTimerRunning => _refresh.IsEnabled;
    private static Brush Brush(string color) => UiResources.Brush(color);
    private sealed record RankVisual(Border Container, TextBlock Name, TextBlock Count, TextBlock Share);
    private sealed class HistoryRow : INotifyPropertyChanged
    {
        private DaySummary _summary;
        public DaySummary Summary => _summary;
        private InputKind _device;
        public InputKind Device { get => _device; set { if (_device == value) return; _device = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedTotal))); } }
        public long SelectedTotal => new HistoryQuery(Device, null, Date).Count(_summary);
        public DateOnly Date => _summary.Date;
        public string DateLabel { get; }
        public long Total => _summary.Total;
        public long KeyboardTotal => _summary.KeyboardTotal;
        public long MouseTotal => _summary.MouseTotal;
        public int Unique => _summary.UsedKeys;
        public int Minutes => _summary.ActiveMinutes;
        public string Favorite => _summary.FavoriteKey is null ? "—" : KeyCatalog.Get(_summary.FavoriteKey).Name;
        public event PropertyChangedEventHandler? PropertyChanged;
        public HistoryRow(DaySummary summary) { _summary = summary; DateLabel = Date.ToString("yyyy/MM/dd  ddd", CultureInfo.GetCultureInfo("zh-CN")); }
        public void Update(DaySummary summary)
        { if (_summary == summary) return; _summary = summary; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null)); }
    }
}

internal sealed record WindowSession(DateOnly Date, bool FollowToday, int Page, string? Key, bool AllKeys, string Search, double Width, double Height, bool Maximized, string? MouseButton,
    InputKind HistoryDevice, int ActivityRangeDays, HistoryRange HistoryRange, DateTime? RangeStart, DateTime? RangeEnd);

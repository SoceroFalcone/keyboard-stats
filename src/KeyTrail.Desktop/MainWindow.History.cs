using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace KeyTrail.Desktop;

public partial class MainWindow
{
    private readonly ICollectionView _historyView;
    private InputKind _historyDevice;
    private HistoryRange _historyRange;
    private HistoryQuery _historyQuery = HistoryQuery.Create(HistoryRange.All, InputKind.All, DateOnly.FromDateTime(DateTime.Today));
    private bool _historyRangeValid = true;
    private bool _restoringFilters;
    private DateOnly _toolbarDate;
    private DateOnly _toolbarToday;
    private static readonly CultureInfo Chinese = CultureInfo.GetCultureInfo("zh-CN");
    private string DeviceLabel => DeviceName(_historyDevice);
    private static string DeviceName(InputKind kind) => kind switch { InputKind.Keyboard => "键盘", InputKind.Mouse => "鼠标", _ => "键鼠" };

    private void UpdateDateToolbar(DateOnly today)
    {
        if (_toolbarDate == _date && _toolbarToday == today) return;
        _toolbarDate = _date; _toolbarToday = today;
        NextDayButton.IsEnabled = _date < today;
        PreviousDayButton.IsEnabled = _date > new DateOnly(1970, 1, 1);
        TodayButton.IsChecked = _date == today;
        YesterdayButton.IsChecked = _date == today.AddDays(-1);
        DayContext.Text = _date.ToString("dddd", Chinese);
        RangeStartPicker.DisplayDateEnd = RangeEndPicker.DisplayDateEnd = today.ToDateTime(TimeOnly.MinValue);
    }

    private void DeviceFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded || _restoringFilters || sender is not RadioButton { Tag: string tag } || !Enum.TryParse<InputKind>(tag, out var kind)) return;
        _historyDevice = kind;
        foreach (var row in _historyRows) row.Device = kind;
        RenderActivity(); ApplyHistoryFilters();
    }

    private void ActivityRange_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded || _restoringFilters || sender is not RadioButton { Tag: string tag }
            || !int.TryParse(tag, NumberStyles.None, CultureInfo.InvariantCulture, out var days) || days == _activityRangeDays) return;
        _activityRangeDays = days;
        HideKeyHover();
        RenderActivity();
    }

    private void HistoryRange_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded || _restoringFilters || sender is not RadioButton { Tag: string tag } || !Enum.TryParse<HistoryRange>(tag, out var range)) return;
        _historyRange = range;
        CustomDateControls.Visibility = range == HistoryRange.Custom ? Visibility.Visible : Visibility.Collapsed;
        UpdateHistoryViewport();
        ApplyHistoryFilters();
    }

    private void CustomRange_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_loaded && !_restoringFilters && _historyRange == HistoryRange.Custom) ApplyHistoryFilters();
    }

    private void Date_ValidationError(object? sender, DatePickerDateValidationErrorEventArgs e)
    {
        e.ThrowException = false;
        ShowInlineError("日期未更改。请输入有效日期（如 2026/11/15），或点击日历选择，日期不能晚于今天。");
    }

    private void ApplyHistoryFilters()
    {
        if (_historyView is null) return;
        try
        {
            _historyQuery = HistoryQuery.Create(_historyRange, _historyDevice, DateOnly.FromDateTime(DateTime.Today),
                RangeStartPicker.SelectedDate is { } start ? DateOnly.FromDateTime(start) : null,
                RangeEndPicker.SelectedDate is { } end ? DateOnly.FromDateTime(end) : null);
            _historyRangeValid = true; RangeError.Visibility = Visibility.Collapsed;
        }
        catch (ArgumentException ex)
        {
            _historyRangeValid = false; RangeError.Text = ex.Message; RangeError.Visibility = Visibility.Visible;
        }
        _historyView.Refresh();
        HistoryTotalColumn.Header = _historyDevice == InputKind.All ? "键鼠合计" : $"{DeviceLabel}次数";
        HistoryKeyboardColumn.Visibility = HistoryMouseColumn.Visibility = _historyDevice == InputKind.All ? Visibility.Visible : Visibility.Collapsed;
        HistoryFavoriteColumn.Visibility = _historyDevice == InputKind.Mouse ? Visibility.Collapsed : Visibility.Visible;
        var count = 0; long total = 0;
        foreach (HistoryRow row in _historyView) { count++; total += row.SelectedTotal; }
        HistoryCount.Text = $"{count:N0} 天";
        var rangeText = _historyQuery.Start is { } first
            ? first == _historyQuery.End ? first.ToString("yyyy/MM/dd") : $"{first:yyyy/MM/dd} — {_historyQuery.End:yyyy/MM/dd}"
            : "全部已记录日期";
        HistoryRangeSummary.Text = _historyRangeValid ? $"{rangeText}，共 {total:N0} 次{DeviceLabel}操作" : "请先调整日期范围";
        HistoryRangeSummary.Visibility = _historyRangeValid ? Visibility.Visible : Visibility.Collapsed;
        HistoryEmpty.Text = _historyRangeValid ? $"这个范围内还没有{DeviceLabel}记录。可选择其他日期或统计对象。" : "开始日期应早于或等于结束日期。";
        HistoryEmpty.Visibility = count == 0 && _historyRangeValid ? Visibility.Visible : Visibility.Collapsed;
        HistoryTable.Visibility = HistoryActions.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (HistoryTable.SelectedItem is null && count > 0) HistoryTable.SelectedIndex = 0;
        OpenHistoryButton.IsEnabled = HistoryTable.SelectedItem is HistoryRow;
        if (_page == 1) ExportButton.IsEnabled = !_exporting && _historyRangeValid;
        UpdateHistoryViewport();
    }

    private void HistorySelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (OpenHistoryButton is not null) OpenHistoryButton.IsEnabled = HistoryTable.SelectedItem is HistoryRow;
    }

    private void UpdateHistoryViewport()
    {
        // Keep the record filters and column headers on-screen when the outer page
        // reaches its end; recycle only a viewport's worth of actual record rows.
        if (PageScroll.ActualHeight <= 0) return;
        var chrome = HistoryRecordsPanel.ActualHeight > 0 ? HistoryRecordsPanel.ActualHeight - HistoryTable.ActualHeight
            : CustomDateControls.Visibility == Visibility.Visible ? 270 : 220;
        var height = Math.Clamp(PageScroll.ActualHeight - chrome, 96, 430);
        if (Math.Abs(HistoryTable.MaxHeight - height) > .5) HistoryTable.MaxHeight = height;
    }

    private void RestoreHistoryFilters(WindowSession session)
    {
        _restoringFilters = true;
        try
        {
            _historyDevice = session.HistoryDevice; _activityRangeDays = session.ActivityRangeDays; _historyRange = session.HistoryRange;
            RangeStartPicker.SelectedDate = session.RangeStart; RangeEndPicker.SelectedDate = session.RangeEnd;
            (session.HistoryDevice switch { InputKind.Keyboard => KeyboardFilter, InputKind.Mouse => MouseFilter, _ => AllDevicesFilter }).IsChecked = true;
            (session.ActivityRangeDays switch { 7 => Activity7Days, 30 => Activity30Days, 180 => Activity180Days, _ => ActivityYear }).IsChecked = true;
            (session.HistoryRange switch
            {
                HistoryRange.Today => RangeToday, HistoryRange.Yesterday => RangeYesterday, HistoryRange.Last7Days => Range7Days,
                HistoryRange.Last30Days => Range30Days, HistoryRange.Custom => RangeCustom, _ => RangeAll
            }).IsChecked = true;
            CustomDateControls.Visibility = _historyRange == HistoryRange.Custom ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { _restoringFilters = false; }
        ApplyHistoryFilters();
        UpdateHistoryViewport();
    }
}

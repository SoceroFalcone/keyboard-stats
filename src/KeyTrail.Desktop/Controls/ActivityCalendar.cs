using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Media;

namespace KeyTrail.Desktop.Controls;

// Each week is a column in a single rendered visual.
public sealed class ActivityCalendar : FrameworkElement
{
    private const int DefaultDays = 365;
    private const double DayLabelsWidth = 26;
    private const double Right = 2;
    private const double Top = 4;
    private const double Gap = 4;
    private const double MaxCellSize = 25;
    private static readonly Typeface Face = new("Segoe UI, Microsoft YaHei UI");
    private static readonly Typeface CountFace = new(Face.FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Brush Ink = UiResources.Brush("#5A6072");
    private static readonly Brush DarkCellInk = UiResources.Brush("#0D1B2C");
    private static readonly Pen TodayBorder = CreatePen("#3B608F", 1);
    private static readonly Pen SelectedBorder = CreatePen("#B8694A", 2);
    private static readonly Pen FocusBorder = CreatePen("#1F3A5C", 1.5);
    private static readonly CultureInfo Chinese = CultureInfo.GetCultureInfo("zh-CN");
    internal static readonly Brush[] ActivityColors = [UiResources.Brush("#E8EDF4"), UiResources.Brush("#D6E9FC"),
        UiResources.Brush("#ACD4FA"), UiResources.Brush("#7AB5EF"), UiResources.Brush("#438DDA"),
        UiResources.Brush("#2461AB"), UiResources.Brush("#193E76")];

    private DateOnly _today;
    private DateOnly _start;
    private DateOnly _gridStart;
    private long[] _counts = [];
    private int[] _levels = [];
    private long _beforeStart;
    private DateOnly _focused;
    private DateOnly? _selected;
    private DateOnly? _hovered;
    private int _columns;
    private int _days = DefaultDays;
    private InputKind _kind;

    public event Action<DateOnly>? DateSelected;
    public event Action<string>? DetailChanged;
    internal event Action<DateOnly?, MouseEventArgs>? DateHovered;
    internal DateOnly FirstDate => _start;
    internal DateOnly LastDate => _today;
    internal DateOnly FocusedDate => _focused;

    public ActivityCalendar()
    {
        Focusable = true;
        Cursor = Cursors.Arrow;
        AutomationProperties.SetHelpText(this, "上下方向键按天移动，左右方向键按周移动，Home 和 End 到区间首日或今天，Enter 查看当天详情。");
        SetData([], DateOnly.FromDateTime(DateTime.Today));
    }

    public void SetData(IReadOnlyList<DaySummary> summaries, DateOnly today, InputKind kind = InputKind.All, int days = DefaultDays)
    {
        if (days is < 1 or > DefaultDays) throw new ArgumentOutOfRangeException(nameof(days));
        var start = DateOnly.FromDayNumber(Math.Max(0, today.DayNumber - days + 1));
        var counts = new long[today.DayNumber - start.DayNumber + 1];
        var query = new HistoryQuery(kind, start, today);
        long beforeStart = 0;
        foreach (var summary in summaries)
        {
            var index = summary.Date.DayNumber - start.DayNumber;
            if (index == -1) beforeStart = query.Count(summary);
            else if ((uint)index < (uint)counts.Length) counts[index] = query.Count(summary);
        }
        if (_start == start && _today == today && _days == days && _kind == kind && _beforeStart == beforeStart && _counts.AsSpan().SequenceEqual(counts)) return;

        var followToday = _counts.Length == 0 || (_focused == _today && _selected is null);
        var rangeChanged = _start != start || _today != today || _days != days;
        _kind = kind;
        _today = today;
        _start = start;
        _gridStart = start.AddDays(-MondayIndex(start));
        var columns = (today.DayNumber - _gridStart.DayNumber) / 7 + 1;
        if (columns != _columns || days != _days)
        {
            _columns = columns;
            _days = days;
            InvalidateMeasure();
        }
        _counts = counts;
        _beforeStart = beforeStart;
        _levels = GetHeatLevels(counts);
        if (followToday || !ContainsDate(_focused)) _focused = today;
        if (_selected is { } selected && !ContainsDate(selected)) _selected = null;
        if (rangeChanged)
        {
            _hovered = null;
            Cursor = Cursors.Arrow;
        }

        Announce(_focused);
        if (_hovered is { } day) DetailChanged?.Invoke(Describe(day));
        InvalidateVisual();
    }

    private static int[] GetHeatLevels(long[] counts)
    {
        var positive = counts.Where(c => c > 0).Distinct().Order().ToArray();
        var byCount = new Dictionary<long, int>();
        for (var rank = 0; rank < positive.Length; rank++)
        {
            var level = positive.Length == 1 ? 4 : 1 + (int)Math.Round(5.0 * rank / (positive.Length - 1), MidpointRounding.AwayFromZero);
            byCount.Add(positive[rank], level);
        }
        return counts.Select(c => byCount.GetValueOrDefault(c)).ToArray();
    }

    internal int GetHeatLevel(DateOnly date) => ContainsDate(date) ? _levels[date.DayNumber - _start.DayNumber] : 0;

    internal bool TryGetDay(DateOnly date, out long count, out long previousCount)
    {
        var index = date.DayNumber - _start.DayNumber;
        if ((uint)index >= (uint)_counts.Length)
        {
            count = 0;
            previousCount = 0;
            return false;
        }
        count = _counts[index];
        previousCount = index == 0 ? _beforeStart : _counts[index - 1];
        return true;
    }

    private bool ContainsDate(DateOnly date) => date >= _start && date <= _today;
    private static int MondayIndex(DateOnly date) => ((int)date.DayOfWeek + 6) % 7;
    private bool UseDayCards => _days <= 30;

    private ChartLayout LayoutForWidth(double width)
    {
        var available = Math.Max(0, width - DayLabelsWidth - Right);
        var step = available / _columns;
        if (UseDayCards) step = Math.Min(step, 160);
        else if (_days <= 180) step = Math.Min(step, 35);
        var cellWidth = Math.Clamp(step - Gap, 0, UseDayCards ? 154 : MaxCellSize);
        var cellHeight = UseDayCards ? 25 : cellWidth;
        var left = DayLabelsWidth + (available - step * _columns) / 2;
        return new ChartLayout(left, step, cellWidth, cellHeight);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 800;
        var layout = LayoutForWidth(width);
        return new Size(0, layout.CaptionTop + 19);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (ActualWidth < 100 || _counts.Length == 0) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

        var layout = LayoutForWidth(ActualWidth);
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        const string weekdays = "一二三四五六日";
        for (var row = 0; row < weekdays.Length; row++)
        {
            if (!UseDayCards && row % 2 != 0) continue;
            var text = FormatText(weekdays[row].ToString(), pixelsPerDip);
            dc.DrawText(text, new Point(layout.Left - DayLabelsWidth, Top + row * layout.RowStep + (layout.CellHeight - text.Height) / 2));
        }
        var radius = UseDayCards ? 5.0 : 3.0;
        for (var i = 0; i < _counts.Length; i++)
        {
            var date = _start.AddDays(i);
            var rect = Bounds(date, layout);
            dc.DrawRoundedRectangle(ActivityColors[_levels[i]], null, rect, radius, radius);
            if (UseDayCards) DrawDayCard(dc, rect, date, _counts[i], _levels[i], pixelsPerDip);
            if (_today == date && _selected != date)
            {
                var outline = rect;
                outline.Inflate(1, 1);
                dc.DrawRoundedRectangle(null, TodayBorder, outline, radius + .5, radius + .5);
            }
            if (_selected == date) dc.DrawRoundedRectangle(null, SelectedBorder, rect, radius, radius);
            if ((IsKeyboardFocused && _focused == date) || _hovered == date)
            {
                rect.Inflate(1.5, 1.5);
                dc.DrawRoundedRectangle(null, FocusBorder, rect, radius + 1, radius + 1);
            }
        }
        if (UseDayCards) DrawWeekCaptions(dc, layout, pixelsPerDip);
        else DrawMonths(dc, layout, pixelsPerDip);
    }

    private static void DrawDayCard(DrawingContext dc, Rect rect, DateOnly date, long count, int level, double pixelsPerDip)
    {
        var foreground = level >= 5 ? Brushes.White : DarkCellInk;
        var day = FormatText(date.ToString("M/d", Chinese), pixelsPerDip, foreground);
        const double padding = 8;
        var y = rect.Y + (rect.Height - day.Height) / 2;
        if (rect.Width < day.Width + 2 * padding) return;
        dc.DrawText(day, new Point(rect.X + padding, y));
        var space = rect.Width - day.Width - 3 * padding;
        if (space < 24) return;
        var total = new FormattedText($"{count:N0} 次", Chinese, FlowDirection.LeftToRight, CountFace, 12, foreground, pixelsPerDip)
        {
            MaxTextWidth = space,
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Right
        };
        dc.DrawText(total, new Point(rect.Right - padding - space, y));
    }

    private void DrawWeekCaptions(DrawingContext dc, ChartLayout layout, double pixelsPerDip)
    {
        for (var column = 0; column < _columns; column++)
        {
            var date = _gridStart.AddDays(column * 7);
            var label = FormatText($"{date:M/d} 周", pixelsPerDip);
            if (label.Width > layout.ColumnStep) continue;
            var x = layout.Left + column * layout.ColumnStep + (layout.ColumnStep - label.Width) / 2;
            dc.DrawText(label, new Point(x, layout.CaptionTop));
        }
    }

    private void DrawMonths(DrawingContext dc, ChartLayout layout, double pixelsPerDip)
    {
        var previousLabelRight = 0.0;
        var columnsLeft = layout.Left + layout.CellInset;
        var month = _start;
        while (true)
        {
            var column = (month.DayNumber - _gridStart.DayNumber) / 7;
            var label = FormatText($"{month.Month}月", pixelsPerDip);
            var x = Math.Min(columnsLeft + column * layout.ColumnStep, ActualWidth - label.Width - Right);
            var lastMonth = month.Year == _today.Year && month.Month == _today.Month;
            var nextMonth = lastMonth ? (DateOnly?)null : new DateOnly(month.Year, month.Month, 1).AddMonths(1);
            // Leave room for the first full month's label when the rolling window starts near a month boundary.
            var partialMonthOverlap = month.Day > 1 && nextMonth is { } next &&
                x + label.Width + 5 > columnsLeft + (next.DayNumber - _gridStart.DayNumber) / 7 * layout.ColumnStep;
            if (!partialMonthOverlap && x >= previousLabelRight + 5)
            {
                dc.DrawText(label, new Point(x, layout.CaptionTop));
                previousLabelRight = x + label.Width;
            }
            if (nextMonth is not { } nextLabel) break;
            month = nextLabel;
        }
    }

    internal Rect GetCellBounds(DateOnly date)
    {
        if (!ContainsDate(date) || ActualWidth < 100) return Rect.Empty;
        return Bounds(date, LayoutForWidth(ActualWidth));
    }

    private Rect Bounds(DateOnly date, ChartLayout layout)
    {
        var offset = date.DayNumber - _gridStart.DayNumber;
        return new Rect(layout.Left + offset / 7 * layout.ColumnStep + layout.CellInset, Top + offset % 7 * layout.RowStep,
            layout.CellWidth, layout.CellHeight);
    }

    internal DateOnly? DateAt(Point point)
    {
        if (ActualWidth < 100 || point.Y < Top) return null;
        var layout = LayoutForWidth(ActualWidth);
        if (point.X < layout.Left || point.X >= layout.Left + layout.ColumnStep * _columns) return null;
        var column = (int)((point.X - layout.Left) / layout.ColumnStep);
        var row = (int)((point.Y - Top) / layout.RowStep);
        if (column >= _columns || row >= 7) return null;
        var dayNumber = _gridStart.DayNumber + column * 7 + row;
        if (dayNumber < _start.DayNumber || dayNumber > _today.DayNumber) return null;
        var date = DateOnly.FromDayNumber(dayNumber);
        return Bounds(date, layout).Contains(point) ? date : null;
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        UpdateHover(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        UpdateHover(e);
    }

    private void UpdateHover(MouseEventArgs e)
    {
        var date = DateAt(e.GetPosition(this));
        if (date != _hovered)
        {
            _hovered = date;
            Cursor = date.HasValue ? Cursors.Hand : Cursors.Arrow;
            DetailChanged?.Invoke(Describe(date ?? _focused));
            InvalidateVisual();
        }
        DateHovered?.Invoke(date, e);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = null;
        Cursor = Cursors.Arrow;
        Announce(_focused);
        InvalidateVisual();
        DateHovered?.Invoke(null, e);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (DateAt(e.GetPosition(this)) is not { } date) return;
        SelectDate(date);
        Focus();
        OpenFocusedDate();
        e.Handled = true;
    }

    internal void SelectDate(DateOnly date)
    {
        if (!ContainsDate(date)) return;
        _focused = date;
        _selected = date;
        Announce(date);
        InvalidateVisual();
    }

    internal void OpenFocusedDate()
    {
        if (ContainsDate(_focused)) DateSelected?.Invoke(_focused);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Enter or Key.Space)
        {
            OpenFocusedDate();
            e.Handled = true;
            return;
        }
        var delta = e.Key switch { Key.Left => -7, Key.Right => 7, Key.Up => -1, Key.Down => 1, _ => 0 };
        if (e.Key == Key.Home) SelectDate(_start);
        else if (e.Key == Key.End) SelectDate(_today);
        else if (delta != 0) SelectDate(DateOnly.FromDayNumber(Math.Clamp(_focused.DayNumber + delta, _start.DayNumber, _today.DayNumber)));
        else return;
        e.Handled = true;
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        Announce(_focused);
        InvalidateVisual();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        InvalidateVisual();
    }

    private string Describe(DateOnly date)
    {
        TryGetDay(date, out var count, out _);
        var device = _kind switch { InputKind.Keyboard => "键盘", InputKind.Mouse => "鼠标", _ => "键鼠" };
        return $"{date.ToString("yyyy/MM/dd  ddd", Chinese)} · {count:N0} 次{device}操作";
    }

    private void Announce(DateOnly date)
    {
        var detail = Describe(date);
        AutomationProperties.SetName(this, "每日键鼠活动，" + detail);
        DetailChanged?.Invoke(detail);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new CalendarPeer(this);
    private sealed class CalendarPeer(ActivityCalendar owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(ActivityCalendar);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Custom;
    }

    private static FormattedText FormatText(string text, double pixelsPerDip, Brush? foreground = null) =>
        new(text, Chinese, FlowDirection.LeftToRight, Face, 12, foreground ?? Ink, pixelsPerDip);

    private readonly record struct ChartLayout(double Left, double ColumnStep, double CellWidth, double CellHeight)
    {
        internal double RowStep => CellHeight + Gap;
        internal double CellInset => (ColumnStep - CellWidth) / 2;
        internal double CaptionTop => Top + 7 * RowStep + 3;
    }

    private static Pen CreatePen(string color, double width)
    {
        var pen = new Pen(UiResources.Brush(color), width);
        pen.Freeze();
        return pen;
    }
}

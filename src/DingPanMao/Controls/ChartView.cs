using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DingPanMao.Models;

namespace DingPanMao.Controls;

public enum ChartMode
{
    /// <summary>折线，用于分时。</summary>
    Line,

    /// <summary>蜡烛图，用于日线。</summary>
    Candle,
}

/// <summary>带坐标轴、网格和刻度的图表，供详情窗口使用。</summary>
public sealed class ChartView : FrameworkElement
{
    private static readonly Brush AxisBrush = new SolidColorBrush(Color.FromRgb(0x7C, 0x87, 0x97));

    private static readonly Brush GridBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));

    private static readonly Brush BaselineBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xC5, 0x3D));

    private static readonly Typeface LabelTypeface = new("Consolas");

    private IReadOnlyList<ChartPoint> _points = [];

    private IReadOnlyList<DailyBar> _bars = [];

    private ChartMode _mode = ChartMode.Line;

    private double _baseline = double.NaN;

    private int _decimals = 2;

    private string _suffix = string.Empty;

    private Color _upColor = Color.FromRgb(0xFF, 0x4D, 0x4F);

    private Color _downColor = Color.FromRgb(0x21, 0xC5, 0x5D);

    private int _hoverIndex = -1;

    /// <summary>鼠标所在位置的数据描述，交给外部信息条显示，避免浮层遮住图形。</summary>
    public event Action<string>? HoverChanged;

    public ChartView()
    {
        // FrameworkElement 默认没有背景、不参与命中测试，重写 HitTestCore 后这里不用额外处理。
        IsHitTestVisible = true;
    }

    /// <summary>让图表本身接收鼠标事件（FrameworkElement 默认会穿透）。</summary>
    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters)
        => new PointHitTestResult(this, hitTestParameters.HitPoint);

    public void Configure(int decimals, string suffix, bool redUpGreenDown)
    {
        _decimals = decimals;
        _suffix = suffix;
        _upColor = redUpGreenDown ? Color.FromRgb(0xFF, 0x4D, 0x4F) : Color.FromRgb(0x21, 0xC5, 0x5D);
        _downColor = redUpGreenDown ? Color.FromRgb(0x21, 0xC5, 0x5D) : Color.FromRgb(0xFF, 0x4D, 0x4F);
    }

    /// <summary>设置折线数据，例如当日分时。</summary>
    public void SetLine(IReadOnlyList<ChartPoint> points, double baseline)
    {
        _points = points;
        _bars = [];
        _mode = ChartMode.Line;
        _baseline = baseline;
        _hoverIndex = -1;
        HoverChanged?.Invoke(string.Empty);
        InvalidateVisual();
    }

    /// <summary>设置蜡烛数据，例如日线。</summary>
    public void SetCandles(IReadOnlyList<DailyBar> bars, double baseline)
    {
        _bars = bars;
        _points = [];
        _mode = ChartMode.Candle;
        _baseline = baseline;
        _hoverIndex = -1;
        HoverChanged?.Invoke(string.Empty);
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var index = IndexAt(e.GetPosition(this));
        if (index != _hoverIndex)
        {
            _hoverIndex = index;
            HoverChanged?.Invoke(index < 0 ? string.Empty : Describe(index));
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);

        if (_hoverIndex >= 0)
        {
            _hoverIndex = -1;
            HoverChanged?.Invoke(string.Empty);
            InvalidateVisual();
        }
    }

    private string Describe(int index)
    {
        if (_mode == ChartMode.Candle)
        {
            if (index >= _bars.Count)
            {
                return string.Empty;
            }

            var bar = _bars[index];
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}   开 {1}   高 {2}   低 {3}   收 {4}",
                bar.Date.ToString("yyyy-MM-dd"),
                Format(bar.Open),
                Format(bar.High),
                Format(bar.Low),
                Format(bar.Close));
        }

        if (index >= _points.Count)
        {
            return string.Empty;
        }

        var point = _points[index];
        return $"{point.Time:HH:mm}   {Format(point.Value)}";
    }

    private int IndexAt(Point position)
    {
        var count = _mode == ChartMode.Candle ? _bars.Count : _points.Count;
        if (count < 2)
        {
            return -1;
        }

        const double left = 58;
        const double right = 10;
        var plotWidth = ActualWidth - left - right;
        if (plotWidth <= 10 || position.X < left || position.X > left + plotWidth)
        {
            return -1;
        }

        var ratio = (position.X - left) / plotWidth;
        return Math.Clamp((int)Math.Round(ratio * (count - 1)), 0, count - 1);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 60 || height < 40)
        {
            return;
        }

        // 左边留给价格刻度，下边留给时间刻度
        const double left = 58;
        const double right = 10;
        const double top = 10;
        const double bottom = 22;
        var plotWidth = width - left - right;
        var plotHeight = height - top - bottom;
        if (plotWidth <= 10 || plotHeight <= 10)
        {
            return;
        }

        if (!TryGetRange(out var min, out var max, out var first, out var last))
        {
            DrawEmpty(dc, left, top, plotWidth, plotHeight);
            return;
        }

        var span = max - min;
        if (span <= 0)
        {
            span = Math.Max(Math.Abs(max) * 0.001, 0.5);
            min -= span / 2;
            max += span / 2;
            span = max - min;
        }
        else
        {
            var pad = span * 0.08;
            min -= pad;
            max += pad;
            span = max - min;
        }

        var origin = new Point(left, top);
        var plot = new Rect(left, top, plotWidth, plotHeight);

        double Y(double value) => top + ((max - value) / span * plotHeight);
        double X(int index, int count) => left + (count <= 1 ? plotWidth / 2 : (double)index / (count - 1) * plotWidth);

        // 网格与价格刻度
        const int rows = 4;
        for (var i = 0; i <= rows; i++)
        {
            var value = max - (span * i / rows);
            var y = Y(value);
            dc.DrawLine(new Pen(GridBrush, 1), new Point(left, y), new Point(left + plotWidth, y));
            DrawLabel(dc, value.ToString($"F{_decimals}") + _suffix, new Point(4, y - 7), AxisBrush, TextAlignment.Left);
        }

        // 时间刻度
        if (_mode == ChartMode.Candle && _bars.Count > 1)
        {
            DrawTimeAxis(dc, _bars.Count, X, top + plotHeight, (i) => _bars[i].Date.ToString("MM-dd"));
        }
        else if (_points.Count > 1)
        {
            DrawTimeAxis(dc, _points.Count, X, top + plotHeight, (i) => _points[i].Time.ToString("HH:mm"));
        }

        // 昨收基准线
        if (!double.IsNaN(_baseline) && _baseline >= min && _baseline <= max)
        {
            var y = Y(_baseline);
            var pen = new Pen(BaselineBrush, 1) { DashStyle = DashStyles.Dash };
            dc.DrawLine(pen, new Point(left, y), new Point(left + plotWidth, y));
        }

        if (_mode == ChartMode.Candle)
        {
            DrawCandles(dc, plot, X, Y, min, max);
        }
        else
        {
            DrawLine(dc, plotWidth, X, Y, first, last);
        }

        DrawHover(dc, plot, X, Y);

        // 绘图区边框
        dc.DrawRectangle(null, new Pen(GridBrush, 1), plot);
        _ = origin;
    }

    /// <summary>十字光标与数据浮层。</summary>
    private void DrawHover(
        DrawingContext dc,
        Rect plot,
        Func<int, int, double> x,
        Func<double, double> y)
    {
        if (_hoverIndex < 0)
        {
            return;
        }

        var count = _mode == ChartMode.Candle ? _bars.Count : _points.Count;
        if (_hoverIndex >= count)
        {
            return;
        }

        var cursor = new Pen(new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xC5, 0x3D)), 1)
        {
            DashStyle = DashStyles.Dot,
        };
        cursor.Freeze();

        var px = x(_hoverIndex, count);
        var py = _mode == ChartMode.Candle ? y(_bars[_hoverIndex].Close) : y(_points[_hoverIndex].Value);

        dc.DrawLine(cursor, new Point(px, plot.Top), new Point(px, plot.Bottom));
        dc.DrawLine(cursor, new Point(plot.Left, py), new Point(plot.Right, py));

        var marker = new SolidColorBrush(Color.FromRgb(0xFF, 0xC5, 0x3D));
        marker.Freeze();
        dc.DrawEllipse(marker, null, new Point(px, py), 3, 3);
    }

    private string Format(double value) => value.ToString($"F{_decimals}") + _suffix;

    private static void DrawEmpty(DrawingContext dc, double left, double top, double width, double height)
    {
        dc.DrawRectangle(null, new Pen(GridBrush, 1), new Rect(left, top, width, height));
        DrawLabel(dc, "no data", new Point(left + 8, top + 8), AxisBrush, TextAlignment.Left);
    }

    private bool TryGetRange(out double min, out double max, out double first, out double last)
    {
        min = double.MaxValue;
        max = double.MinValue;
        first = 0;
        last = 0;

        if (_mode == ChartMode.Candle)
        {
            if (_bars.Count < 2)
            {
                return false;
            }

            foreach (var bar in _bars)
            {
                min = Math.Min(min, bar.Low);
                max = Math.Max(max, bar.High);
            }

            first = _bars[0].Close;
            last = _bars[^1].Close;
            return min < double.MaxValue;
        }

        if (_points.Count < 2)
        {
            return false;
        }

        foreach (var point in _points)
        {
            min = Math.Min(min, point.Value);
            max = Math.Max(max, point.Value);
        }

        first = _points[0].Value;
        last = _points[^1].Value;
        return min < double.MaxValue;
    }

    private void DrawLine(
        DrawingContext dc,
        double plotWidth,
        Func<int, int, double> x,
        Func<double, double> y,
        double first,
        double last)
    {
        var rising = last >= first;
        var color = rising ? _upColor : _downColor;
        var pen = new Pen(new SolidColorBrush(color), 1.4) { LineJoin = PenLineJoin.Round };
        pen.Freeze();

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            for (var i = 0; i < _points.Count; i++)
            {
                var point = new Point(x(i, _points.Count), y(_points[i].Value));
                if (i == 0)
                {
                    ctx.BeginFigure(point, false, false);
                }
                else
                {
                    ctx.LineTo(point, true, false);
                }
            }
        }

        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
        _ = plotWidth;
    }

    private void DrawCandles(
        DrawingContext dc,
        Rect plot,
        Func<int, int, double> x,
        Func<double, double> y,
        double min,
        double max)
    {
        var count = _bars.Count;
        var slot = plot.Width / count;
        var bodyWidth = Math.Max(Math.Min(slot * 0.62, 14), 1.5);

        for (var i = 0; i < count; i++)
        {
            var bar = _bars[i];
            var center = x(i, count) + (count <= 1 ? 0 : 0);
            var rising = bar.Close >= bar.Open;
            var brush = new SolidColorBrush(rising ? _upColor : _downColor);
            brush.Freeze();
            var pen = new Pen(brush, Math.Max(bodyWidth * 0.16, 1));
            pen.Freeze();

            // 影线
            dc.DrawLine(pen, new Point(center, y(bar.High)), new Point(center, y(bar.Low)));

            // 实体
            var openY = y(bar.Open);
            var closeY = y(bar.Close);
            var top = Math.Min(openY, closeY);
            var height = Math.Max(Math.Abs(closeY - openY), 1);
            dc.DrawRectangle(brush, null, new Rect(center - (bodyWidth / 2), top, bodyWidth, height));
        }

        _ = min;
        _ = max;
    }

    private static void DrawTimeAxis(
        DrawingContext dc,
        int count,
        Func<int, int, double> x,
        double y,
        Func<int, string> label)
    {
        const int ticks = 4;
        for (var i = 0; i <= ticks; i++)
        {
            var index = (int)Math.Round((count - 1) * i / (double)ticks);
            index = Math.Clamp(index, 0, count - 1);
            var px = x(index, count);
            var text = label(index);
            DrawLabel(dc, text, new Point(px - 22, y + 4), AxisBrush, TextAlignment.Left, 44);
        }
    }

    private static void DrawLabel(
        DrawingContext dc,
        string text,
        Point origin,
        Brush brush,
        TextAlignment alignment,
        double maxWidth = 52)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            10,
            brush,
            VisualTreeHelper.GetDpi(new DrawingVisual()).PixelsPerDip)
        {
            MaxTextWidth = maxWidth,
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = alignment,
        };

        dc.DrawText(formatted, origin);
    }
}

/// <summary>折线图上的一个点。</summary>
public readonly record struct ChartPoint(DateTime Time, double Value);

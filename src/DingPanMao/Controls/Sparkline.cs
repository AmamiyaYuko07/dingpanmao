using System.Windows;
using System.Windows.Media;

namespace DingPanMao.Controls;

/// <summary>极简的实时走势图，按给定序列自动缩放。</summary>
public sealed class Sparkline : FrameworkElement
{
    private IReadOnlyList<double> _values = [];

    public Brush LineBrush { get; set; } = new SolidColorBrush(Color.FromRgb(0x8A, 0x94, 0xA6));

    public void SetValues(IReadOnlyList<double> values, Brush brush)
    {
        _values = values;
        LineBrush = brush;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 2 || height <= 2 || _values.Count < 2)
        {
            return;
        }

        var min = double.MaxValue;
        var max = double.MinValue;
        foreach (var value in _values)
        {
            if (value < min)
            {
                min = value;
            }

            if (value > max)
            {
                max = value;
            }
        }

        var span = max - min;
        if (span <= 0)
        {
            span = Math.Max(max * 0.001, 0.5);
            min -= span / 2;
            max += span / 2;
            span = max - min;
        }

        const double pad = 1.5;
        var usable = height - (pad * 2);
        if (usable <= 0)
        {
            return;
        }

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            for (var i = 0; i < _values.Count; i++)
            {
                var x = i / (double)(_values.Count - 1) * width;
                var y = pad + ((1 - ((_values[i] - min) / span)) * usable);
                var point = new Point(x, y);
                if (i == 0)
                {
                    context.BeginFigure(point, false, false);
                }
                else
                {
                    context.LineTo(point, true, false);
                }
            }
        }

        geometry.Freeze();
        var pen = new Pen(LineBrush, 1.1) { LineJoin = PenLineJoin.Round };
        pen.Freeze();
        drawingContext.DrawGeometry(null, pen, geometry);
    }
}

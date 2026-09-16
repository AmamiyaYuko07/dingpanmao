using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DingPanMao.Config;
using DingPanMao.Models;

namespace DingPanMao.Controls;

/// <summary>一个品种格子。支持小/中/大三种尺寸，每种显示的内容不同。</summary>
public partial class SymbolTile : UserControl
{
    private static readonly TimeSpan MessageHold = TimeSpan.FromSeconds(4);

    private static readonly Color AlertColor = Color.FromRgb(0xFF, 0xC5, 0x3D);

    private static readonly Color StaleColor = Color.FromRgb(0x7A, 0x82, 0x90);

    private readonly DispatcherTimer _messageTimer = new() { Interval = MessageHold };

    private Quote? _lastQuote;

    private IReadOnlyList<double> _lastSeries = [];

    private TileSize _size = TileSize.Large;

    private double _fontScale = 1.0;

    private bool _redUpGreenDown = true;

    public SymbolTile()
    {
        InitializeComponent();
        _messageTimer.Tick += (_, _) => RestoreQuote();
    }

    public string SymbolId { get; set; } = string.Empty;

    public TileSize Size => _size;

    /// <summary>应用尺寸、字号和配色方案。</summary>
    public void ApplyStyle(TileSize size, double fontScale, bool redUpGreenDown)
    {
        _size = size;
        _fontScale = fontScale;
        _redUpGreenDown = redUpGreenDown;

        var scale = fontScale;
        switch (size)
        {
            case TileSize.Small:
                // 小尺寸也带走势图，只是格子窄、图自然就小。
                Spark.Visibility = Visibility.Visible;
                PriceText.Visibility = Visibility.Collapsed;
                ChartRow.Height = new GridLength(1, GridUnitType.Star);
                NameText.FontSize = 10.5 * scale;
                ChangeText.FontSize = 10.5 * scale;
                ChangeText.Margin = new Thickness(0, 0, 0, 1);
                break;

            case TileSize.Medium:
                Spark.Visibility = Visibility.Visible;
                PriceText.Visibility = Visibility.Collapsed;
                ChartRow.Height = new GridLength(1, GridUnitType.Star);
                NameText.FontSize = 10 * scale;
                ChangeText.FontSize = 10 * scale;
                ChangeText.Margin = new Thickness(0, 0, 0, 1);
                break;

            default:
                Spark.Visibility = Visibility.Visible;
                PriceText.Visibility = Visibility.Visible;
                ChartRow.Height = new GridLength(1, GridUnitType.Star);
                NameText.FontSize = 10 * scale;
                PriceText.FontSize = 12.5 * scale;
                ChangeText.FontSize = 9.5 * scale;
                ChangeText.Margin = new Thickness(5, 0, 0, 1);
                break;
        }

        RestoreQuote();
    }

    public void Render(Quote quote, IReadOnlyList<double> series)
    {
        _lastQuote = quote;
        _lastSeries = series;
        RestoreQuote();
    }

    /// <summary>闪两下，并把格子里的文字换成提醒内容。</summary>
    public void Flash(bool bullish, string message)
    {
        var color = bullish
            ? ParseColor(_redUpGreenDown ? "#FF4D4F" : "#21C55D")
            : ParseColor(_redUpGreenDown ? "#21C55D" : "#FF4D4F");

        var brush = new SolidColorBrush(color) { Opacity = 0 };
        FlashLayer.Background = brush;

        var animation = new DoubleAnimationUsingKeyFrames();
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0.40, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(140))));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(330))));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0.40, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(470))));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(660))));
        brush.BeginAnimation(SolidColorBrush.OpacityProperty, animation);

        NameText.Visibility = Visibility.Collapsed;
        QuotePanel.Visibility = Visibility.Collapsed;
        AlertText.FontSize = (_size == TileSize.Small ? 10.5 : 11) * _fontScale;
        AlertText.Text = message;
        AlertText.Visibility = Visibility.Visible;

        _messageTimer.Stop();
        _messageTimer.Start();
    }

    /// <summary>行情还没拉起来时的占位状态。</summary>
    public void ShowPlaceholder(string name)
    {
        NameText.Text = name;
        PriceText.Text = "--";
        ChangeText.Text = string.Empty;
    }

    private void RestoreQuote()
    {
        _messageTimer.Stop();

        AlertText.Visibility = Visibility.Collapsed;
        NameText.Visibility = Visibility.Visible;
        QuotePanel.Visibility = Visibility.Visible;

        if (_lastQuote is null)
        {
            return;
        }

        var quote = _lastQuote;

        if (quote.IsStale)
        {
            var stale = new SolidColorBrush(StaleColor);
            stale.Freeze();
            NameText.Foreground = stale;
            PriceText.Foreground = stale;
            ChangeText.Foreground = stale;
            PriceText.Text = "--";
            ChangeText.Text = string.Empty;
            return;
        }

        NameText.Text = quote.Name;
        PriceText.Text = quote.FormatPrice();
        ChangeText.Text = quote.FormatChangePercent();

        var brush = new SolidColorBrush(
            quote.Change >= 0
                ? ParseColor(_redUpGreenDown ? "#FF4D4F" : "#21C55D")
                : ParseColor(_redUpGreenDown ? "#21C55D" : "#FF4D4F"));
        brush.Freeze();

        NameText.Foreground = new SolidColorBrush(Color.FromRgb(0xC2, 0xCC, 0xDA));
        PriceText.Foreground = brush;
        ChangeText.Foreground = brush;
        Spark.SetValues(_lastSeries, brush);
    }

    private static Color ParseColor(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}

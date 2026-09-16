using System.Windows;
using System.Windows.Media;
using DingPanMao.Config;
using DingPanMao.Models;
using DingPanMao.Services;

namespace DingPanMao.Views;

/// <summary>点击格子后弹出的详情窗口：当日分时 + 近 30 日走势。</summary>
public partial class DetailWindow : Window
{
    private readonly MarketDataService _market;

    private readonly SymbolDefinition _symbol;

    private readonly AppSettings _settings;

    public DetailWindow(MarketDataService market, SymbolDefinition symbol, AppSettings settings)
    {
        _market = market;
        _symbol = symbol;
        _settings = settings;

        InitializeComponent();
        Title = symbol.NameFor(Lang.CurrentLanguage);
        NameText.Text = symbol.NameFor(Lang.CurrentLanguage);
        IntradayLabel.Text = Lang.T("detail.intraday");
        DailyLabel.Text = Lang.T("detail.daily");

        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var upBrush = BrushFor(up: true);
        var downBrush = BrushFor(up: false);

        try
        {
            var ticks = await _market.GetIntradayAsync(_symbol);
            if (ticks.Count > 1)
            {
                var prices = ticks.Select(t => t.Price).ToList();
                var rising = prices[^1] >= prices[0];
                IntradayChart.SetValues(prices, rising ? upBrush : downBrush);
                ApplyHeader(prices[^1], prices[0]);
            }

            var bars = await _market.GetDailyAsync(_symbol, 30);
            if (bars.Count > 1)
            {
                var closes = bars.Select(b => b.Close).ToList();
                var rising = closes[^1] >= closes[0];
                DailyChart.SetValues(closes, rising ? upBrush : downBrush);
            }
        }
        catch
        {
            // 打不开图表不影响其它功能。
        }
    }

    private void ApplyHeader(double price, double firstPrice)
    {
        var rising = price >= firstPrice;
        var brush = rising ? BrushFor(up: true) : BrushFor(up: false);
        PriceText.Foreground = brush;
        ChangeText.Foreground = brush;

        PriceText.Text = price.ToString($"F{_symbol.Decimals}") + _symbol.Suffix;
        var percent = firstPrice <= 0 ? 0 : (price - firstPrice) / firstPrice * 100.0;
        ChangeText.Text = $"{(percent >= 0 ? "+" : string.Empty)}{percent:F2}%";
    }

    private Brush BrushFor(bool up)
    {
        var hex = _settings.RedUpGreenDown
            ? (up ? "#FF4D4F" : "#21C55D")
            : (up ? "#21C55D" : "#FF4D4F");
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}

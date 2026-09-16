using System.Windows;
using System.Windows.Media;
using DingPanMao.Config;
using DingPanMao.Controls;
using DingPanMao.Models;
using DingPanMao.Services;

namespace DingPanMao.Views;

/// <summary>点击格子后弹出的详情窗口：当日分时 + 近 60 根日 K。</summary>
public partial class DetailWindow : Window
{
    private const int DailyCount = 60;

    private readonly MarketDataService _market;

    private readonly SymbolDefinition _symbol;

    private readonly AppSettings _settings;

    private readonly Quote? _quote;

    public DetailWindow(
        MarketDataService market,
        SymbolDefinition symbol,
        AppSettings settings,
        Quote? quote)
    {
        _market = market;
        _symbol = symbol;
        _settings = settings;
        _quote = quote;

        InitializeComponent();

        var name = symbol.NameFor(Lang.CurrentLanguage);
        Title = name;
        NameText.Text = name;
        IntradayLabel.Text = Lang.T("detail.intraday");
        DailyLabel.Text = Lang.T("detail.daily");

        IntradayChart.Configure(symbol.Decimals, symbol.Suffix, settings.RedUpGreenDown);
        DailyChart.Configure(symbol.Decimals, symbol.Suffix, settings.RedUpGreenDown);

        ApplyHeader();
        Loaded += async (_, _) => await LoadAsync();

        // 固定在主屏居中偏上，避免跑到副屏。
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = Math.Max(0, (SystemParameters.PrimaryScreenWidth - Width) / 2);
        Top = Math.Max(40, (SystemParameters.PrimaryScreenHeight - Height) / 2 - 60);
    }

    private void ApplyHeader()
    {
        if (_quote is null)
        {
            PriceText.Text = "--";
            return;
        }

        var brush = new SolidColorBrush(_quote.Change >= 0 ? UpColor : DownColor);
        brush.Freeze();
        PriceText.Foreground = brush;
        ChangeText.Foreground = brush;

        PriceText.Text = _quote.FormatPrice();
        ChangeText.Text = _quote.FormatChangePercent();

        var d = _symbol.Decimals;
        var suffix = _symbol.Suffix;
        StatsText.Text =
            $"昨收 {_quote.PrevClose.ToString($"F{d}")}{suffix}   "
            + $"开 {_quote.Open.ToString($"F{d}")}{suffix}   "
            + $"高 {_quote.DayHigh.ToString($"F{d}")}{suffix}   "
            + $"低 {_quote.DayLow.ToString($"F{d}")}{suffix}";
    }

    private Color UpColor => _settings.RedUpGreenDown
        ? Color.FromRgb(0xFF, 0x4D, 0x4F)
        : Color.FromRgb(0x21, 0xC5, 0x5D);

    private Color DownColor => _settings.RedUpGreenDown
        ? Color.FromRgb(0x21, 0xC5, 0x5D)
        : Color.FromRgb(0xFF, 0x4D, 0x4F);

    private async Task LoadAsync()
    {
        try
        {
            var ticks = await _market.GetIntradayAsync(_symbol);
            if (ticks.Count > 1)
            {
                var points = ticks
                    .Select(t => new ChartPoint(t.Time, t.Price))
                    .ToList();
                var baseline = _quote?.PrevClose ?? 0;
                IntradayChart.SetLine(points, baseline);
            }

            var bars = await _market.GetDailyAsync(_symbol, DailyCount);
            if (bars.Count > 1)
            {
                DailyChart.SetCandles(bars, double.NaN);
            }
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }
}

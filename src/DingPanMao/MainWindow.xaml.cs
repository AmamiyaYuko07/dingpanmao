using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using DingPanMao.Config;
using DingPanMao.Controls;
using DingPanMao.Interop;
using DingPanMao.Models;
using DingPanMao.Services;
using DingPanMao.Views;

namespace DingPanMao;

public partial class MainWindow : Window
{
    private const double EdgeGap = 8;

    private const double DividerWidth = 1;

    private readonly AppSettings _settings;

    private readonly MarketDataService _market = new();

    private readonly AlertEngine _alerts;

    private readonly DispatcherTimer _refreshTimer = new();

    private readonly DispatcherTimer _topmostTimer = new();

    private readonly List<SymbolTile> _tiles = [];

    private readonly List<SymbolDefinition> _symbols = [];

    private readonly Dictionary<string, List<Tick>> _series = [];

    private readonly Dictionary<string, IReadOnlyList<Level>> _levels = [];

    private readonly Dictionary<string, Quote> _quotes = [];

    private readonly Dictionary<string, double> _atr = [];

    private SettingsWindow? _settingsWindow;

    private DetailWindow? _detailWindow;

    private IntPtr _handle;

    private IntPtr _foregroundHook;

    private NativeMethods.WinEventProc? _foregroundCallback;

    private bool _busy;

    private bool _dragging;

    private bool _dragged;

    private double _dragOriginScreenX;

    private double _dragOriginLeft;

    private double _barTop;

    /// <summary>按下时落在哪个格子上，用来区分「点击」和「拖动」。</summary>
    private SymbolTile? _pressedTile;

    public MainWindow()
    {
        _settings = SettingsStore.Load();
        Lang.SetLanguage(_settings.Language);
        _alerts = new AlertEngine(BuildAlertOptions());

        InitializeComponent();
        BuildMenu();
        BuildTiles();

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closed += OnClosed;
        Lang.LanguageChanged += OnLanguageChanged;
    }

    private AlertOptions BuildAlertOptions()
    {
        var factor = Math.Clamp(_settings.Sensitivity, 0.5, 2.0);
        return new AlertOptions
        {
            ReversalAtrFactor = 0.06 * factor,
            BreakAtrFactor = 0.10 * factor,
            ReversalMinPercent = 0.0008 * factor,
            EnableReversalAlerts = _settings.ReversalAlerts,
            EnableBreakAlerts = _settings.BreakoutAlerts,
            EnableLevelAlerts = _settings.LevelAlerts,
        };
    }

    /// <summary>按当前配置重建所有格子。</summary>
    private void BuildTiles()
    {
        TileHost.Children.Clear();
        TileHost.ColumnDefinitions.Clear();
        _tiles.Clear();
        _symbols.Clear();

        var slots = _settings.Slots.Take(AppConfig.MaxSlots).ToList();
        for (var i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];

            if (i > 0)
            {
                TileHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(DividerWidth) });
                var divider = new Rectangle
                {
                    Fill = new SolidColorBrush(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF)),
                    Margin = new Thickness(0, 10, 0, 10),
                };
                Grid.SetColumn(divider, TileHost.ColumnDefinitions.Count - 1);
                TileHost.Children.Add(divider);
            }

            TileHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(AppConfig.WidthFor(slot.Size)) });

            var tile = new SymbolTile { SymbolId = slot.Symbol.Id };
            tile.ApplyStyle(slot.Size, _settings.FontScale, _settings.RedUpGreenDown);
            tile.ShowPlaceholder(slot.Symbol.NameFor(Lang.CurrentLanguage));
            tile.MouseWheel += OnTileWheel;
            Grid.SetColumn(tile, TileHost.ColumnDefinitions.Count - 1);
            TileHost.Children.Add(tile);

            _tiles.Add(tile);
            _symbols.Add(slot.Symbol);
            _series.TryAdd(slot.Symbol.Id, []);

            if (_quotes.TryGetValue(slot.Symbol.Id, out var cached))
            {
                tile.Render(cached, ChartSeries(slot.Symbol.Id));
            }
        }

        Width = slots.Sum(s => AppConfig.WidthFor(s.Size))
            + (DividerWidth * Math.Max(slots.Count - 1, 0))
            + 6;
    }

    private void BuildMenu()
    {
        var menu = new ContextMenu
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x20, 0x27)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xE9, 0xEE, 0xF5)),
        };

        if (_alerts.History.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = Lang.T("alert.none"), IsEnabled = false });
        }
        else
        {
            foreach (var alert in _alerts.History)
            {
                menu.Items.Add(new MenuItem { Header = FormatAlert(alert), IsEnabled = false });
            }
        }

        menu.Items.Add(new Separator { Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)) });
        menu.Items.Add(CreateMenuItem(Lang.T("menu.settings"), OnSettingsClick));
        menu.Items.Add(CreateMenuItem(Lang.T("menu.relocate"), OnRelocateClick));
        menu.Items.Add(new Separator { Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)) });
        menu.Items.Add(CreateMenuItem(Lang.T("menu.exit"), OnExitClick));

        ContextMenu = menu;
    }

    private static MenuItem CreateMenuItem(string header, RoutedEventHandler handler)
    {
        var item = new MenuItem { Header = header };
        item.Click += handler;
        return item;
    }

    private string FormatAlert(AlertEvent alert)
    {
        var symbol = _symbols.FirstOrDefault(s => s.Id == alert.SymbolId);
        var name = symbol is null ? string.Empty : symbol.NameFor(Lang.CurrentLanguage);
        var value = FormatValue(alert, symbol);
        return $"{alert.Time:HH:mm}  {name} {Lang.T(AlertKey(alert.Kind))} {value}";
    }

    private string AlertShortText(AlertEvent alert, bool withPrefix)
    {
        var symbol = _symbols.FirstOrDefault(s => s.Id == alert.SymbolId);
        var prefix = withPrefix ? Lang.T("alert.lastPrefix") + " " : string.Empty;
        return $"{prefix}{Lang.T(AlertKey(alert.Kind))} {FormatValue(alert, symbol)}";
    }

    private static string FormatValue(AlertEvent alert, SymbolDefinition? symbol)
        => alert.Reference.ToString($"F{symbol?.Decimals ?? 2}") + (symbol?.Suffix ?? string.Empty);

    private static string AlertKey(AlertKind kind) => kind switch
    {
        AlertKind.BottomRebound => "alert.bottomRebound",
        AlertKind.TopReversal => "alert.topReversal",
        AlertKind.BreakHigh => "alert.breakHigh",
        AlertKind.BreakLow => "alert.breakLow",
        AlertKind.LevelUp => "alert.levelUp",
        _ => "alert.levelDown",
    };

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _handle = new WindowInteropHelper(this).Handle;

        var style = NativeMethods.GetWindowLongPtr(_handle, NativeMethods.GwlExStyle).ToInt64();
        style |= NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate;
        NativeMethods.SetWindowLongPtr(_handle, NativeMethods.GwlExStyle, new IntPtr(style));

        // 任何应用被切到前台时立刻重新置顶，保证长条始终压在任务栏之上。
        _foregroundCallback = OnForegroundChanged;
        _foregroundHook = NativeMethods.SetWinEventHook(
            NativeMethods.EventSystemForeground,
            NativeMethods.EventSystemForeground,
            IntPtr.Zero,
            _foregroundCallback,
            0,
            0,
            NativeMethods.WineventOutOfContext);

        ApplyAppearance();
        ApplyLayout();
        NudgeTopmost();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _refreshTimer.Interval = TimeSpan.FromSeconds(_settings.RefreshSeconds);
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        _refreshTimer.Start();

        _topmostTimer.Interval = TimeSpan.FromSeconds(1.5);
        _topmostTimer.Tick += (_, _) => NudgeTopmost();
        _topmostTimer.Start();

        // 便于调试：带 --detail 参数启动时立刻打开第一个格子的大图（不等待网络预热）。
        if (Environment.GetCommandLineArgs().Contains("--detail") && _tiles.Count > 0)
        {
            OpenDetailFor(_tiles[0]);
        }

        await BackfillAsync(_symbols);
        await RefreshAsync();

        var latest = _alerts.History.FirstOrDefault();
        if (latest is not null
            && DateTime.Now - latest.Time <= TimeSpan.FromHours(12)
            && TryFindTile(latest.SymbolId, out var tile))
        {
            tile.Flash(latest.IsBullish, AlertShortText(latest, withPrefix: true));
        }

        // 便于调试：带 --settings 参数启动时会直接打开设置窗口。
        if (Environment.GetCommandLineArgs().Contains("--settings"))
        {
            OnSettingsClick(this, new RoutedEventArgs());
        }

    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();
        _topmostTimer.Stop();
        Lang.LanguageChanged -= OnLanguageChanged;
        _market.Dispose();

        if (_foregroundHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_foregroundHook);
            _foregroundHook = IntPtr.Zero;
        }
    }

    private void OnLanguageChanged()
    {
        BuildMenu();
        BuildTiles();
        UpdateRecentAlertsMenu();
    }

    private void UpdateRecentAlertsMenu() => BuildMenu();

    private bool TryFindTile(string symbolId, out SymbolTile tile)
    {
        var index = _symbols.FindIndex(s => s.Id == symbolId);
        if (index >= 0 && index < _tiles.Count)
        {
            tile = _tiles[index];
            return true;
        }

        tile = null!;
        return false;
    }

    private void ApplyAppearance()
    {
        var alpha = (byte)Math.Round(Math.Clamp(_settings.Opacity, 0.2, 1.0) * 255);
        Root.Background = new SolidColorBrush(Color.FromArgb(alpha, 0x12, 0x14, 0x1A));
    }

    /// <summary>把长条贴到任务栏上，横向位置优先使用上次拖动后记住的位置。</summary>
    private void ApplyLayout()
    {
        var taskbar = TaskbarLocator.GetTaskbarRect();
        if (taskbar is null)
        {
            return;
        }

        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (scale <= 0)
        {
            scale = 1;
        }

        var screenHeight = SystemParameters.PrimaryScreenHeight;
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var barHeight = taskbar.Value.Height / scale;
        var taskbarTop = taskbar.Value.Top / scale;

        if (taskbarTop >= screenHeight - 4)
        {
            taskbarTop = screenHeight - barHeight;
        }

        var tray = TaskbarLocator.GetTrayNotifyRect();
        var rightEdge = (tray?.Left ?? taskbar.Value.Right) / scale;

        Height = barHeight;
        Top = taskbarTop;
        _barTop = taskbarTop;

        var defaultLeft = rightEdge - Width - EdgeGap;
        var left = _settings.BarLeft ?? defaultLeft;
        if (double.IsNaN(left) || left < 0 || left + Width > screenWidth)
        {
            left = defaultLeft;
        }

        Left = Math.Round(left);
    }

    private void NudgeTopmost()
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.SetWindowPos(
            _handle,
            NativeMethods.HwndTopmost,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
    }

    private void OnForegroundChanged(
        IntPtr hook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint thread,
        uint time)
    {
        if (hwnd == IntPtr.Zero || hwnd == _handle)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            if (IsLoaded)
            {
                NudgeTopmost();
            }
        });
    }

    /// <summary>启动或换品种时补齐日线和分时。</summary>
    private async Task BackfillAsync(IReadOnlyList<SymbolDefinition> symbols)
    {
        foreach (var symbol in symbols)
        {
            try
            {
                if (!_levels.ContainsKey(symbol.Id) || _atr.GetValueOrDefault(symbol.Id) <= 0)
                {
                    var bars = await _market.GetDailyAsync(symbol);
                    if (bars.Count > 2)
                    {
                        var history = bars.Where(b => b.Date.Date < DateTime.Today).ToList();
                        if (history.Count < 15)
                        {
                            history = bars.ToList();
                        }

                        if (history.Count > 2)
                        {
                            _atr[symbol.Id] = Indicators.Atr(history);

                            var lastBar = history[^1];
                            var candidates = new List<Level>();
                            candidates.AddRange(Indicators.PivotLevels(Indicators.Pivot(lastBar)));
                            candidates.AddRange(Indicators.SwingLevels(history));
                            candidates.AddRange(Indicators.RoundLevels(lastBar.Close));
                            _levels[symbol.Id] = candidates;
                        }
                    }
                }

                var series = _series[symbol.Id];
                if (series.Count == 0)
                {
                    var ticks = await _market.GetIntradayAsync(symbol);
                    if (ticks.Count > 1)
                    {
                        series.AddRange(ticks);
                    }
                }
            }
            catch
            {
                // 预热失败不影响实时行情，等下一轮刷新即可。
            }
        }
    }

    private async Task RefreshAsync()
    {
        if (_busy || _symbols.Count == 0)
        {
            return;
        }

        _busy = true;
        try
        {
            var quotes = await _market.GetQuotesAsync(_symbols);
            var received = quotes.ToDictionary(q => q.SymbolId);

            for (var i = 0; i < _symbols.Count; i++)
            {
                var symbol = _symbols[i];
                var tile = _tiles[i];

                if (received.TryGetValue(symbol.Id, out var quote))
                {
                    ApplyQuote(symbol, tile, quote);
                }
                else if (_quotes.TryGetValue(symbol.Id, out var cached))
                {
                    // 所有数据源都没拿到，保留上一笔并把格子变灰。
                    var stale = new Quote
                    {
                        SymbolId = cached.SymbolId,
                        Name = cached.Name,
                        Price = cached.Price,
                        PrevClose = cached.PrevClose,
                        Open = cached.Open,
                        DayHigh = cached.DayHigh,
                        DayLow = cached.DayLow,
                        Decimals = cached.Decimals,
                        Suffix = cached.Suffix,
                        QuoteTime = cached.QuoteTime,
                        FetchedAt = DateTime.Now,
                        Provider = cached.Provider,
                        IsStale = true,
                    };
                    tile.Render(stale, ChartSeries(symbol.Id));
                }
            }
        }
        catch
        {
            // 网络异常时保留上一笔数据。
        }
        finally
        {
            _busy = false;
        }
    }

    private void ApplyQuote(SymbolDefinition symbol, SymbolTile tile, Quote quote)
    {
        _quotes[symbol.Id] = quote;

        var series = _series[symbol.Id];
        var minute = new DateTime(
            quote.QuoteTime.Year,
            quote.QuoteTime.Month,
            quote.QuoteTime.Day,
            quote.QuoteTime.Hour,
            quote.QuoteTime.Minute,
            0);

        if (series.Count > 0 && series[^1].Time == minute)
        {
            series[^1] = new Tick(minute, quote.Price);
        }
        else
        {
            series.Add(new Tick(minute, quote.Price));
            if (series.Count > AppConfig.MaxSeriesPoints)
            {
                series.RemoveRange(0, series.Count - AppConfig.MaxSeriesPoints);
            }
        }

        tile.Render(quote, ChartSeries(symbol.Id));

        var atr = _atr.GetValueOrDefault(symbol.Id);
        var levels = SelectedLevels(symbol.Id, quote.Price);
        var events = _alerts.Update(quote, atr, levels);

        foreach (var alert in events)
        {
            tile.Flash(alert.IsBullish, AlertShortText(alert, withPrefix: false));
        }

        if (events.Count > 0)
        {
            _settings.RecentAlerts = _alerts.History.ToList();
            SettingsStore.Save(_settings);
            BuildMenu();
        }
    }

    private List<double> ChartSeries(string symbolId)
    {
        var series = _series[symbolId];
        var start = Math.Max(0, series.Count - AppConfig.ChartPoints);
        var prices = new List<double>(series.Count - start);
        for (var i = start; i < series.Count; i++)
        {
            prices.Add(series[i].Price);
        }

        return prices;
    }

    private IReadOnlyList<Level> SelectedLevels(string symbolId, double price)
    {
        if (!_levels.TryGetValue(symbolId, out var candidates) || candidates.Count == 0)
        {
            return [];
        }

        var selected = Indicators.SelectKeyLevels(price, candidates);
        var result = new List<Level>(selected.Supports.Count + selected.Resistances.Count);
        result.AddRange(selected.Supports);
        result.AddRange(selected.Resistances);
        return result;
    }

    private void OnTileWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not SymbolTile tile)
        {
            return;
        }

        var index = _tiles.IndexOf(tile);
        if (index < 0)
        {
            return;
        }

        var catalog = SymbolCatalog.BuiltIn;
        var position = catalog.ToList().FindIndex(s => s.Id == _symbols[index].Id);
        var next = e.Delta > 0
            ? (position <= 0 ? catalog.Count - 1 : position - 1)
            : (position < 0 || position >= catalog.Count - 1 ? 0 : position + 1);

        _settings.Slots[index].Symbol = catalog[next];
        SettingsStore.Save(_settings);
        BuildTiles();
        _ = BackfillAndRefreshAsync();
        e.Handled = true;
    }

    /// <summary>从鼠标事件源往上找所属的格子。</summary>
    private static SymbolTile? FindTile(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is SymbolTile tile)
            {
                return tile;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private void OpenDetailFor(SymbolTile tile)
    {
        var index = _tiles.IndexOf(tile);
        if (index < 0)
        {
            return;
        }

        _detailWindow?.Close();
        _quotes.TryGetValue(_symbols[index].Id, out var quote);
        try
        {
            _detailWindow = new DetailWindow(_market, _symbols[index], _settings, quote);
            _detailWindow.Closed += (_, _) => _detailWindow = null;
            _detailWindow.Show();
            _detailWindow.Activate();
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }

    private async Task BackfillAndRefreshAsync()
    {
        await BackfillAsync(_symbols);
        await RefreshAsync();
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_settings, _market);
        _settingsWindow.Saved += OnSettingsSaved;
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void OnSettingsSaved()
    {
        SettingsStore.Save(_settings);
        AutoStart.Apply(_settings.AutoStart);

        _alerts.Options.ReversalAtrFactor = 0.06 * _settings.Sensitivity;
        _alerts.Options.BreakAtrFactor = 0.10 * _settings.Sensitivity;
        _alerts.Options.ReversalMinPercent = 0.0008 * _settings.Sensitivity;
        _alerts.Options.EnableReversalAlerts = _settings.ReversalAlerts;
        _alerts.Options.EnableBreakAlerts = _settings.BreakoutAlerts;
        _alerts.Options.EnableLevelAlerts = _settings.LevelAlerts;

        _refreshTimer.Interval = TimeSpan.FromSeconds(_settings.RefreshSeconds);
        Lang.SetLanguage(_settings.Language);

        ApplyAppearance();
        BuildTiles();
        BuildMenu();
        ApplyLayout();
        NudgeTopmost();

        _levels.Clear();
        _ = BackfillAndRefreshAsync();
    }

    private void OnRelocateClick(object sender, RoutedEventArgs e)
    {
        _settings.BarLeft = null;
        SettingsStore.Save(_settings);
        ApplyLayout();
        NudgeTopmost();
    }

    private void OnExitClick(object sender, RoutedEventArgs e)
    {
        _refreshTimer.Stop();
        _topmostTimer.Stop();
        Application.Current.Shutdown();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        _dragging = true;
        _dragged = false;
        _pressedTile = FindTile(e.OriginalSource as DependencyObject);
        _dragOriginScreenX = PointToScreen(e.GetPosition(this)).X;
        _dragOriginLeft = Left;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (!_dragging || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (scale <= 0)
        {
            scale = 1;
        }

        var currentX = PointToScreen(e.GetPosition(this)).X;
        var target = _dragOriginLeft + ((currentX - _dragOriginScreenX) / scale);
        var maxLeft = Math.Max(0, SystemParameters.PrimaryScreenWidth - Width);
        Left = Math.Round(Math.Clamp(target, 0, maxLeft));

        if (Math.Abs(Left - _dragOriginLeft) >= 4)
        {
            _dragged = true;
        }

        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        ReleaseMouseCapture();
        e.Handled = true;

        if (_dragged)
        {
            _settings.BarLeft = Left;
            SettingsStore.Save(_settings);
            NudgeTopmost();
        }
        else if (_pressedTile is not null && _settings.OpenDetailOnClick)
        {
            // 没有移动就是一次点击，打开该品种的大图。
            OpenDetailFor(_pressedTile);
        }

        _pressedTile = null;
    }
}

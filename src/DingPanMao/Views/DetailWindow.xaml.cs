using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DingPanMao.Config;
using DingPanMao.Controls;
using DingPanMao.Models;
using DingPanMao.Services;

namespace DingPanMao.Views;

/// <summary>点击格子后弹出的详情窗口：左边行情图表，右边可以跟 AI 分析、对话。</summary>
public partial class DetailWindow : Window
{
    private const int DailyCount = 60;

    private static readonly TimeSpan DailyRefreshInterval = TimeSpan.FromMinutes(5);

    /// <summary>流式输出时重渲染 Markdown 的最小间隔，避免每来一个字符就重排。</summary>
    private static readonly TimeSpan RenderThrottle = TimeSpan.FromMilliseconds(160);

    private static readonly Brush UserBubble = new SolidColorBrush(Color.FromRgb(0x2C, 0x4A, 0x6E));

    private static readonly Brush MutedBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0xA5, 0xB4));

    private static readonly Brush ThinkingBackground = new SolidColorBrush(Color.FromRgb(0x1C, 0x1F, 0x27));

    private static readonly Brush ThinkingBorder = new SolidColorBrush(Color.FromArgb(0x66, 0x7C, 0x87, 0x97));

    private readonly MarketDataService _market;

    private readonly AiAnalysisService _ai = new();

    private readonly SymbolDefinition _symbol;

    private readonly AppSettings _settings;

    private readonly DispatcherTimer _timer = new();

    private readonly FlowDocument _chat = new() { PagePadding = new Thickness(0) };

    /// <summary>多轮对话历史，每次请求整体发给模型。</summary>
    private readonly List<AiMessage> _conversation = [];

    private readonly StringBuilder _answerText = new();

    private readonly StringBuilder _thinkingText = new();

    private Section? _answerSection;

    private Section? _thinkingSection;

    /// <summary>折叠状态下只改这个 Run 的文本，避免整块重排。</summary>
    private Run? _thinkingSummaryRun;

    /// <summary>展开时用节流重排，思考内容可能有上万字。</summary>
    private DispatcherTimer? _thinkingRenderTimer;

    private DateTime _lastRender = DateTime.MinValue;

    private Quote? _quote;

    private IReadOnlyList<DailyBar> _bars = [];

    private IReadOnlyList<Tick> _ticks = [];

    private CancellationTokenSource? _aiCts;

    private DateTime _lastDailyAt = DateTime.MinValue;

    private bool _busy;

    private bool _aiRunning;

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

        ChatBox.Document = _chat;
        _chat.FontSize = 11;
        _chat.LineHeight = 18;

        var name = symbol.NameFor(Lang.CurrentLanguage);
        Title = name;
        NameText.Text = name;
        IntradayLabel.Text = Lang.T("detail.intraday");
        DailyLabel.Text = Lang.T("detail.daily");
        AiTitle.Text = Lang.T("ai.title");
        AiRunButton.Content = Lang.T("ai.run");
        AiSendButton.Content = Lang.T("ai.send");
        AiCopyButton.Content = Lang.T("ai.copyAll");
        AiInput.ToolTip = Lang.T("ai.placeholder");
        ThinkingToggle.Content = Lang.T("ai.thinkingLabel");
        ThinkingToggle.ToolTip = Lang.T("ai.thinkingHint");

        IntradayChart.Configure(symbol.Decimals, symbol.Suffix, settings.RedUpGreenDown);
        DailyChart.Configure(symbol.Decimals, symbol.Suffix, settings.RedUpGreenDown);
        IntradayChart.HoverChanged += text => IntradayHover.Text = text;
        DailyChart.HoverChanged += text => DailyHover.Text = text;

        ApplyHeader();
        ApplyAiPanelVisibility();
        AppendHint(Lang.T("ai.hint"));

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = Math.Max(0, (SystemParameters.PrimaryScreenWidth - Width) / 2);
        Top = Math.Max(40, (SystemParameters.PrimaryScreenHeight - Height) / 2 - 60);

        _timer.Interval = TimeSpan.FromSeconds(Math.Max(settings.RefreshSeconds, 3));
        _timer.Tick += async (_, _) => await RefreshAsync();

        Loaded += async (_, _) =>
        {
            await RefreshAsync();
            _timer.Start();
        };

        Closed += (_, _) =>
        {
            _timer.Stop();
            _aiCts?.Cancel();
            _ai.Dispose();
        };
    }

    private Color UpColor => _settings.RedUpGreenDown
        ? Color.FromRgb(0xFF, 0x4D, 0x4F)
        : Color.FromRgb(0x21, 0xC5, 0x5D);

    private Color DownColor => _settings.RedUpGreenDown
        ? Color.FromRgb(0x21, 0xC5, 0x5D)
        : Color.FromRgb(0xFF, 0x4D, 0x4F);

    private void ApplyAiPanelVisibility()
    {
        if (_settings.AiEnabled)
        {
            AiPanel.Visibility = Visibility.Visible;
            Width = 1100;
            MinWidth = 800;
        }
        else
        {
            AiPanel.Visibility = Visibility.Collapsed;
            Width = 620;
            MinWidth = 460;
        }
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

    private async Task RefreshAsync()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        try
        {
            var quotes = await _market.GetQuotesAsync([_symbol]);
            if (quotes.Count > 0)
            {
                _quote = quotes[0];
                ApplyHeader();
            }

            var ticks = await _market.GetIntradayAsync(_symbol);
            if (ticks.Count > 1)
            {
                _ticks = ticks;
                IntradayChart.SetLine(
                    ticks.Select(t => new ChartPoint(t.Time, t.Price)).ToList(),
                    _quote?.PrevClose ?? 0);
            }

            if (DateTime.Now - _lastDailyAt >= DailyRefreshInterval)
            {
                var bars = await _market.GetDailyAsync(_symbol, DailyCount);
                if (bars.Count > 1)
                {
                    _bars = bars;
                    DailyChart.SetCandles(bars, double.NaN);
                    _lastDailyAt = DateTime.Now;
                }
            }
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
        finally
        {
            _busy = false;
        }
    }

    // ---- 对话渲染 ----

    private void AppendHint(string text)
    {
        _chat.Blocks.Add(new Paragraph(new Run(text))
        {
            Foreground = MutedBrush,
            FontSize = 10.5,
            LineHeight = 17,
            Margin = new Thickness(0, 0, 0, 10),
        });
    }

    private void AppendUserMessage(string text)
    {
        _conversation.Add(new AiMessage("user", text));
        _chat.Blocks.Add(new Paragraph(new Run(text))
        {
            Background = UserBubble,
            Foreground = Brushes.White,
            FontSize = 11,
            Padding = new Thickness(9, 6, 9, 6),
            Margin = new Thickness(44, 0, 0, 10),
            LineHeight = 18,
        });
        ChatBox.ScrollToEnd();
    }

    /// <summary>开始一轮回复：先放思考块（带「正在思考」提示），再放回复块。</summary>
    private void BeginAssistant()
    {
        _answerText.Clear();
        _thinkingText.Clear();
        _lastRender = DateTime.MinValue;

        _thinkingSection = new Section();
        _chat.Blocks.Add(_thinkingSection);

        // 请求刚发出、模型还没吐字时，先给出「正在思考…」的反馈
        RenderThinking();

        _answerSection = new Section();
        _chat.Blocks.Add(_answerSection);
        ChatBox.ScrollToEnd();
    }

    private void AppendThinkingDelta(string text)
    {
        if (_thinkingSection is null)
        {
            return;
        }

        _thinkingText.Append(text);

        if (ThinkingToggle.IsChecked == true)
        {
            // 展开状态：节流重排，避免每个片段都重建整块
            _thinkingRenderTimer ??= CreateThinkingTimer();
            _thinkingRenderTimer.Stop();
            _thinkingRenderTimer.Start();
        }
        else if (_thinkingSummaryRun is not null)
        {
            // 折叠状态：只改一行文字，代价极小
            _thinkingSummaryRun.Text = ThinkingSummary();
        }
    }

    private DispatcherTimer CreateThinkingTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            RenderThinking();
        };
        return timer;
    }

    private string ThinkingSummary()
        => _thinkingText.Length > 0
            ? $"{Lang.T("ai.thinkingLabel")} · {_thinkingText.Length} {Lang.T("ai.chars")}"
            : Lang.T("ai.thinking");

    private void OnThinkingToggleClick(object sender, RoutedEventArgs e) => RenderThinking();

    /// <summary>按开关状态渲染思考块：关闭时只留一行标题和字数。</summary>
    private void RenderThinking()
    {
        if (_thinkingSection is null)
        {
            return;
        }

        _thinkingSection.Blocks.Clear();

        var expanded = ThinkingToggle.IsChecked == true;

        var paragraph = new Paragraph
        {
            FontSize = 10.5,
            LineHeight = 17,
            Foreground = MutedBrush,
            Margin = new Thickness(0, 0, 0, expanded ? 4 : 10),
        };

        _thinkingSummaryRun = new Run((expanded ? "▾ " : "▸ ") + ThinkingSummary());
        paragraph.Inlines.Add(_thinkingSummaryRun);

        _thinkingSection.Blocks.Add(paragraph);

        if (expanded && _thinkingText.Length > 0)
        {
            // 上万字一次性排版同样会卡，超出部分截断
            const int maxChars = 4000;
            var body = _thinkingText.Length > maxChars
                ? _thinkingText.ToString(0, maxChars) + " …"
                : _thinkingText.ToString();

            _thinkingSection.Blocks.Add(new Paragraph(new Run(body))
            {
                FontSize = 10.5,
                LineHeight = 17,
                Foreground = MutedBrush,
                Background = ThinkingBackground,
                Padding = new Thickness(9, 7, 9, 7),
                Margin = new Thickness(0, 0, 0, 10),
                BorderBrush = ThinkingBorder,
                BorderThickness = new Thickness(2, 0, 0, 0),
            });
        }

        ChatBox.ScrollToEnd();
    }

    private void AppendAnswerDelta(string text)
    {
        _answerText.Append(text);

        // 节流重排：Markdown 需要整段解析，太频繁会卡
        if ((DateTime.Now - _lastRender) < RenderThrottle)
        {
            return;
        }

        _lastRender = DateTime.Now;
        RenderAnswer();
    }

    private void RenderAnswer()
    {
        if (_answerSection is null)
        {
            return;
        }

        _answerSection.Blocks.Clear();
        MarkdownRenderer.Append(_answerSection.Blocks, _answerText.ToString());
        ChatBox.ScrollToEnd();
    }

    private void FinishAssistant(string fallbackSuffix)
    {
        var text = _answerText.ToString();
        if (string.IsNullOrWhiteSpace(text))
        {
            text = fallbackSuffix;
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            _conversation.Add(new AiMessage("assistant", text));
        }

        RenderAnswer();

        // 回复很长时滚到底部会把开头的操作建议顶出视野，这里滚回本条回答的开头
        ScrollToAnswerStart();

        _answerSection = null;
        _thinkingSection = null;
    }

    private void ScrollToAnswerStart()
    {
        if (_answerSection is null)
        {
            ChatBox.ScrollToEnd();
            return;
        }

        try
        {
            var rect = _answerSection.ContentStart.GetCharacterRect(LogicalDirection.Forward);
            if (rect.IsEmpty)
            {
                ChatBox.ScrollToEnd();
                return;
            }

            ChatBox.ScrollToVerticalOffset(ChatBox.VerticalOffset + rect.Top - 8);
        }
        catch
        {
            ChatBox.ScrollToEnd();
        }
    }

    // ---- 发送 ----

    private async Task SendAsync(string question)
    {
        if (_aiRunning)
        {
            return;
        }

        AppendUserMessage(question);

        _aiRunning = true;
        _aiCts = new CancellationTokenSource();
        AiRunButton.Content = Lang.T("ai.stop");
        AiSendButton.IsEnabled = false;

        BeginAssistant();

        try
        {
            var context = await _ai.BuildContextAsync(_settings, _symbol, _quote, _bars, _ticks, _aiCts.Token);

            // SSE 回调跑在后台线程，必须切回 UI 线程再改控件
            await _ai.ChatAsync(
                _settings,
                context,
                _conversation.ToList(),
                text => Dispatcher.BeginInvoke(() => AppendAnswerDelta(text)),
                text => Dispatcher.BeginInvoke(() => AppendThinkingDelta(text)),
                _aiCts.Token);

            Dispatcher.Invoke(() => FinishAssistant(string.Empty));
        }
        catch (OperationCanceledException)
        {
            Dispatcher.Invoke(() => FinishAssistant(Lang.T("ai.cancelled")));
        }
        catch (Exception ex)
        {
            App.Log(ex);
            Dispatcher.Invoke(() => FinishAssistant(Lang.T("ai.failed") + Environment.NewLine + ex.Message));
        }
        finally
        {
            _aiRunning = false;
            _aiCts?.Dispose();
            _aiCts = null;
            AiRunButton.Content = Lang.T("ai.run");
            AiSendButton.IsEnabled = true;
            AiInput.Focus();
        }
    }

    private async void OnRunAiClick(object sender, RoutedEventArgs e)
    {
        if (_aiRunning)
        {
            _aiCts?.Cancel();
            return;
        }

        await SendAsync(Lang.T("ai.defaultQuestion"));
    }

    private async void OnSendClick(object sender, RoutedEventArgs e)
    {
        var text = AiInput.Text.Trim();
        if (text.Length == 0 || _aiRunning)
        {
            return;
        }

        AiInput.Clear();
        await SendAsync(text);
    }

    private void OnAiInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            e.Handled = true;
            OnSendClick(sender, new RoutedEventArgs());
        }
    }

    private void OnCopyAiClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var text = string.Join(
                Environment.NewLine + Environment.NewLine,
                _conversation.Select(m => (m.Role == "user" ? "我：" : "AI：") + m.Content));

            if (!string.IsNullOrWhiteSpace(text))
            {
                Clipboard.SetText(text);
                AiCopyButton.Content = Lang.T("ai.copied");
                DispatcherTimer? reset = null;
                reset = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                reset.Tick += (_, _) =>
                {
                    AiCopyButton.Content = Lang.T("ai.copyAll");
                    reset!.Stop();
                };
                reset.Start();
            }
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }
}

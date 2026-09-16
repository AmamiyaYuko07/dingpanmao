using DingPanMao.Models;

namespace DingPanMao.Services;

/// <summary>提醒阈值，全部可调，并且按品种的 ATR 自适应。</summary>
public sealed class AlertOptions
{
    /// <summary>反转确认幅度 = ATR × 该系数。</summary>
    public double ReversalAtrFactor { get; set; } = 0.06;

    /// <summary>反转确认幅度的相对下限（相对于价格的百分比）。</summary>
    public double ReversalMinPercent { get; set; } = 0.0008;

    /// <summary>价格离开极值后需要持续这么久才确认反转。</summary>
    public TimeSpan ReversalConfirmDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>日内振幅低于 ATR × 该系数时不做反转判定，避免窄幅震荡误报。</summary>
    public double SessionRangeAtrFactor { get; set; } = 0.15;

    /// <summary>突破日内极值的最小间距 = ATR × 该系数。</summary>
    public double BreakAtrFactor { get; set; } = 0.10;

    public TimeSpan BreakCooldown { get; set; } = TimeSpan.FromMinutes(10);

    public TimeSpan LevelCooldown { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>报价时间落后于本地时间超过这个长度就视为休市，暂停提醒。</summary>
    public TimeSpan StaleTolerance { get; set; } = TimeSpan.FromMinutes(5);

    public bool EnableReversalAlerts { get; set; } = true;

    public bool EnableBreakAlerts { get; set; } = true;

    public bool EnableLevelAlerts { get; set; } = true;
}

/// <summary>三类提醒的判定：日内极值反转、日内极值突破、关键位穿越。</summary>
public sealed class AlertEngine
{
    private const int MaxHistory = 3;

    private readonly Dictionary<string, SymbolState> _states = [];
    private readonly List<AlertEvent> _history = [];

    public AlertEngine(AlertOptions? options = null) => Options = options ?? new AlertOptions();

    public AlertOptions Options { get; }

    /// <summary>最近的提醒记录，最新在前。</summary>
    public IReadOnlyList<AlertEvent> History => _history;

    /// <summary>把上次运行保存下来的提醒恢复到列表里。</summary>
    public void RestoreHistory(IEnumerable<AlertEvent> events)
    {
        _history.Clear();
        _history.AddRange(events.Take(MaxHistory));
    }

    public IReadOnlyList<AlertEvent> Update(Quote quote, double atr, IReadOnlyList<Level> levels)
    {
        var events = new List<AlertEvent>();
        var now = quote.FetchedAt;
        var price = quote.Price;

        if (!_states.TryGetValue(quote.SymbolId, out var state))
        {
            state = new SymbolState();
            _states[quote.SymbolId] = state;
        }

        var tickSize = Math.Max(Math.Abs(price) * Options.ReversalMinPercent, 1e-6);
        var reversalMove = Math.Max(atr * Options.ReversalAtrFactor, tickSize);
        var breakMove = Math.Max(atr * Options.BreakAtrFactor, tickSize * 1.5);
        var sessionRange = quote.DayHigh - quote.DayLow;
        var rangeEnough = atr <= 0 || sessionRange >= atr * Options.SessionRangeAtrFactor;

        if (!state.Initialized || state.TradingDate != quote.QuoteTime.Date)
        {
            BeginSession(state, quote, reversalMove);
        }

        // 休市或数据源滞后时不判定，否则收盘后价格不动会误报。
        if (quote.QuoteTime != default && now - quote.QuoteTime > Options.StaleTolerance)
        {
            state.PreviousPrice = price;
            return events;
        }

        // 当日极值被刷新时才重新进入待确认状态，避免同一个低点/高点反复提醒。
        if (quote.DayLow > 0 && quote.DayLow <= state.ArmedLow - 1e-9)
        {
            state.ArmedLow = quote.DayLow;
            state.BottomArmed = true;
            state.LowNearAt = now;
        }
        else if (state.BottomArmed && quote.DayLow > 0 && price - quote.DayLow < reversalMove)
        {
            state.LowNearAt = now;
        }

        if (quote.DayHigh > 0 && quote.DayHigh >= state.ArmedHigh + 1e-9)
        {
            state.ArmedHigh = quote.DayHigh;
            state.TopArmed = true;
            state.HighNearAt = now;
        }
        else if (state.TopArmed && quote.DayHigh > 0 && quote.DayHigh - price < reversalMove)
        {
            state.HighNearAt = now;
        }

        if (Options.EnableReversalAlerts && rangeEnough)
        {
            if (state.BottomArmed
                && quote.DayLow > 0
                && price - quote.DayLow >= reversalMove
                && now - state.LowNearAt >= Options.ReversalConfirmDelay)
            {
                state.BottomArmed = false;
                events.Add(Create(now, quote, AlertKind.BottomRebound, price, quote.DayLow, string.Empty, true));
            }

            if (state.TopArmed
                && quote.DayHigh > 0
                && quote.DayHigh - price >= reversalMove
                && now - state.HighNearAt >= Options.ReversalConfirmDelay)
            {
                state.TopArmed = false;
                events.Add(Create(now, quote, AlertKind.TopReversal, price, quote.DayHigh, string.Empty, false));
            }
        }

        if (Options.EnableBreakAlerts)
        {
            if (quote.DayHigh > 0
                && price >= quote.DayHigh
                && price - state.LastBreakHighPrice >= breakMove
                && now - state.LastBreakHighAt >= Options.BreakCooldown)
            {
                state.LastBreakHighPrice = price;
                state.LastBreakHighAt = now;
                events.Add(Create(now, quote, AlertKind.BreakHigh, price, price, string.Empty, true));
            }

            if (quote.DayLow > 0
                && price <= quote.DayLow
                && state.LastBreakLowPrice - price >= breakMove
                && now - state.LastBreakLowAt >= Options.BreakCooldown)
            {
                state.LastBreakLowPrice = price;
                state.LastBreakLowAt = now;
                events.Add(Create(now, quote, AlertKind.BreakLow, price, price, string.Empty, false));
            }
        }

        if (Options.EnableLevelAlerts && !double.IsNaN(state.PreviousPrice) && levels.Count > 0)
        {
            foreach (var level in levels)
            {
                var crossedUp = state.PreviousPrice < level.Price && price >= level.Price;
                var crossedDown = state.PreviousPrice > level.Price && price <= level.Price;
                if (!crossedUp && !crossedDown)
                {
                    continue;
                }

                var key = (long)Math.Round(level.Price / Math.Max(reversalMove / 100.0, 1e-9));
                if (state.LevelAlertTimes.TryGetValue(key, out var lastAt)
                    && now - lastAt < Options.LevelCooldown)
                {
                    continue;
                }

                state.LevelAlertTimes[key] = now;
                events.Add(Create(
                    now,
                    quote,
                    crossedUp ? AlertKind.LevelUp : AlertKind.LevelDown,
                    price,
                    level.Price,
                    level.Note,
                    crossedUp));
            }
        }

        state.PreviousPrice = price;

        if (events.Count > 0)
        {
            _history.InsertRange(0, events);
            if (_history.Count > MaxHistory)
            {
                _history.RemoveRange(MaxHistory, _history.Count - MaxHistory);
            }
        }

        return events;
    }

    private static AlertEvent Create(
        DateTime now,
        Quote quote,
        AlertKind kind,
        double price,
        double reference,
        string note,
        bool bullish) => new()
        {
            Time = now,
            SymbolId = quote.SymbolId,
            Kind = kind,
            Price = price,
            Reference = reference,
            Note = note,
            IsBullish = bullish,
        };

    /// <summary>
    /// 开始跟踪一个交易日。极值直接取自行情源的当日最高/最低，
    /// 只有当价格此刻就贴在极值附近时才进入待确认状态，避免启动瞬间拿历史高点误报。
    /// </summary>
    private static void BeginSession(SymbolState state, Quote quote, double reversalMove)
    {
        var now = quote.FetchedAt;
        var price = quote.Price;

        state.LowNearAt = now;
        state.HighNearAt = now;
        state.ArmedLow = quote.DayLow > 0 ? quote.DayLow : price;
        state.ArmedHigh = quote.DayHigh > 0 ? quote.DayHigh : price;
        state.BottomArmed = quote.DayLow > 0 && price - quote.DayLow < reversalMove;
        state.TopArmed = quote.DayHigh > 0 && quote.DayHigh - price < reversalMove;
        state.LastBreakHighPrice = quote.DayHigh > 0 ? quote.DayHigh : price;
        state.LastBreakLowPrice = quote.DayLow > 0 ? quote.DayLow : price;
        state.LastBreakHighAt = DateTime.MinValue;
        state.LastBreakLowAt = DateTime.MinValue;
        state.PreviousPrice = double.NaN;
        state.LevelAlertTimes.Clear();
        state.TradingDate = quote.QuoteTime.Date;
        state.Initialized = true;
    }

    private sealed class SymbolState
    {
        public bool Initialized { get; set; }

        public DateTime TradingDate { get; set; } = DateTime.MinValue;

        public bool BottomArmed { get; set; }

        public DateTime LowNearAt { get; set; } = DateTime.MinValue;

        public double ArmedLow { get; set; }

        public bool TopArmed { get; set; }

        public DateTime HighNearAt { get; set; } = DateTime.MinValue;

        public double ArmedHigh { get; set; }

        public double LastBreakHighPrice { get; set; } = double.MinValue;

        public DateTime LastBreakHighAt { get; set; } = DateTime.MinValue;

        public double LastBreakLowPrice { get; set; } = double.MaxValue;

        public DateTime LastBreakLowAt { get; set; } = DateTime.MinValue;

        public double PreviousPrice { get; set; } = double.NaN;

        public Dictionary<long, DateTime> LevelAlertTimes { get; } = [];
    }
}

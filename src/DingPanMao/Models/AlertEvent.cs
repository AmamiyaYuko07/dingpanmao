namespace DingPanMao.Models;

/// <summary>提醒类型。标题文案由界面层按当前语言渲染。</summary>
public enum AlertKind
{
    BottomRebound,
    TopReversal,
    BreakHigh,
    BreakLow,
    LevelUp,
    LevelDown,
}

/// <summary>一条提醒。文本字段存的是语言键和数字，保证切换语言后历史记录也能正确显示。</summary>
public sealed record AlertEvent
{
    public required DateTime Time { get; init; }

    public required string SymbolId { get; init; }

    public required AlertKind Kind { get; init; }

    /// <summary>触发时的现价。</summary>
    public required double Price { get; init; }

    /// <summary>触发时对应的极值或关键位。</summary>
    public required double Reference { get; init; }

    /// <summary>关键位的来源说明（如「枢轴位」「09-02 低点」），可为空。</summary>
    public string Note { get; init; } = string.Empty;

    /// <summary>true 表示看涨方向，用于决定闪烁颜色。</summary>
    public required bool IsBullish { get; init; }
}

namespace DingPanMao.Models;

/// <summary>一次实时快照。</summary>
public sealed class Quote
{
    public required string SymbolId { get; init; }

    public string Name { get; set; } = string.Empty;

    public double Price { get; set; }

    public double PrevClose { get; set; }

    public double Open { get; set; }

    public double DayHigh { get; set; }

    public double DayLow { get; set; }

    public int Decimals { get; set; } = 2;

    public string Suffix { get; set; } = string.Empty;

    /// <summary>行情源给出的报价时间。</summary>
    public DateTime QuoteTime { get; set; }

    /// <summary>本地抓取时间。</summary>
    public DateTime FetchedAt { get; set; }

    /// <summary>实际提供这笔数据的数据源名。</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>所有数据源都失败时置为 true，界面上会把格子变灰。</summary>
    public bool IsStale { get; set; }

    public double Change => Price - PrevClose;

    public double ChangePercent => PrevClose <= 0 ? 0 : Change / PrevClose * 100.0;

    /// <summary>按品种配置的小数位格式化价格。</summary>
    public string FormatPrice() => Price.ToString($"F{Decimals}") + Suffix;

    public string FormatChangePercent()
    {
        var sign = ChangePercent >= 0 ? "+" : string.Empty;
        return $"{sign}{ChangePercent:F2}%";
    }
}

using DingPanMao.Models;

namespace DingPanMao.Services;

/// <summary>筛选后的关键位集合。</summary>
public sealed record KeyLevels(IReadOnlyList<Level> Supports, IReadOnlyList<Level> Resistances);

/// <summary>技术指标与关键位计算。</summary>
public static class Indicators
{
    /// <summary>平均真实波幅，只应传入已收盘的交易日。</summary>
    public static double Atr(IReadOnlyList<DailyBar> bars, int period = 14)
    {
        if (bars.Count < 2)
        {
            return 0;
        }

        var count = Math.Min(period, bars.Count - 1);
        var sum = 0.0;
        for (var i = bars.Count - count; i < bars.Count; i++)
        {
            var prevClose = bars[i - 1].Close;
            var bar = bars[i];
            var trueRange = Math.Max(
                bar.High - bar.Low,
                Math.Max(Math.Abs(bar.High - prevClose), Math.Abs(bar.Low - prevClose)));
            sum += trueRange;
        }

        return sum / count;
    }

    /// <summary>基于上一交易日最高/最低/收盘的枢轴点。</summary>
    public static PivotSet Pivot(DailyBar prev)
    {
        var pp = (prev.High + prev.Low + prev.Close) / 3.0;
        var range = prev.High - prev.Low;
        return new PivotSet(
            pp,
            2 * pp - prev.Low,
            pp + range,
            prev.High + 2 * (pp - prev.Low),
            2 * pp - prev.High,
            pp - range,
            prev.Low - 2 * (prev.High - pp));
    }

    public static IReadOnlyList<Level> PivotLevels(PivotSet pivot) =>
    [
        new(pivot.S1, LevelKind.Support, LevelSource.Pivot, 2.0, "S1"),
        new(pivot.S2, LevelKind.Support, LevelSource.Pivot, 1.6, "S2"),
        new(pivot.S3, LevelKind.Support, LevelSource.Pivot, 1.1, "S3"),
        new(pivot.PP, LevelKind.Support, LevelSource.Pivot, 2.5, "多空分界"),
        new(pivot.R1, LevelKind.Resistance, LevelSource.Pivot, 2.0, "R1"),
        new(pivot.R2, LevelKind.Resistance, LevelSource.Pivot, 1.6, "R2"),
        new(pivot.R3, LevelKind.Resistance, LevelSource.Pivot, 1.1, "R3"),
    ];

    /// <summary>近 N 个交易日的摆动高低点，越新的点权重越高。</summary>
    public static IReadOnlyList<Level> SwingLevels(IReadOnlyList<DailyBar> bars, int window = 5, int lookback = 90)
    {
        var result = new List<Level>();
        var count = bars.Count;
        if (count < (window * 2) + 1)
        {
            return result;
        }

        var start = Math.Max(window, count - lookback);
        var span = Math.Max(1, count - (window * 2) - start);

        for (var i = start; i < count - window; i++)
        {
            var isHigh = true;
            var isLow = true;
            for (var j = i - window; j <= i + window; j++)
            {
                if (j == i)
                {
                    continue;
                }

                if (bars[j].High >= bars[i].High)
                {
                    isHigh = false;
                }

                if (bars[j].Low <= bars[i].Low)
                {
                    isLow = false;
                }

                if (!isHigh && !isLow)
                {
                    break;
                }
            }

            var weight = 1.2 + ((i - start) / (double)span * 0.8);
            if (isHigh)
            {
                result.Add(new Level(bars[i].High, LevelKind.Resistance, LevelSource.Swing, weight, $"{bars[i].Date:MM-dd} 高点"));
            }

            if (isLow)
            {
                result.Add(new Level(bars[i].Low, LevelKind.Support, LevelSource.Swing, weight, $"{bars[i].Date:MM-dd} 低点"));
            }
        }

        return result;
    }

    /// <summary>整数关口。</summary>
    public static IReadOnlyList<Level> RoundLevels(double price, double step = 50, int count = 3)
    {
        var result = new List<Level>();
        var baseValue = Math.Floor(price / step) * step;
        for (var i = -count; i <= count; i++)
        {
            var value = baseValue + (i * step);
            if (value > 0)
            {
                result.Add(new Level(value, LevelKind.Support, LevelSource.Round, 1.0, "整数关口"));
            }
        }

        return result;
    }

    /// <summary>
    /// 把三类候选位合并成最终提醒表：限定在现价附近、相近价位合并、每个方向最多保留若干个。
    /// </summary>
    public static KeyLevels SelectKeyLevels(
        double price,
        IEnumerable<Level> candidates,
        double rangePercent = 5.0,
        double mergeDistance = 2.0,
        int maxPerSide = 3)
    {
        var min = price * (1 - (rangePercent / 100.0));
        var max = price * (1 + (rangePercent / 100.0));

        var sorted = candidates
            .Where(level => level.Price >= min && level.Price <= max)
            .OrderBy(level => level.Price)
            .ToList();

        var merged = new List<Level>(sorted.Count);
        foreach (var level in sorted)
        {
            if (merged.Count > 0 && Math.Abs(level.Price - merged[^1].Price) <= mergeDistance)
            {
                var keep = merged[^1];
                merged[^1] = keep with
                {
                    Weight = keep.Weight + level.Weight,
                    Note = keep.Weight >= level.Weight ? keep.Note : level.Note,
                };
            }
            else
            {
                merged.Add(level);
            }
        }

        var supports = merged
            .Where(level => level.Price < price)
            .OrderByDescending(level => level.Weight)
            .ThenByDescending(level => level.Price)
            .Take(maxPerSide)
            .Select(level => level with { Kind = LevelKind.Support })
            .OrderByDescending(level => level.Price)
            .ToList();

        var resistances = merged
            .Where(level => level.Price > price)
            .OrderByDescending(level => level.Weight)
            .ThenBy(level => level.Price)
            .Take(maxPerSide)
            .Select(level => level with { Kind = LevelKind.Resistance })
            .OrderBy(level => level.Price)
            .ToList();

        return new KeyLevels(supports, resistances);
    }
}

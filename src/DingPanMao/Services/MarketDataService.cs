using System.Net.Http;
using DingPanMao.Models;

namespace DingPanMao.Services;

/// <summary>
/// 行情门面：按优先级依次尝试各个数据源，某个品种在某个源上拿不到就自动降级到下一个。
/// </summary>
public sealed class MarketDataService : IDisposable
{
    private readonly HttpClient _http;
    private readonly List<IMarketDataProvider> _providers;

    public MarketDataService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");

        _providers =
        [
            new EastMoneyProvider(_http),
            new SinaProvider(_http),
        ];
        _providers.Sort((a, b) => a.Priority.CompareTo(b.Priority));
    }

    /// <summary>各数据源的名称，供设置界面展示。</summary>
    public IReadOnlyList<string> ProviderNames => _providers.ConvertAll(p => p.Name);

    /// <summary>
    /// 批量取实时行情。返回列表中缺少的品种代表所有数据源都没有拿到。
    /// </summary>
    public async Task<IReadOnlyList<Quote>> GetQuotesAsync(
        IReadOnlyList<SymbolDefinition> symbols,
        CancellationToken ct = default)
    {
        var collected = new Dictionary<string, Quote>();
        var pending = new List<SymbolDefinition>(symbols);

        foreach (var provider in _providers)
        {
            if (pending.Count == 0)
            {
                break;
            }

            var targets = pending.Where(provider.Supports).ToList();
            if (targets.Count == 0)
            {
                continue;
            }

            try
            {
                foreach (var quote in await provider.GetQuotesAsync(targets, ct).ConfigureAwait(false))
                {
                    collected[quote.SymbolId] = quote;
                }
            }
            catch
            {
                // 这个源整体失败，交给下一个源。
            }

            pending = pending.Where(s => !collected.ContainsKey(s.Id)).ToList();
        }

        var result = new List<Quote>(symbols.Count);
        foreach (var symbol in symbols)
        {
            if (collected.TryGetValue(symbol.Id, out var quote))
            {
                result.Add(quote);
            }
        }

        return result;
    }

    /// <summary>取日 K，同样按优先级降级。</summary>
    public async Task<IReadOnlyList<DailyBar>> GetDailyAsync(
        SymbolDefinition symbol,
        int count = 120,
        CancellationToken ct = default)
    {
        foreach (var provider in _providers)
        {
            if (!provider.Supports(symbol))
            {
                continue;
            }

            try
            {
                var bars = await provider.GetDailyAsync(symbol, count, ct).ConfigureAwait(false);
                if (bars.Count > 2)
                {
                    return bars;
                }
            }
            catch
            {
                // 换下一个源。
            }
        }

        return [];
    }

    /// <summary>取当日分时，用于走势图预热。</summary>
    public async Task<IReadOnlyList<Tick>> GetIntradayAsync(
        SymbolDefinition symbol,
        CancellationToken ct = default)
    {
        foreach (var provider in _providers)
        {
            if (!provider.Supports(symbol))
            {
                continue;
            }

            try
            {
                var ticks = await provider.GetIntradayAsync(symbol, ct).ConfigureAwait(false);
                if (ticks.Count > 1)
                {
                    return ticks;
                }
            }
            catch
            {
                // 换下一个源。
            }
        }

        return [];
    }

    /// <summary>按名称搜索品种，只有东方财富支持。</summary>
    public async Task<IReadOnlyList<SymbolDefinition>> SearchAsync(string keyword, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return [];
        }

        try
        {
            return await new EastMoneyProvider(_http).SearchAsync(keyword, ct).ConfigureAwait(false);
        }
        catch
        {
            return [];
        }
    }

    public void Dispose() => _http.Dispose();
}

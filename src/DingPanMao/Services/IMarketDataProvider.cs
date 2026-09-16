using DingPanMao.Models;

namespace DingPanMao.Services;

/// <summary>行情数据源。实现类需要保证失败时抛异常，由上层决定是否降级。</summary>
public interface IMarketDataProvider
{
    /// <summary>数据源标识，与 <see cref="SymbolDefinition.Provider"/> 对应。</summary>
    string Name { get; }

    /// <summary>优先级，数字越小越优先。</summary>
    int Priority { get; }

    /// <summary>是否支持该品种。</summary>
    bool Supports(SymbolDefinition symbol);

    /// <summary>批量取实时行情。返回的列表可能少于请求的品种，缺失的代表该源拿不到。</summary>
    Task<IReadOnlyList<Quote>> GetQuotesAsync(IReadOnlyList<SymbolDefinition> symbols, CancellationToken ct);

    /// <summary>取日 K，用于 ATR、枢轴点和摆动点。</summary>
    Task<IReadOnlyList<DailyBar>> GetDailyAsync(SymbolDefinition symbol, int count, CancellationToken ct);

    /// <summary>取当日分时，用于走势图预热。</summary>
    Task<IReadOnlyList<Tick>> GetIntradayAsync(SymbolDefinition symbol, CancellationToken ct);
}

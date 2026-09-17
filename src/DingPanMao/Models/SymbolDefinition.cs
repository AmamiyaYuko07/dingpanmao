namespace DingPanMao.Models;

/// <summary>品种分类，用于设置界面的分组展示。</summary>
public enum SymbolCategory
{
    Index,
    Equity,
    Commodity,
    Bond,
    Currency,
    Crypto,
    Other,
}

/// <summary>
/// 一个可显示的品种。Id 采用「数据源:代码」的形式，是所有本地状态的唯一键。
/// </summary>
public sealed record SymbolDefinition
{
    /// <summary>唯一键，例如 "eastmoney:122.XAU"。</summary>
    public required string Id { get; init; }

    /// <summary>默认显示名（数据源返回的中文名或用户自定义名）。</summary>
    public required string Name { get; init; }

    public SymbolCategory Category { get; init; } = SymbolCategory.Other;

    /// <summary>数据源标识，见 <see cref="Services.MarketDataService"/> 中注册的名字。</summary>
    public string Provider { get; init; } = "eastmoney";

    /// <summary>该数据源内的代码。</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>价格小数位。</summary>
    public int Decimals { get; init; } = 2;

    /// <summary>后缀，例如收益率的 "%"。</summary>
    public string Suffix { get; init; } = string.Empty;

    /// <summary>联网搜索时用的关键词，留空则用 <see cref="Name"/>。</summary>
    public string SearchKeyword { get; init; } = string.Empty;

    /// <summary>多语言名称，键是语言代码；找不到时回退到 <see cref="Name"/>。</summary>
    public IReadOnlyDictionary<string, string>? LocalizedNames { get; init; }

    public static SymbolDefinition EastMoney(string code, string name, int decimals = 2, SymbolCategory category = SymbolCategory.Other, string suffix = "")
        => new()
        {
            Id = $"eastmoney:{code}",
            Name = name,
            Provider = "eastmoney",
            Code = code,
            Decimals = decimals,
            Category = category,
            Suffix = suffix,
        };

    /// <summary>按语言取显示名。</summary>
    public string NameFor(string language)
    {
        if (LocalizedNames is not null && LocalizedNames.TryGetValue(language, out var localized))
        {
            return localized;
        }

        return Name;
    }

    /// <summary>取搜索关键词。</summary>
    public string KeywordForSearch()
        => string.IsNullOrWhiteSpace(SearchKeyword) ? Name : SearchKeyword;
}

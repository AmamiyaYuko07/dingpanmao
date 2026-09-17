using DingPanMao.Models;

namespace DingPanMao.Services;

/// <summary>内置品种库。用户也可以在设置里搜索添加任意品种。</summary>
public static class SymbolCatalog
{
    private static SymbolDefinition Em(
        string code,
        string name,
        int decimals,
        SymbolCategory category,
        string suffix = "",
        string? en = null,
        string? keyword = null) => new()
        {
            Id = $"eastmoney:{code}",
            Name = name,
            Provider = "eastmoney",
            Code = code,
            Decimals = decimals,
            Category = category,
            Suffix = suffix,
            SearchKeyword = keyword ?? string.Empty,
            LocalizedNames = en is null ? null : new Dictionary<string, string> { ["en"] = en },
        };

    /// <summary>首次启动时默认显示的两个品种。</summary>
    public static IReadOnlyList<SymbolDefinition> Defaults { get; } =
    [
        Em("122.XAU", "伦敦金", 2, SymbolCategory.Commodity, en: "Gold", keyword: "现货黄金"),
        Em("112.B00Y", "布伦特原油", 2, SymbolCategory.Commodity, en: "Brent"),
    ];

    public static IReadOnlyList<SymbolDefinition> BuiltIn { get; } =
    [
        // 指数
        Em("1.000001", "上证指数", 2, SymbolCategory.Index, en: "SSE Index", keyword: "上证指数"),
        Em("0.399001", "深证成指", 2, SymbolCategory.Index, en: "SZSE Index", keyword: "深证成指"),
        Em("0.399006", "创业板指", 2, SymbolCategory.Index, en: "ChiNext", keyword: "创业板指"),
        Em("1.000300", "沪深300", 2, SymbolCategory.Index, en: "CSI 300", keyword: "沪深300"),
        Em("100.HSI", "恒生指数", 2, SymbolCategory.Index, en: "Hang Seng", keyword: "恒生指数"),
        Em("100.HSCEI", "国企指数", 2, SymbolCategory.Index, en: "HSCEI", keyword: "国企指数"),
        Em("100.N225", "日经225", 2, SymbolCategory.Index, en: "Nikkei 225", keyword: "日经指数"),
        Em("100.GDAXI", "德国DAX30", 2, SymbolCategory.Index, en: "DAX", keyword: "德国DAX"),
        Em("100.FTSE", "英国富时100", 2, SymbolCategory.Index, en: "FTSE 100", keyword: "英国富时100"),
        Em("100.NDX", "纳斯达克100", 2, SymbolCategory.Index, en: "Nasdaq 100", keyword: "纳斯达克"),
        Em("100.DJIA", "道琼斯", 2, SymbolCategory.Index, en: "Dow Jones", keyword: "道琼斯"),
        Em("100.SPX", "标普500", 2, SymbolCategory.Index, en: "S&P 500", keyword: "标普500"),

        // 股票
        Em("1.600519", "贵州茅台", 2, SymbolCategory.Equity, en: "Moutai"),
        Em("0.300750", "宁德时代", 2, SymbolCategory.Equity, en: "CATL"),
        Em("1.601318", "中国平安", 2, SymbolCategory.Equity, en: "Ping An"),
        Em("116.00700", "腾讯控股", 3, SymbolCategory.Equity, en: "Tencent"),
        Em("105.AAPL", "苹果", 2, SymbolCategory.Equity, en: "Apple"),
        Em("105.NVDA", "英伟达", 2, SymbolCategory.Equity, en: "NVDA"),
        Em("105.TSLA", "特斯拉", 2, SymbolCategory.Equity, en: "Tesla"),
        Em("105.MSFT", "微软", 2, SymbolCategory.Equity, en: "MSFT"),

        // 商品
        Em("122.XAU", "伦敦金", 2, SymbolCategory.Commodity, en: "Gold", keyword: "现货黄金"),
        Em("101.GC00Y", "COMEX黄金", 1, SymbolCategory.Commodity, en: "COMEX Gold", keyword: "黄金期货"),
        Em("112.B00Y", "布伦特原油", 2, SymbolCategory.Commodity, en: "Brent"),
        Em("102.CL00Y", "NYMEX原油", 2, SymbolCategory.Commodity, en: "WTI", keyword: "原油"),
        Em("113.aum", "沪金主连", 2, SymbolCategory.Commodity, en: "SHFE Gold", keyword: "沪金"),
        Em("113.agm", "沪银主连", 2, SymbolCategory.Commodity, en: "SHFE Silver", keyword: "沪银"),

        // 债券
        Em("171.US10Y", "美国10年期国债收益率", 4, SymbolCategory.Bond, "%", "US 10Y", keyword: "美国国债收益率"),
        Em("171.US30Y", "美国30年期国债收益率", 4, SymbolCategory.Bond, "%", "US 30Y", keyword: "美国国债收益率"),

        // 外汇
        Em("100.UDI", "美元指数", 2, SymbolCategory.Currency, en: "DXY", keyword: "美元指数"),
        Em("133.USDCNH", "美元兑离岸人民币", 4, SymbolCategory.Currency, en: "USD/CNH", keyword: "人民币汇率"),
    ];

    public static SymbolDefinition? FindById(string id)
        => BuiltIn.FirstOrDefault(s => s.Id == id);
}

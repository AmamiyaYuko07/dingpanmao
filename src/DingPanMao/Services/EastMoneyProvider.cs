using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using DingPanMao.Models;

namespace DingPanMao.Services;

/// <summary>
/// 东方财富。一个批量接口覆盖 A股/港股/美股/指数/期货/外汇/债券，历史数据接口对所有品种通用。
/// </summary>
public sealed class EastMoneyProvider : IMarketDataProvider
{
    /// <summary>
    /// 实时行情走这个路径。第一个域名偶尔会连不上，第二个是同源延时域名，
    /// 数据一致（免费接口本来就有延迟），用作兜底。
    /// </summary>
    private static readonly string[] QuoteHosts =
    [
        "https://push2.eastmoney.com/api/qt/ulist.np/get",
        "https://push2delay.eastmoney.com/api/qt/ulist.np/get",
    ];

    private const string KlineApi = "https://push2his.eastmoney.com/api/qt/stock/kline/get";
    private const string SearchApi = "https://searchapi.eastmoney.com/api/suggest/get";
    private const string Ut = "fa5fd1943c7b386f172d6893dbfba10b";

    private const string QuoteFields = "f1,f2,f12,f13,f14,f15,f16,f17,f18,f124";

    private const string KlineFields = "f51,f52,f53,f54,f55,f56,f57,f58";

    private readonly HttpClient _http;

    public EastMoneyProvider(HttpClient http) => _http = http;

    public string Name => "eastmoney";

    public int Priority => 0;

    public bool Supports(SymbolDefinition symbol) => symbol.Provider == Name;

    public async Task<IReadOnlyList<Quote>> GetQuotesAsync(
        IReadOnlyList<SymbolDefinition> symbols,
        CancellationToken ct)
    {
        var targets = symbols.Where(Supports).ToList();
        if (targets.Count == 0)
        {
            return [];
        }

        var secids = string.Join(",", targets.Select(s => s.Code));
        var query = $"?secids={Uri.EscapeDataString(secids)}&fields={QuoteFields}";

        JsonDocument? document = null;
        Exception? lastError = null;
        foreach (var host in QuoteHosts)
        {
            try
            {
                document = await GetJsonAsync(host + query, ct).ConfigureAwait(false);
                break;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        if (document is null)
        {
            throw lastError ?? new InvalidOperationException("东方财富实时接口不可用");
        }

        using (document)
        {
            return ParseQuotes(document, targets);
        }
    }

    private List<Quote> ParseQuotes(JsonDocument document, List<SymbolDefinition> targets)
    {

        var result = new List<Quote>(targets.Count);
        if (document.RootElement.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("diff", out var diff)
            && diff.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in diff.EnumerateArray())
            {
                var code = ReadString(item, "f12");
                var market = ReadString(item, "f13");
                var definition = targets.FirstOrDefault(s => s.Code == $"{market}.{code}");
                if (definition is null)
                {
                    continue;
                }

                var decimals = (int)ReadNumber(item, "f1");
                var scale = Math.Pow(10, Math.Max(decimals, 0));
                var price = ReadNumber(item, "f2") / scale;
                if (price <= 0)
                {
                    continue;
                }

                var stamp = ReadNumber(item, "f124");
                result.Add(new Quote
                {
                    SymbolId = definition.Id,
                    Name = definition.NameFor(Lang.CurrentLanguage) is { Length: > 0 } localized ? localized : ReadString(item, "f14"),
                    Price = price,
                    PrevClose = ReadNumber(item, "f18") / scale,
                    Open = ReadNumber(item, "f17") / scale,
                    DayHigh = ReadNumber(item, "f15") / scale,
                    DayLow = ReadNumber(item, "f16") / scale,
                    Decimals = definition.Decimals,
                    Suffix = definition.Suffix,
                    QuoteTime = stamp > 0
                        ? DateTimeOffset.FromUnixTimeSeconds((long)stamp).LocalDateTime
                        : DateTime.Now,
                    FetchedAt = DateTime.Now,
                    Provider = Name,
                });
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<DailyBar>> GetDailyAsync(
        SymbolDefinition symbol,
        int count,
        CancellationToken ct)
    {
        var url = $"{KlineApi}?secid={Uri.EscapeDataString(symbol.Code)}&klt=101&fqt=1&lmt={count}"
            + $"&end=20500101&fields1=f1,f2,f3,f4,f5,f6&fields2={KlineFields}&ut={Ut}";
        using var document = await GetJsonAsync(url, ct).ConfigureAwait(false);

        var bars = new List<DailyBar>(count);
        foreach (var line in ReadKlines(document))
        {
            var parts = line.Split(',');
            if (parts.Length < 5
                || !DateTime.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                continue;
            }

            bars.Add(new DailyBar(
                date,
                Parse(parts[1]),
                Parse(parts[3]),
                Parse(parts[4]),
                Parse(parts[2])));
        }

        return bars;
    }

    public async Task<IReadOnlyList<Tick>> GetIntradayAsync(SymbolDefinition symbol, CancellationToken ct)
    {
        var url = $"{KlineApi}?secid={Uri.EscapeDataString(symbol.Code)}&klt=1&fqt=1&lmt=240"
            + $"&end=20500101&fields1=f1,f2,f3,f4,f5,f6&fields2=f51,f53&ut={Ut}";
        using var document = await GetJsonAsync(url, ct).ConfigureAwait(false);

        var ticks = new List<Tick>();
        foreach (var line in ReadKlines(document))
        {
            var parts = line.Split(',');
            if (parts.Length < 2
                || !DateTime.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            {
                continue;
            }

            var price = Parse(parts[1]);
            if (price > 0)
            {
                ticks.Add(new Tick(time, price));
            }
        }

        return ticks;
    }

    /// <summary>按名称搜索品种，供设置界面使用。</summary>
    public async Task<IReadOnlyList<SymbolDefinition>> SearchAsync(string keyword, CancellationToken ct)
    {
        var url = $"{SearchApi}?input={Uri.EscapeDataString(keyword)}&type=14&count=20&token=D43BF722C8E33BDC906FB84D85E326E8";
        using var document = await GetJsonAsync(url, ct).ConfigureAwait(false);

        var result = new List<SymbolDefinition>();
        if (!document.RootElement.TryGetProperty("QuotationCodeTable", out var table)
            || !table.TryGetProperty("Data", out var data)
            || data.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in data.EnumerateArray())
        {
            var quoteId = ReadString(item, "QuoteID");
            var name = ReadString(item, "Name");
            if (string.IsNullOrWhiteSpace(quoteId) || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var typeName = ReadString(item, "SecurityTypeName");
            result.Add(new SymbolDefinition
            {
                Id = $"eastmoney:{quoteId}",
                Name = name,
                Provider = Name,
                Code = quoteId,
                Decimals = typeName.Contains("债") ? 4 : 2,
                Category = Categorize(typeName),
            });
        }

        return result;
    }

    /// <summary>把东财的日线转换成一次可读的价格序列，用于大图窗口。</summary>
    private static SymbolCategory Categorize(string typeName) => typeName switch
    {
        var t when t.Contains("指数") => SymbolCategory.Index,
        var t when t.Contains("债") => SymbolCategory.Bond,
        var t when t.Contains("期货") || t.Contains("现货") => SymbolCategory.Commodity,
        var t when t.Contains("外汇") || t.Contains("汇率") => SymbolCategory.Currency,
        var t when t.Contains("股") => SymbolCategory.Equity,
        _ => SymbolCategory.Other,
    };

    private static IEnumerable<string> ReadKlines(JsonDocument document)
    {
        if (document.RootElement.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("klines", out var klines)
            && klines.ValueKind == JsonValueKind.Array)
        {
            foreach (var line in klines.EnumerateArray())
            {
                var text = line.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    yield return text;
                }
            }
        }
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct)
    {
        await using var stream = await _http.GetStreamAsync(url, ct).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
    }

    /// <summary>东财的代码/市场字段有时是数字，这里两种都接受。</summary>
    private static string ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var prop))
        {
            return string.Empty;
        }

        return prop.ValueKind switch
        {
            JsonValueKind.String => prop.GetString() ?? string.Empty,
            JsonValueKind.Number => prop.GetRawText(),
            _ => string.Empty,
        };
    }

    private static double ReadNumber(JsonElement element, string name)
        => element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number
            ? prop.GetDouble()
            : 0;

    private static double Parse(string text)
        => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;
}

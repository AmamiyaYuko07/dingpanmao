using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DingPanMao.Models;

namespace DingPanMao.Services;

/// <summary>
/// 新浪财经。作为东方财富的降级源，覆盖 A股、港股、美股、外盘期货与贵金属。
/// 取不到的品种（例如美债收益率、外汇）会在降级时被跳过。
/// </summary>
public sealed class SinaProvider : IMarketDataProvider
{
    /// <summary>新浪的行情接口强制校验 Referer，缺了会返回 403。</summary>
    private const string Referer = "https://finance.sina.com.cn/";

    private const string QuoteApi = "https://hq.sinajs.cn/list=";

    private const string FuturesDailyApi =
        "https://stock2.finance.sina.com.cn/futures/api/jsonp.php/var%20_{0}=/GlobalFuturesService.getGlobalFuturesDailyKLine?symbol={0}";

    private const string FuturesMinuteApi =
        "https://gu.sina.cn/ft/api/jsonp.php/var%20_{0}=/GlobalService.getMink?symbol={0}&type=1";

    private static readonly Encoding Gbk = CreateGbk();

    private static readonly Regex SnapshotRegex = new("hq_str_([A-Za-z0-9_]+)=\"([^\"]*)\"", RegexOptions.Compiled);

    private static readonly Regex JsonArrayRegex = new(@"\((\[.*\])\)", RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>新浪与东财代码不一致的品种需要显式映射。</summary>
    private static readonly Dictionary<string, string> CodeMap = new()
    {
        ["122.XAU"] = "hf_XAU",
        ["112.B00Y"] = "hf_OIL",
        ["101.GC00Y"] = "hf_GC",
        ["102.CL00Y"] = "hf_CL",
        ["100.NDX"] = "gb_ixic",
        ["100.DJIA"] = "gb_dji",
        ["100.SPX"] = "gb_inx",
        ["100.HSI"] = "rt_hkHSI",
        ["100.HSCEI"] = "rt_hkHSCEI",
    };

    private readonly HttpClient _http;

    public SinaProvider(HttpClient http) => _http = http;

    public string Name => "sina";

    public int Priority => 1;

    public bool Supports(SymbolDefinition symbol) => ToSinaCode(symbol) is not null;

    public async Task<IReadOnlyList<Quote>> GetQuotesAsync(
        IReadOnlyList<SymbolDefinition> symbols,
        CancellationToken ct)
    {
        var targets = symbols
            .Select(s => (Symbol: s, Code: ToSinaCode(s)))
            .Where(pair => pair.Code is not null)
            .Select(pair => (pair.Symbol, Code: pair.Code!))
            .ToList();

        if (targets.Count == 0)
        {
            return [];
        }

        var query = string.Join(",", targets.Select(t => t.Code));
        var bytes = await GetBytesAsync(QuoteApi + query, ct).ConfigureAwait(false);
        var text = Gbk.GetString(bytes);

        var result = new List<Quote>(targets.Count);
        foreach (Match match in SnapshotRegex.Matches(text))
        {
            var sinaCode = match.Groups[1].Value;
            var target = targets.FirstOrDefault(t => string.Equals(t.Code, sinaCode, StringComparison.OrdinalIgnoreCase));
            if (target.Symbol is null)
            {
                continue;
            }

            var quote = ParseSnapshot(target.Symbol, sinaCode, match.Groups[2].Value);
            if (quote is not null)
            {
                result.Add(quote);
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<DailyBar>> GetDailyAsync(
        SymbolDefinition symbol,
        int count,
        CancellationToken ct)
    {
        // 只补外盘期货/贵金属：新浪对这几个品种有稳定的日线接口。
        var code = ToSinaCode(symbol);
        if (code is null || !code.StartsWith("hf_", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var url = string.Format(CultureInfo.InvariantCulture, FuturesDailyApi, code[3..].ToUpperInvariant());
        var raw = await GetTextAsync(url, ct).ConfigureAwait(false);
        var match = JsonArrayRegex.Match(raw);
        if (!match.Success)
        {
            return [];
        }

        using var document = JsonDocument.Parse(match.Groups[1].Value);
        var bars = new List<DailyBar>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var dateText = ReadString(item, "date");
            if (!DateTime.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                continue;
            }

            var bar = new DailyBar(date, ReadNumber(item, "open"), ReadNumber(item, "high"), ReadNumber(item, "low"), ReadNumber(item, "close"));
            if (bar.High > 0 && bar.Low > 0)
            {
                bars.Add(bar);
            }
        }

        return bars.Count > count ? bars.Skip(bars.Count - count).ToList() : bars;
    }

    public async Task<IReadOnlyList<Tick>> GetIntradayAsync(SymbolDefinition symbol, CancellationToken ct)
    {
        var code = ToSinaCode(symbol);
        if (code is null || !code.StartsWith("hf_", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var url = string.Format(CultureInfo.InvariantCulture, FuturesMinuteApi, code[3..].ToUpperInvariant());
        var raw = await GetTextAsync(url, ct).ConfigureAwait(false);
        var match = JsonArrayRegex.Match(raw);
        if (!match.Success)
        {
            return [];
        }

        using var document = JsonDocument.Parse(match.Groups[1].Value);
        var ticks = new List<Tick>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var timeText = ReadString(item, "d");
            if (!DateTime.TryParse(timeText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            {
                continue;
            }

            var price = ReadNumber(item, "c");
            if (price > 0)
            {
                ticks.Add(new Tick(time, price));
            }
        }

        return ticks;
    }

    /// <summary>把东财代码翻译成新浪代码，翻译不了说明该源不支持这个品种。</summary>
    internal static string? ToSinaCode(SymbolDefinition symbol)
    {
        var code = symbol.Code;

        // 沪市 / 深市
        if (code.StartsWith("1.", StringComparison.Ordinal) && code.Length == 8)
        {
            return "sh" + code[2..];
        }

        if (code.StartsWith("0.", StringComparison.Ordinal) && code.Length == 8)
        {
            return "sz" + code[2..];
        }

        // 港股
        if (code.StartsWith("116.", StringComparison.Ordinal) && code.Length == 9)
        {
            return "rt_hk" + code[4..];
        }

        // 美股
        if ((code.StartsWith("105.", StringComparison.Ordinal)
                || code.StartsWith("106.", StringComparison.Ordinal)
                || code.StartsWith("107.", StringComparison.Ordinal))
            && code.Length > 4)
        {
            return "gb_" + code[4..].ToLowerInvariant();
        }

        return CodeMap.TryGetValue(code, out var mapped) ? mapped : null;
    }

    private static Quote? ParseSnapshot(SymbolDefinition symbol, string sinaCode, string body)
    {
        var fields = body.Split(',');
        if (fields.Length < 6)
        {
            return null;
        }

        double price, prevClose, open, high, low;
        string name;
        DateTime quoteTime;

        if (sinaCode.StartsWith("hf_", StringComparison.OrdinalIgnoreCase))
        {
            // 外盘期货：现价,?,买,卖,最高,最低,时间,昨收,开盘,...
            if (fields.Length < 9)
            {
                return null;
            }

            price = Parse(fields[0]);
            high = Parse(fields[4]);
            low = Parse(fields[5]);
            prevClose = Parse(fields[7]);
            open = Parse(fields[8]);
            name = symbol.Name;
            quoteTime = ParseStamp(fields[12], fields[6]);
        }
        else if (sinaCode.StartsWith("rt_hk", StringComparison.OrdinalIgnoreCase))
        {
            // 港股：英文名,中文名,今开,昨收,最高,最低,现价,...
            if (fields.Length < 7)
            {
                return null;
            }

            price = Parse(fields[6]);
            prevClose = Parse(fields[3]);
            open = Parse(fields[2]);
            high = Parse(fields[4]);
            low = Parse(fields[5]);
            name = string.IsNullOrWhiteSpace(fields[1]) ? symbol.Name : fields[1];
            quoteTime = ParseStamp(fields[17], fields[18]);
        }
        else if (sinaCode.StartsWith("gb_", StringComparison.OrdinalIgnoreCase))
        {
            // 美股/美股指数：名称,现价,涨跌幅,时间,涨跌额,开盘,最高,最低,...
            if (fields.Length < 8)
            {
                return null;
            }

            price = Parse(fields[1]);
            prevClose = price - Parse(fields[4]);
            open = Parse(fields[5]);
            high = Parse(fields[6]);
            low = Parse(fields[7]);
            name = string.IsNullOrWhiteSpace(fields[0]) ? symbol.Name : fields[0];
            quoteTime = DateTime.TryParse(fields[3], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
                ? t
                : DateTime.Now;
        }
        else if (sinaCode.StartsWith("s_", StringComparison.OrdinalIgnoreCase))
        {
            // 简版指数：名称,当前点数,涨跌额,涨跌幅,成交量,成交额
            if (fields.Length < 4)
            {
                return null;
            }

            price = Parse(fields[1]);
            prevClose = price - Parse(fields[2]);
            open = 0;
            high = 0;
            low = 0;
            name = symbol.Name;
            quoteTime = DateTime.Now;
        }
        else
        {
            // A股：名称,今开,昨收,现价,最高,最低,...,日期,时间
            if (fields.Length < 32)
            {
                return null;
            }

            price = Parse(fields[3]);
            prevClose = Parse(fields[2]);
            open = Parse(fields[1]);
            high = Parse(fields[4]);
            low = Parse(fields[5]);
            name = string.IsNullOrWhiteSpace(fields[0]) ? symbol.Name : fields[0];
            quoteTime = ParseStamp(fields[30], fields[31]);
        }

        if (price <= 0)
        {
            return null;
        }

        return new Quote
        {
            SymbolId = symbol.Id,
            Name = name,
            Price = price,
            PrevClose = prevClose,
            Open = open,
            DayHigh = high,
            DayLow = low,
            Decimals = symbol.Decimals,
            Suffix = symbol.Suffix,
            QuoteTime = quoteTime,
            FetchedAt = DateTime.Now,
            Provider = "sina",
        };
    }

    private static Encoding CreateGbk()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding("GB18030");
    }

    /// <summary>带 Referer 的 GET，新浪接口缺这个头会 403。</summary>
    private async Task<byte[]> GetBytesAsync(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Referrer = new Uri(Referer);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }

    /// <summary>带 Referer 的 GET（返回文本）。</summary>
    private async Task<string> GetTextAsync(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Referrer = new Uri(Referer);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    private static DateTime ParseStamp(string dateText, string timeText)
        => DateTime.TryParseExact(
            $"{dateText} {timeText}",
            ["yyyy-MM-dd HH:mm:ss", "yyyy/MM/dd HH:mm:ss"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var stamp)
            ? stamp
            : DateTime.Now;

    private static string ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString() ?? string.Empty
            : string.Empty;

    private static double ReadNumber(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var prop))
        {
            return 0;
        }

        return prop.ValueKind switch
        {
            JsonValueKind.Number => prop.GetDouble(),
            JsonValueKind.String => Parse(prop.GetString()),
            _ => 0,
        };
    }

    private static double Parse(string? text)
        => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;
}

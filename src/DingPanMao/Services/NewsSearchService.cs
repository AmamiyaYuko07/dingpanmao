using System.Net.Http;
using System.Text.Json;
using DingPanMao.Models;

namespace DingPanMao.Services;

/// <summary>一条资讯。</summary>
public readonly record struct NewsItem(DateTime Time, string Title, string Source, string Summary, string Url)
{
    public override string ToString()
        => $"{Time:yyyy-MM-dd HH:mm}  [{Source}]  {Title}";
}

/// <summary>
/// 财经资讯检索，数据来自东方财富的搜索接口。免费、不需要额外密钥，
/// 覆盖面是主流财经媒体，比通用搜索更贴合行情分析场景。
/// </summary>
public sealed class NewsSearchService : IDisposable
{
    private const string Endpoint = "https://search-api-web.eastmoney.com/search/jsonp";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public NewsSearchService()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
        _http.DefaultRequestHeaders.Referrer = new Uri("https://so.eastmoney.com/");
    }

    /// <summary>按关键词搜索最近的资讯。</summary>
    public async Task<IReadOnlyList<NewsItem>> SearchAsync(
        string keyword,
        int count = 10,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return [];
        }

        var param = JsonSerializer.Serialize(new
        {
            uid = string.Empty,
            keyword,
            type = new[] { "cmsArticleWebOld" },
            client = "web",
            clientType = "web",
            clientVersion = "curr",
            param = new
            {
                cmsArticleWebOld = new
                {
                    searchScope = "default",
                    sort = "default",
                    pageIndex = 1,
                    pageSize = Math.Clamp(count, 1, 30),
                    preTag = string.Empty,
                    postTag = string.Empty,
                },
            },
        });

        var url = $"{Endpoint}?cb=cb&param={Uri.EscapeDataString(param)}";
        var raw = await _http.GetStringAsync(url, ct).ConfigureAwait(false);

        // 返回的是 JSONP：cb({...});
        var start = raw.IndexOf('(');
        var end = raw.LastIndexOf(')');
        if (start < 0 || end <= start)
        {
            return [];
        }

        using var doc = JsonDocument.Parse(raw[(start + 1)..end]);
        if (!doc.RootElement.TryGetProperty("result", out var result)
            || !result.TryGetProperty("cmsArticleWebOld", out var list)
            || list.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var items = new List<NewsItem>();
        foreach (var element in list.EnumerateArray())
        {
            var title = Read(element, "title");
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            DateTime.TryParse(Read(element, "date"), out var time);
            items.Add(new NewsItem(
                time,
                title,
                Read(element, "mediaName"),
                StripHtml(Read(element, "content")),
                Read(element, "url")));
        }

        return items;
    }

    private static string Read(JsonElement element, string name)
        => element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString() ?? string.Empty
            : string.Empty;

    private static string StripHtml(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var cleaned = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", string.Empty);
        return cleaned.Length <= 200 ? cleaned : cleaned[..200] + "…";
    }

    public void Dispose() => _http.Dispose();
}

using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DingPanMao.Config;
using DingPanMao.Models;

namespace DingPanMao.Services;

/// <summary>对话中的一条消息。</summary>
public readonly record struct AiMessage(string Role, string Content);

/// <summary>
/// 调用兼容 OpenAI Responses API 的服务（默认 DeepSeek）做行情分析。
/// 请求体走 /responses，stream=true，逐条解析 SSE 事件。
/// </summary>
public sealed class AiAnalysisService : IDisposable
{
    public const string DefaultAnalysisQuestionKey = "ai.defaultQuestion";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };

    private readonly NewsSearchService _news = new();

    /// <summary>
    /// 多轮对话。每收到一段文本就回调一次 <paramref name="onDelta"/>，返回完整回复。
    /// 行情上下文通过 instructions 每轮附带，保证模型始终看到最新数据。
    /// </summary>
    public async Task<string> ChatAsync(
        AppSettings settings,
        string instructions,
        IReadOnlyList<AiMessage> messages,
        Action<string> onDelta,
        Action<string>? onReasoning = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(settings.AiApiKey))
        {
            throw new InvalidOperationException(Lang.T("ai.noKey"));
        }

        var payload = new Dictionary<string, object?>
        {
            ["model"] = string.IsNullOrWhiteSpace(settings.AiModel) ? "deepseek-v4-flash" : settings.AiModel.Trim(),
            ["instructions"] = instructions,
            ["input"] = messages
                .Select(m => new Dictionary<string, string> { ["role"] = m.Role, ["content"] = m.Content })
                .ToArray(),
            ["stream"] = true,
        };

        // OpenAI 支持服务端联网搜索；DeepSeek 只认 function，其他类型会被忽略，
        // 这种情况下改用本地资讯检索把资料塞进上下文。
        if (settings.AiWebSearch && IsOpenAiEndpoint(settings.AiBaseUrl))
        {
            payload["tools"] = new[] { new Dictionary<string, string> { ["type"] = "web_search" } };
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildEndpoint(settings.AiBaseUrl));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.AiApiKey.Trim());
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}\n{Trim(body)}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var builder = new StringBuilder();
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            if (line.Length == 0 || !line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var data = line[5..].Trim();
            if (data.Length == 0 || data == "[DONE]")
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(data);
                var root = doc.RootElement;
                var type = root.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;

                switch (type)
                {
                    // 模型的思考过程（部分模型会返回）
                    case "response.reasoning_text.delta":
                    case "response.reasoning_summary_text.delta":
                        if (onReasoning is not null && root.TryGetProperty("delta", out var reasoning))
                        {
                            onReasoning(reasoning.GetString() ?? string.Empty);
                        }

                        break;

                    case "response.output_text.delta":
                        if (root.TryGetProperty("delta", out var delta))
                        {
                            var text = delta.GetString() ?? string.Empty;
                            builder.Append(text);
                            onDelta(text);
                        }

                        break;

                    case "response.failed":
                    case "error":
                        throw new InvalidOperationException(ExtractError(root));

                    case "response.completed":
                    case "response.incomplete":
                        return builder.ToString();
                }
            }
            catch (JsonException)
            {
                // 忽略解析不了的行，继续读后面的。
            }
        }

        return builder.ToString();
    }

    /// <summary>构造随每轮对话一起发送的行情上下文，必要时附带最新财经资讯。</summary>
    public async Task<string> BuildContextAsync(
        AppSettings settings,
        SymbolDefinition symbol,
        Quote? quote,
        IReadOnlyList<DailyBar> bars,
        IReadOnlyList<Tick> ticks,
        CancellationToken ct = default)
    {
        var d = symbol.Decimals;
        var suffix = symbol.Suffix;
        var sb = new StringBuilder();

        sb.AppendLine("你是一名严谨的市场分析师，服务于一个桌面行情工具。");
        sb.AppendLine("只根据下面提供的数据判断，不要编造未提供的数据；数字要精确，输出使用简体中文。");
        sb.AppendLine();
        sb.AppendLine("【必须遵守的输出要求】");
        sb.AppendLine("1. 回答开头先给出明确的操作建议，只能从这几个词里选一个：强烈买入 / 买入 / 增持 / 持有观望 / 减仓 / 清仓。");
        sb.AppendLine("2. 接着给出：建议仓位（用百分比区间）、核心依据、关键价位（支撑、压力、触发买入或卖出的价位、止损参考位）。");
        sb.AppendLine("3. 如果现有数据不足以支撑判断，就直接写「数据不足，建议观望」，不要含糊其辞。");
        sb.AppendLine("4. 用 Markdown 组织内容，结构清晰；不要重复罗列上面已经给出的原始行情数据。");
        sb.AppendLine("5. 结尾用一句话说明这是基于行情数据的程序化判断，仅供参考。");
        sb.AppendLine();
        sb.AppendLine($"【当前查看的品种】{symbol.NameFor(Lang.CurrentLanguage)}（{symbol.Code}）");

        if (quote is not null)
        {
            sb.AppendLine($"【现价】{quote.Price.ToString($"F{d}")}{suffix}   {quote.FormatChangePercent()}");
            sb.AppendLine(
                $"【今日】昨收 {quote.PrevClose.ToString($"F{d}")}   开 {quote.Open.ToString($"F{d}")}   "
                + $"高 {quote.DayHigh.ToString($"F{d}")}   低 {quote.DayLow.ToString($"F{d}")}");
        }
        else
        {
            sb.AppendLine("【现价】暂未取到");
        }

        if (bars.Count > 0)
        {
            var recent = bars.Skip(Math.Max(0, bars.Count - 30)).ToList();
            sb.AppendLine();
            sb.AppendLine($"【最近 {recent.Count} 个交易日】格式：日期,开,高,低,收");
            foreach (var bar in recent)
            {
                sb.AppendLine(string.Join(
                    ",",
                    bar.Date.ToString("yyyy-MM-dd"),
                    bar.Open.ToString($"F{d}"),
                    bar.High.ToString($"F{d}"),
                    bar.Low.ToString($"F{d}"),
                    bar.Close.ToString($"F{d}")));
            }
        }

        if (ticks.Count > 1)
        {
            var sampled = ticks.Where((_, i) => i % 5 == 0 || i == ticks.Count - 1).ToList();
            sb.AppendLine();
            sb.AppendLine("【今日分时】格式：时间,价格（每 5 分钟采样）");
            foreach (var tick in sampled)
            {
                sb.AppendLine($"{tick.Time:HH:mm},{tick.Price.ToString($"F{d}")}");
            }
        }

        // 非 OpenAI 端点下用资讯检索补足「联网」能力
        if (settings.AiWebSearch && !IsOpenAiEndpoint(settings.AiBaseUrl))
        {
            try
            {
                var news = await _news.SearchAsync(symbol.KeywordForSearch(), 10, ct).ConfigureAwait(false);
                if (news.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("【最新财经资讯】以下是从财经媒体检索到的相关报道（时间 来源 标题），回答时可以引用，并注明来源与时间：");
                    foreach (var item in news)
                    {
                        sb.AppendLine($"- {item}");
                    }
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine();
                sb.AppendLine($"（资讯检索失败：{ex.Message}）");
            }
        }

        var custom = (settings.AiCustomPrompt ?? string.Empty).Trim();
        if (custom.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("【用户的额外要求】" + custom);
        }

        return sb.ToString();
    }

    private static bool IsOpenAiEndpoint(string baseUrl)
        => !string.IsNullOrWhiteSpace(baseUrl)
            && baseUrl.Contains("openai.com", StringComparison.OrdinalIgnoreCase);

    private static string BuildEndpoint(string baseUrl)
    {
        var trimmed = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (trimmed.Length == 0)
        {
            trimmed = "https://api.deepseek.com";
        }

        return trimmed.EndsWith("/responses", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : trimmed + "/responses";
    }

    private static string ExtractError(JsonElement root)
    {
        if (root.TryGetProperty("error", out var error))
        {
            if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var message))
            {
                return message.GetString() ?? error.ToString();
            }

            return error.ToString();
        }

        return root.TryGetProperty("message", out var direct)
            ? direct.GetString() ?? root.ToString()
            : root.ToString();
    }

    private static string Trim(string text)
        => text.Length <= 500 ? text : text[..500] + "…";

    public void Dispose()
    {
        _news.Dispose();
        _http.Dispose();
    }
}

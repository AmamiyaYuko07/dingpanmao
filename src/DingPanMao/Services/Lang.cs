using System.Reflection;
using System.Text.Json;

namespace DingPanMao.Services;

public sealed record LanguageInfo(string Code, string NativeName);

/// <summary>界面文案的多语言支持。语言包以 JSON 形式嵌入在程序里，切换后立即生效。</summary>
public static class Lang
{
    private const string DefaultLanguage = "zh-CN";

    private static readonly Dictionary<string, Dictionary<string, string>> Catalog = Load();

    private static string _current = DefaultLanguage;

    public static event Action? LanguageChanged;

    public static string CurrentLanguage => _current;

    public static IReadOnlyList<LanguageInfo> Languages { get; } = BuildLanguageList();

    public static void SetLanguage(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || !Catalog.ContainsKey(code) || code == _current)
        {
            return;
        }

        _current = code;
        LanguageChanged?.Invoke();
    }

    /// <summary>取文案。找不到就回退到简体中文，再找不到就返回键名本身。</summary>
    public static string T(string key)
    {
        if (Catalog.TryGetValue(_current, out var table) && table.TryGetValue(key, out var text))
        {
            return text;
        }

        if (Catalog.TryGetValue(DefaultLanguage, out var fallback) && fallback.TryGetValue(key, out var text2))
        {
            return text2;
        }

        return key;
    }

    public static string T(string key, params object[] args)
    {
        var template = T(key);
        try
        {
            return string.Format(template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }

    private static IReadOnlyList<LanguageInfo> BuildLanguageList()
    {
        var result = new List<LanguageInfo>();
        foreach (var code in Catalog.Keys)
        {
            var native = Catalog[code].TryGetValue("language.name", out var name) ? name : code;
            result.Add(new LanguageInfo(code, native));
        }

        return result;
    }

    private static Dictionary<string, Dictionary<string, string>> Load()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var name = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("Strings.json", StringComparison.OrdinalIgnoreCase));
            if (name is null)
            {
                return [];
            }

            using var stream = assembly.GetManifestResourceStream(name);
            if (stream is null)
            {
                return [];
            }

            var parsed = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(stream);
            return parsed ?? [];
        }
        catch
        {
            return [];
        }
    }
}

using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DingPanMao.Models;

namespace DingPanMao.Config;

/// <summary>一个格子的配置。</summary>
public sealed class SlotSettings
{
    /// <summary>品种定义，随配置一起保存，因此自定义品种也能在下次启动时还原。</summary>
    public SymbolDefinition Symbol { get; set; } = new() { Id = string.Empty, Name = string.Empty };

    public TileSize Size { get; set; } = TileSize.Large;
}

/// <summary>需要持久化的用户设置。</summary>
public sealed class AppSettings
{
    public List<SlotSettings> Slots { get; set; } = [];

    public string Language { get; set; } = "zh-CN";

    /// <summary>true 表示红涨绿跌。</summary>
    public bool RedUpGreenDown { get; set; } = true;

    /// <summary>长条背景不透明度，0.2 ~ 1.0。</summary>
    public double Opacity { get; set; } = 0.55;

    /// <summary>字号缩放，0.8 ~ 1.4。</summary>
    public double FontScale { get; set; } = 1.0;

    public bool AutoStart { get; set; }

    /// <summary>点击格子打开大图。</summary>
    public bool OpenDetailOnClick { get; set; } = true;

    public int RefreshSeconds { get; set; } = 10;

    public bool ReversalAlerts { get; set; } = true;

    public bool BreakoutAlerts { get; set; } = true;

    public bool LevelAlerts { get; set; } = true;

    /// <summary>提醒灵敏度，越大越不敏感，0.5 ~ 2.0。</summary>
    public double Sensitivity { get; set; } = 1.0;

    /// <summary>任务栏长条左边缘的位置（DIP）。为空表示使用默认位置。</summary>
    public double? BarLeft { get; set; }

    /// <summary>上次运行留下的提醒，用于启动时提示最近一次情况。</summary>
    public List<AlertEvent>? RecentAlerts { get; set; }

    [JsonIgnore]
    public int SlotCount => Math.Clamp(Slots.Count, AppConfig.MinSlots, AppConfig.MaxSlots);
}

/// <summary>设置的读写。</summary>
public static class SettingsStore
{
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppConfig.SettingsFolder);

    private static readonly string FilePath = Path.Combine(Folder, "settings.json");

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
                if (settings is not null)
                {
                    Normalize(settings);
                    return settings;
                }
            }
        }
        catch
        {
            // 配置损坏时退回默认值。
        }

        return CreateDefault();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, WriteOptions));
        }
        catch
        {
            // 写不进去不影响行情显示。
        }
    }

    public static AppSettings CreateDefault()
    {
        var settings = new AppSettings();
        foreach (var symbol in Services.SymbolCatalog.Defaults)
        {
            settings.Slots.Add(new SlotSettings { Symbol = symbol, Size = TileSize.Large });
        }

        return settings;
    }

    private static void Normalize(AppSettings settings)
    {
        if (settings.Slots.Count == 0)
        {
            settings.Slots = CreateDefault().Slots;
        }

        settings.Slots = settings.Slots
            .Where(s => !string.IsNullOrWhiteSpace(s.Symbol?.Id))
            .Take(AppConfig.MaxSlots)
            .ToList();

        if (settings.Slots.Count == 0)
        {
            settings.Slots = CreateDefault().Slots;
        }

        settings.RefreshSeconds = Math.Clamp(settings.RefreshSeconds, 3, 120);
        settings.Opacity = Math.Clamp(settings.Opacity, 0.2, 1.0);
        settings.FontScale = Math.Clamp(settings.FontScale, 0.8, 1.4);
        settings.Sensitivity = Math.Clamp(settings.Sensitivity, 0.5, 2.0);
    }
}

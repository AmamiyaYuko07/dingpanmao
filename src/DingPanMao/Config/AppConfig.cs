namespace DingPanMao.Config;

/// <summary>格子尺寸。宽度不同，显示的内容也不同。</summary>
public enum TileSize
{
    /// <summary>只显示名称和涨跌百分比。</summary>
    Small,

    /// <summary>名称、涨跌百分比和走势图。</summary>
    Medium,

    /// <summary>名称、现价、涨跌百分比和走势图。</summary>
    Large,
}

/// <summary>全局常量与默认值。</summary>
public static class AppConfig
{
    public const int MaxSlots = 4;

    public const int MinSlots = 1;

    /// <summary>走势图实际显示最近多少个数据点。</summary>
    public const int ChartPoints = 60;

    /// <summary>内存里保留的分时点数上限。</summary>
    public const int MaxSeriesPoints = 4000;

    /// <summary>应用显示名。</summary>
    public const string DisplayName = "盯盘猫";

    /// <summary>注册表与配置目录用的名字，保持 ASCII 避免路径问题。</summary>
    public const string ProductName = "DingPanMao";

    public const string SettingsFolder = "DingPanMao";

    /// <summary>各尺寸的格子宽度（DIP）。</summary>
    public static double WidthFor(TileSize size) => size switch
    {
        TileSize.Small => 100,
        TileSize.Medium => 122,
        _ => 156,
    };
}

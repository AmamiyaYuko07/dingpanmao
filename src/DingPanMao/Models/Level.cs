namespace DingPanMao.Models;

/// <summary>关键位的类型：支撑或压力。</summary>
public enum LevelKind
{
    Support,
    Resistance,
}

/// <summary>关键位的来源，用于计算权重。</summary>
public enum LevelSource
{
    /// <summary>上一交易日高低收推导的枢轴点。</summary>
    Pivot,

    /// <summary>近期摆动高低点。</summary>
    Swing,

    /// <summary>整数关口。</summary>
    Round,
}

/// <summary>一个支撑/压力位。</summary>
public sealed record Level(double Price, LevelKind Kind, LevelSource Source, double Weight, string Note);

/// <summary>枢轴点集合。</summary>
public sealed record PivotSet(double PP, double R1, double R2, double R3, double S1, double S2, double S3);

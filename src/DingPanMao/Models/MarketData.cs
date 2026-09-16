namespace DingPanMao.Models;

/// <summary>日 K。</summary>
public readonly record struct DailyBar(DateTime Date, double Open, double High, double Low, double Close);

/// <summary>分时点。</summary>
public readonly record struct Tick(DateTime Time, double Price);

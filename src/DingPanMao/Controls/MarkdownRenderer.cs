using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace DingPanMao.Controls;

/// <summary>
/// 轻量 Markdown 渲染：把模型返回的文本转成 FlowDocument 的 Block。
/// 支持标题、粗体、斜体、行内代码、无序/有序列表、代码块、引用、分隔线和管道表格。
/// </summary>
public static partial class MarkdownRenderer
{
    private static readonly Brush BodyBrush = new SolidColorBrush(Color.FromRgb(0xD6, 0xDE, 0xEA));

    private static readonly Brush MutedBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0xA5, 0xB4));

    private static readonly Brush CodeBrush = new SolidColorBrush(Color.FromRgb(0x8F, 0xE3, 0xA8));

    private static readonly Brush CodeBackground = new SolidColorBrush(Color.FromRgb(0x1A, 0x1D, 0x24));

    private static readonly Brush RuleBrush = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF));

    private static readonly Brush QuoteBackground = new SolidColorBrush(Color.FromRgb(0x26, 0x2A, 0x33));

    private static readonly FontFamily Mono = new("Consolas, Microsoft YaHei UI");

    /// <summary>把一段 Markdown 追加到目标容器（FlowDocument 或 Section 都行）。</summary>
    public static void Append(BlockCollection document, string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return;
        }

        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var i = 0;

        while (i < lines.Length)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            // 代码块
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                i++;
                var code = new List<string>();
                while (i < lines.Length && !lines[i].Trim().StartsWith("```", StringComparison.Ordinal))
                {
                    code.Add(lines[i]);
                    i++;
                }

                i++;
                document.Add(BuildCodeBlock(string.Join(Environment.NewLine, code)));
                continue;
            }

            // 分隔线
            if (trimmed is "---" or "***" or "___")
            {
                document.Add(new Paragraph
                {
                    Margin = new Thickness(0, 6, 0, 6),
                    BorderBrush = RuleBrush,
                    BorderThickness = new Thickness(0, 0, 0, 1),
                });
                i++;
                continue;
            }

            // 表格
            if (trimmed.StartsWith('|') && i + 1 < lines.Length && IsTableSeparator(lines[i + 1]))
            {
                var rows = new List<string[]>();
                while (i < lines.Length && lines[i].Trim().StartsWith('|'))
                {
                    var cells = SplitRow(lines[i]);
                    if (!IsTableSeparator(lines[i]))
                    {
                        rows.Add(cells);
                    }

                    i++;
                }

                if (rows.Count > 0)
                {
                    document.Add(BuildTable(rows));
                }

                continue;
            }

            // 标题
            var heading = HeadingRegex().Match(trimmed);
            if (heading.Success)
            {
                var level = heading.Groups[1].Value.Length;
                document.Add(BuildHeading(heading.Groups[2].Value, level));
                i++;
                continue;
            }

            // 引用
            if (trimmed.StartsWith('>'))
            {
                var quote = new List<string>();
                while (i < lines.Length && lines[i].Trim().StartsWith('>'))
                {
                    quote.Add(lines[i].Trim().TrimStart('>').Trim());
                    i++;
                }

                document.Add(BuildQuote(string.Join(" ", quote)));
                continue;
            }

            // 列表
            if (ListRegex().IsMatch(line))
            {
                var items = new List<(string Text, int Level, string Marker)>();
                while (i < lines.Length && ListRegex().IsMatch(lines[i]))
                {
                    var match = ListRegex().Match(lines[i]);
                    var level = match.Groups[1].Value.Replace("\t", "    ").Length / 2;
                    var bullet = match.Groups[2].Value;
                    items.Add((match.Groups[3].Value, level, bullet));
                    i++;
                }

                foreach (var (text, level, marker) in items)
                {
                    document.Add(BuildListItem(text, level, marker));
                }

                continue;
            }

            // 普通段落
            if (trimmed.Length > 0)
            {
                var paragraph = new List<string>();
                while (i < lines.Length)
                {
                    var current = lines[i].Trim();
                    if (current.Length == 0
                        || current.StartsWith("```", StringComparison.Ordinal)
                        || current.StartsWith('|')
                        || current.StartsWith('>')
                        || HeadingRegex().IsMatch(current)
                        || ListRegex().IsMatch(lines[i])
                        || current is "---" or "***" or "___")
                    {
                        break;
                    }

                    paragraph.Add(current);
                    i++;
                }

                document.Add(BuildParagraph(string.Join(" ", paragraph)));
                continue;
            }

            i++;
        }
    }

    private static Paragraph BuildHeading(string text, int level)
    {
        var paragraph = new Paragraph
        {
            Margin = new Thickness(0, level <= 2 ? 12 : 8, 0, 6),
            FontSize = level switch
            {
                1 => 16,
                2 => 14,
                3 => 13,
                _ => 12.5,
            },
            FontWeight = FontWeights.SemiBold,
            Foreground = BodyBrush,
        };
        AppendInline(paragraph, text);
        return paragraph;
    }

    private static Paragraph BuildParagraph(string text)
    {
        var paragraph = new Paragraph
        {
            Margin = new Thickness(0, 0, 0, 8),
            LineHeight = 20,
            Foreground = BodyBrush,
        };
        AppendInline(paragraph, text);
        return paragraph;
    }

    private static Paragraph BuildListItem(string text, int level, string marker)
    {
        var paragraph = new Paragraph
        {
            Margin = new Thickness(10 + (level * 14), 0, 0, 5),
            LineHeight = 20,
            Foreground = BodyBrush,
        };

        // 有序列表保留原编号，无序列表统一用圆点
        var bullet = marker.EndsWith('.') || marker.EndsWith(')') ? marker + " " : "•  ";
        paragraph.Inlines.Add(new Run(bullet) { Foreground = MutedBrush });
        AppendInline(paragraph, text);
        return paragraph;
    }

    private static Paragraph BuildQuote(string text)
    {
        var paragraph = new Paragraph
        {
            Margin = new Thickness(0, 4, 0, 8),
            Padding = new Thickness(10, 6, 10, 6),
            Background = QuoteBackground,
            BorderBrush = RuleBrush,
            BorderThickness = new Thickness(3, 0, 0, 0),
            LineHeight = 20,
            Foreground = MutedBrush,
        };
        AppendInline(paragraph, text);
        return paragraph;
    }

    private static Paragraph BuildCodeBlock(string code) => new(new Run(code))
    {
        FontFamily = Mono,
        FontSize = 11.5,
        Foreground = CodeBrush,
        Background = CodeBackground,
        Padding = new Thickness(10, 8, 10, 8),
        Margin = new Thickness(0, 4, 0, 8),
        LineHeight = 18,
    };

    private static Table BuildTable(List<string[]> rows)
    {
        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 4, 0, 10) };
        var columns = rows.Max(r => r.Length);
        for (var c = 0; c < columns; c++)
        {
            table.Columns.Add(new TableColumn());
        }

        var group = new TableRowGroup();
        table.RowGroups.Add(group);

        for (var r = 0; r < rows.Count; r++)
        {
            var row = new TableRow { Background = r == 0 ? QuoteBackground : Brushes.Transparent };
            for (var c = 0; c < columns; c++)
            {
                var cell = new TableCell(new Paragraph
                {
                    Margin = new Thickness(0),
                    Foreground = r == 0 ? BodyBrush : MutedBrush,
                    FontWeight = r == 0 ? FontWeights.SemiBold : FontWeights.Normal,
                })
                {
                    Padding = new Thickness(8, 4, 8, 4),
                    BorderBrush = RuleBrush,
                    BorderThickness = new Thickness(0, 0, 0, 1),
                };

                var paragraph = (Paragraph)cell.Blocks.FirstBlock!;
                AppendInline(paragraph, c < rows[r].Length ? rows[r][c] : string.Empty);
                row.Cells.Add(cell);
            }

            group.Rows.Add(row);
        }

        return table;
    }

    /// <summary>解析行内的粗体、斜体、行内代码和链接。</summary>
    private static void AppendInline(Paragraph paragraph, string text)
    {
        var index = 0;
        foreach (Match match in InlineRegex().Matches(text))
        {
            if (match.Index > index)
            {
                paragraph.Inlines.Add(new Run(text[index..match.Index]));
            }

            var value = match.Groups["bold"].Success
                ? match.Groups["bold"].Value
                : match.Groups["italic"].Success
                    ? match.Groups["italic"].Value
                    : match.Groups["code"].Value;

            if (match.Groups["bold"].Success)
            {
                paragraph.Inlines.Add(new Bold(new Run(value)));
            }
            else if (match.Groups["italic"].Success)
            {
                paragraph.Inlines.Add(new Italic(new Run(value)));
            }
            else
            {
                paragraph.Inlines.Add(new Run(value) { FontFamily = Mono, Foreground = CodeBrush });
            }

            index = match.Index + match.Length;
        }

        if (index < text.Length)
        {
            paragraph.Inlines.Add(new Run(text[index..]));
        }
    }

    private static string[] SplitRow(string line)
        => line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray();

    private static bool IsTableSeparator(string line)
    {
        var trimmed = line.Trim();
        return trimmed.StartsWith('|')
            && trimmed.Replace("|", string.Empty).Replace("-", string.Empty).Replace(":", string.Empty).Trim().Length == 0;
    }

    [GeneratedRegex(@"^(#{1,6})\s+(.*)$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^(\s*)([-*+]|\d+[.、)])\s+(.*)$")]
    private static partial Regex ListRegex();

    [GeneratedRegex(@"\*\*(?<bold>[^*]+)\*\*|(?<!\*)\*(?<italic>[^*]+)\*|`(?<code>[^`]+)`")]
    private static partial Regex InlineRegex();
}

using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using EmojiWpf = Emoji.Wpf;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;
using Image = System.Windows.Controls.Image;

namespace ffnotev2.Services;

/// <summary>
/// 텍스트 노트의 raw 마크다운을 FlowDocument로 변환한다.
/// 지원: H1~H6, 불릿/번호 리스트, 코드 블록, 인라인 코드, 강조, 링크, 이미지(`![]()`).
/// 컬러 이모지는 Emoji.Wpf.EmojiInline으로 처리(Twemoji 이미지 fallback).
/// 마크다운 문법이 없는 평문도 그대로 표시되도록 soft line break를 줄바꿈으로 변환.
/// </summary>
public static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAutoLinks()
        .Build();

    public static FlowDocument Render(string markdown, string fontFamily, double fontSize, string imageBaseDir)
    {
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily(fontFamily),
            FontSize = fontSize,
            Foreground = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE)),
            Background = Brushes.Transparent,
            PagePadding = new Thickness(6),
        };

        if (string.IsNullOrEmpty(markdown)) return doc;

        var parsed = Markdown.Parse(markdown, Pipeline);
        foreach (var block in parsed)
        {
            var converted = ConvertBlock(block, fontSize, imageBaseDir);
            if (converted is not null) doc.Blocks.Add(converted);
        }
        return doc;
    }

    private static System.Windows.Documents.Block? ConvertBlock(Markdig.Syntax.Block block, double baseFontSize, string imageBaseDir)
    {
        switch (block)
        {
            case HeadingBlock h:
                return BuildHeading(h, baseFontSize, imageBaseDir);
            case ListBlock list:
                return BuildList(list, baseFontSize, imageBaseDir);
            case FencedCodeBlock fcb:
                return BuildCodeBlock(fcb.Lines.ToString());
            case CodeBlock cb:
                return BuildCodeBlock(cb.Lines.ToString());
            case ParagraphBlock p:
                return BuildParagraph(p.Inline, baseFontSize, imageBaseDir);
            case QuoteBlock q:
                return BuildQuote(q, baseFontSize, imageBaseDir);
            case ThematicBreakBlock:
                return new Paragraph(new Run(new string('─', 40)))
                {
                    Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55))
                };
            default:
                return null;
        }
    }

    private static Paragraph BuildHeading(HeadingBlock h, double baseFontSize, string imageBaseDir)
    {
        var p = new Paragraph
        {
            FontWeight = FontWeights.Bold,
            FontSize = h.Level switch
            {
                1 => baseFontSize + 9,
                2 => baseFontSize + 6,
                3 => baseFontSize + 4,
                4 => baseFontSize + 2,
                5 => baseFontSize + 1,
                _ => baseFontSize,
            },
            Margin = new Thickness(0, h.Level == 1 ? 4 : 2, 0, 2),
        };
        AppendInlines(p.Inlines, h.Inline, baseFontSize, imageBaseDir);
        return p;
    }

    private static List BuildList(ListBlock list, double baseFontSize, string imageBaseDir)
    {
        var l = new List
        {
            MarkerStyle = list.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
            Margin = new Thickness(0, 2, 0, 2),
            Padding = new Thickness(20, 0, 0, 0),
        };
        foreach (var child in list)
        {
            if (child is not ListItemBlock item) continue;
            var li = new ListItem();
            foreach (var sub in item)
            {
                var converted = ConvertBlock(sub, baseFontSize, imageBaseDir);
                if (converted is not null) li.Blocks.Add(converted);
            }
            l.ListItems.Add(li);
        }
        return l;
    }

    private static Paragraph BuildCodeBlock(string code)
    {
        if (code.EndsWith('\n')) code = code[..^1];
        if (code.EndsWith('\r')) code = code[..^1];

        return new Paragraph(new Run(code))
        {
            FontFamily = new FontFamily("Consolas, Cascadia Mono, Courier New"),
            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0xDC, 0xDC)),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 4, 0, 4),
        };
    }

    private static Paragraph BuildParagraph(ContainerInline? inlines, double baseFontSize, string imageBaseDir)
    {
        var p = new Paragraph { Margin = new Thickness(0, 2, 0, 2) };
        AppendInlines(p.Inlines, inlines, baseFontSize, imageBaseDir);
        return p;
    }

    private static Section BuildQuote(QuoteBlock q, double baseFontSize, string imageBaseDir)
    {
        var s = new Section
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(8, 2, 0, 2),
            Margin = new Thickness(0, 2, 0, 2),
            Foreground = new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xBB)),
        };
        foreach (var child in q)
        {
            var converted = ConvertBlock(child, baseFontSize, imageBaseDir);
            if (converted is not null) s.Blocks.Add(converted);
        }
        return s;
    }

    private static void AppendInlines(InlineCollection target, ContainerInline? source, double baseFontSize, string imageBaseDir)
    {
        if (source is null) return;
        var child = source.FirstChild;
        while (child is not null)
        {
            AppendInline(target, child, baseFontSize, imageBaseDir);
            child = child.NextSibling;
        }
    }

    private static void AppendInline(InlineCollection target, Markdig.Syntax.Inlines.Inline inline, double baseFontSize, string imageBaseDir)
    {
        switch (inline)
        {
            case LiteralInline literal:
                AppendTextWithEmoji(target, literal.Content.ToString(), baseFontSize);
                break;

            case LineBreakInline:
                // 평문 노트 호환: soft/hard 구분 없이 줄바꿈 보존
                target.Add(new LineBreak());
                break;

            case CodeInline code:
                target.Add(new Run(code.Content)
                {
                    FontFamily = new FontFamily("Consolas, Cascadia Mono, Courier New"),
                    Background = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
                });
                break;

            case EmphasisInline emphasis:
                {
                    System.Windows.Documents.Span span = emphasis.DelimiterCount >= 2 ? new Bold() : new Italic();
                    AppendInlines(span.Inlines, emphasis, baseFontSize, imageBaseDir);
                    target.Add(span);
                    break;
                }

            case LinkInline link when link.IsImage:
                {
                    var img = TryBuildImage(link.Url, imageBaseDir);
                    if (img is not null) target.Add(new InlineUIContainer(img));
                    else target.Add(new Run($"[이미지 로드 실패: {link.Url}]") { Foreground = Brushes.OrangeRed });
                    break;
                }

            case LinkInline link:
                {
                    var hl = new Hyperlink { Foreground = new SolidColorBrush(Color.FromRgb(0x6C, 0xB6, 0xFF)) };
                    if (Uri.TryCreate(link.Url, UriKind.Absolute, out var uri))
                    {
                        hl.NavigateUri = uri;
                        hl.RequestNavigate += OnNavigateRequest;
                    }
                    AppendInlines(hl.Inlines, link, baseFontSize, imageBaseDir);
                    if (hl.Inlines.Count == 0) hl.Inlines.Add(new Run(link.Url ?? string.Empty));
                    target.Add(hl);
                    break;
                }

            case AutolinkInline auto:
                {
                    var hl = new Hyperlink(new Run(auto.Url))
                    {
                        Foreground = new SolidColorBrush(Color.FromRgb(0x6C, 0xB6, 0xFF))
                    };
                    if (Uri.TryCreate(auto.Url, UriKind.Absolute, out var uri))
                    {
                        hl.NavigateUri = uri;
                        hl.RequestNavigate += OnNavigateRequest;
                    }
                    target.Add(hl);
                    break;
                }

            case ContainerInline container:
                AppendInlines(target, container, baseFontSize, imageBaseDir);
                break;

            default:
                target.Add(new Run(inline.ToString() ?? string.Empty));
                break;
        }
    }

    /// <summary>
    /// 텍스트를 코드포인트 단위로 스캔해 이모지 영역만 EmojiInline으로 변환한다.
    /// ZWJ/skin tone modifier/variation selector를 묶어 한 inline으로 처리.
    /// </summary>
    private static void AppendTextWithEmoji(InlineCollection target, string text, double baseFontSize)
    {
        if (string.IsNullOrEmpty(text)) return;

        var buf = new System.Text.StringBuilder();
        var i = 0;
        while (i < text.Length)
        {
            int cp;
            int len;
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                cp = char.ConvertToUtf32(text[i], text[i + 1]);
                len = 2;
            }
            else
            {
                cp = text[i];
                len = 1;
            }

            if (IsEmojiCodepoint(cp))
            {
                FlushBuffer(target, buf);
                var start = i;
                i += len;
                while (i < text.Length)
                {
                    int next;
                    int nextLen;
                    if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                    {
                        next = char.ConvertToUtf32(text[i], text[i + 1]);
                        nextLen = 2;
                    }
                    else { next = text[i]; nextLen = 1; }

                    if (IsEmojiCombiner(next) || IsEmojiCodepoint(next))
                    {
                        i += nextLen;
                    }
                    else break;
                }
                var emojiText = text.Substring(start, i - start);
                target.Add(new EmojiWpf.EmojiInline
                {
                    Text = emojiText,
                    FontSize = baseFontSize,
                });
            }
            else
            {
                buf.Append(text, i, len);
                i += len;
            }
        }
        FlushBuffer(target, buf);
    }

    private static void FlushBuffer(InlineCollection target, System.Text.StringBuilder buf)
    {
        if (buf.Length == 0) return;
        target.Add(new Run(buf.ToString()));
        buf.Clear();
    }

    private static bool IsEmojiCodepoint(int cp)
    {
        return (cp >= 0x1F300 && cp <= 0x1FAFF)   // 픽토그래프, 이모티콘, 보충 심볼 등
            || (cp >= 0x2600 && cp <= 0x27BF)     // 기타 심볼 + 딩벳
            || (cp >= 0x1F1E6 && cp <= 0x1F1FF)   // regional indicator (국기)
            || cp == 0x2122 || cp == 0x2139
            || (cp >= 0x2194 && cp <= 0x2199)
            || (cp >= 0x21A9 && cp <= 0x21AA)
            || (cp >= 0x231A && cp <= 0x231B)
            || cp == 0x2328 || cp == 0x23CF
            || (cp >= 0x23E9 && cp <= 0x23F3)
            || (cp >= 0x23F8 && cp <= 0x23FA)
            || cp == 0x24C2
            || (cp >= 0x25AA && cp <= 0x25AB)
            || cp == 0x25B6 || cp == 0x25C0
            || (cp >= 0x25FB && cp <= 0x25FE);
    }

    private static bool IsEmojiCombiner(int cp)
    {
        return cp == 0x200D       // ZWJ
            || cp == 0xFE0E        // text variation selector
            || cp == 0xFE0F        // emoji variation selector
            || (cp >= 0x1F3FB && cp <= 0x1F3FF);  // skin tone modifier
    }

    private static UIElement? TryBuildImage(string? url, string imageBaseDir)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        try
        {
            var path = ResolveImagePath(url, imageBaseDir);
            if (path is null) return null;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();

            return new Image
            {
                Source = bmp,
                Stretch = Stretch.Uniform,
                MaxHeight = 400,
                Margin = new Thickness(0, 4, 0, 4),
            };
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveImagePath(string url, string imageBaseDir)
    {
        // http/https는 미지원 (로컬 노트 앱)
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return null;

        if (Path.IsPathRooted(url))
            return File.Exists(url) ? url : null;

        var combined = Path.Combine(imageBaseDir, url);
        return File.Exists(combined) ? combined : null;
    }

    private static void OnNavigateRequest(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true,
            });
        }
        catch { /* 잘못된 URL 무시 */ }
        e.Handled = true;
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Navigation;

namespace TrackSwap.Controls
{
    internal static class MarkdownFlowDocumentRenderer
    {
        private static readonly Regex HeadingPattern = new Regex(
            @"^(#{1,6})\s+(.+)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex InlinePattern = new Regex(
            @"(`[^`]+`|https?://[^\s]+)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        public static FlowDocument Render(
            string markdown,
            Brush textBrush,
            Brush mutedTextBrush,
            Brush codeBackgroundBrush,
            Brush accentBrush)
        {
            var document = new FlowDocument
            {
                PagePadding = new Thickness(0, 0, 12, 18),
                Foreground = textBrush,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 12,
                LineHeight = 18
            };

            string[] lines = (markdown ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n');
            var paragraphLines = new List<string>();
            var codeLines = new List<string>();
            bool insideCodeBlock = false;

            foreach (string line in lines)
            {
                if (line.StartsWith("```", StringComparison.Ordinal))
                {
                    FlushParagraph(
                        document,
                        paragraphLines,
                        textBrush,
                        codeBackgroundBrush,
                        accentBrush);
                    if (insideCodeBlock)
                    {
                        AddCodeBlock(document, codeLines, textBrush, codeBackgroundBrush);
                        codeLines.Clear();
                    }
                    insideCodeBlock = !insideCodeBlock;
                    continue;
                }

                if (insideCodeBlock)
                {
                    codeLines.Add(line);
                    continue;
                }

                Match heading = HeadingPattern.Match(line);
                if (heading.Success)
                {
                    FlushParagraph(
                        document,
                        paragraphLines,
                        textBrush,
                        codeBackgroundBrush,
                        accentBrush);
                    AddHeading(
                        document,
                        heading.Groups[2].Value.Trim(),
                        heading.Groups[1].Value.Length,
                        textBrush,
                        codeBackgroundBrush,
                        accentBrush);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    FlushParagraph(
                        document,
                        paragraphLines,
                        textBrush,
                        codeBackgroundBrush,
                        accentBrush);
                    continue;
                }

                paragraphLines.Add(line.Trim());
            }

            FlushParagraph(
                document,
                paragraphLines,
                textBrush,
                codeBackgroundBrush,
                accentBrush);
            if (insideCodeBlock || codeLines.Count > 0)
            {
                AddCodeBlock(document, codeLines, textBrush, codeBackgroundBrush);
            }

            if (document.Blocks.Count == 0)
            {
                document.Blocks.Add(new Paragraph(new Run("No notices are available."))
                {
                    Foreground = mutedTextBrush
                });
            }
            return document;
        }

        private static void FlushParagraph(
            FlowDocument document,
            ICollection<string> lines,
            Brush textBrush,
            Brush codeBackgroundBrush,
            Brush accentBrush)
        {
            if (lines.Count == 0)
            {
                return;
            }

            var paragraph = new Paragraph
            {
                Margin = new Thickness(0, 0, 0, 12),
                Foreground = textBrush
            };
            AddInlineContent(
                paragraph,
                string.Join(" ", lines),
                textBrush,
                codeBackgroundBrush,
                accentBrush);
            document.Blocks.Add(paragraph);
            lines.Clear();
        }

        private static void AddHeading(
            FlowDocument document,
            string text,
            int level,
            Brush textBrush,
            Brush codeBackgroundBrush,
            Brush accentBrush)
        {
            double fontSize = level == 1 ? 24 : level == 2 ? 18 : 14;
            var paragraph = new Paragraph
            {
                Margin = new Thickness(0, level == 1 ? 0 : 10, 0, 8),
                FontSize = fontSize,
                FontWeight = level <= 2 ? FontWeights.SemiBold : FontWeights.Medium,
                Foreground = textBrush,
                LineHeight = fontSize + 6
            };
            AddInlineContent(paragraph, text, textBrush, codeBackgroundBrush, accentBrush);
            document.Blocks.Add(paragraph);
        }

        private static void AddCodeBlock(
            FlowDocument document,
            ICollection<string> lines,
            Brush textBrush,
            Brush codeBackgroundBrush)
        {
            var paragraph = new Paragraph(new Run(string.Join(Environment.NewLine, lines)))
            {
                Margin = new Thickness(0, 0, 0, 14),
                Padding = new Thickness(12, 10, 12, 10),
                Background = codeBackgroundBrush,
                Foreground = textBrush,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                LineHeight = 16
            };
            document.Blocks.Add(paragraph);
        }

        private static void AddInlineContent(
            Paragraph paragraph,
            string text,
            Brush textBrush,
            Brush codeBackgroundBrush,
            Brush accentBrush)
        {
            int position = 0;
            foreach (Match match in InlinePattern.Matches(text))
            {
                if (match.Index > position)
                {
                    paragraph.Inlines.Add(new Run(text.Substring(position, match.Index - position)));
                }

                string token = match.Value;
                if (token.Length >= 2 && token[0] == '`' && token[token.Length - 1] == '`')
                {
                    paragraph.Inlines.Add(new Run(token.Substring(1, token.Length - 2))
                    {
                        FontFamily = new FontFamily("Consolas"),
                        Background = codeBackgroundBrush,
                        Foreground = textBrush
                    });
                }
                else
                {
                    string address = token.TrimEnd('.', ',', ';', ':', ')', ']');
                    string suffix = token.Substring(address.Length);
                    var hyperlink = new Hyperlink(new Run(address))
                    {
                        NavigateUri = new Uri(address, UriKind.Absolute),
                        Foreground = accentBrush
                    };
                    hyperlink.RequestNavigate += OpenHyperlink;
                    paragraph.Inlines.Add(hyperlink);
                    if (suffix.Length > 0)
                    {
                        paragraph.Inlines.Add(new Run(suffix));
                    }
                }
                position = match.Index + match.Length;
            }

            if (position < text.Length)
            {
                paragraph.Inlines.Add(new Run(text.Substring(position)));
            }
        }

        private static void OpenHyperlink(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri)
                {
                    UseShellExecute = true
                });
            }
            catch
            {
                // The legal text remains readable when the shell cannot open a link.
            }
            e.Handled = true;
        }
    }
}

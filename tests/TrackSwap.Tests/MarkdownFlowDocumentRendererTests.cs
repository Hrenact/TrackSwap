using System;
using System.Linq;
using System.Threading;
using System.Windows.Documents;
using System.Windows.Media;
using TrackSwap.Controls;

namespace TrackSwap.Tests;

public sealed class MarkdownFlowDocumentRendererTests
{
    [Fact]
    public void RenderCreatesHeadingInlineCodeLinkAndCodeBlock()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                FlowDocument document = MarkdownFlowDocumentRenderer.Render(
                    "# Notices\n\nUse `code` at https://example.com.\n\n```text\nlicense text\n```",
                    Brushes.White,
                    Brushes.Gray,
                    Brushes.DarkGray,
                    Brushes.CornflowerBlue);
                Block[] blocks = document.Blocks.Cast<Block>().ToArray();

                Assert.Equal(3, blocks.Length);
                Assert.Equal(24, Assert.IsType<Paragraph>(blocks[0]).FontSize);
                Paragraph body = Assert.IsType<Paragraph>(blocks[1]);
                Assert.Contains(body.Inlines.Cast<Inline>(), inline => inline is Hyperlink);
                Assert.Contains(
                    body.Inlines.Cast<Inline>().OfType<Run>(),
                    run => run.Text == "code" && run.FontFamily.Source == "Consolas");
                Paragraph codeBlock = Assert.IsType<Paragraph>(blocks[2]);
                Assert.Equal("Consolas", codeBlock.FontFamily.Source);
                Assert.Contains("license text", new TextRange(
                    codeBlock.ContentStart,
                    codeBlock.ContentEnd).Text);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null)
        {
            throw failure;
        }
    }
}

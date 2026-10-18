using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using MdInline = Markdig.Syntax.Inlines.Inline;
using Block = Markdig.Syntax.Block;

namespace MemoAI.App;

public sealed class NativeMarkdownView : ContentControl
{
    private static readonly MarkdownPipeline pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().UseCjkFriendlyEmphasis().Build();
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(NativeMarkdownView), new PropertyMetadata("", (d, _) => ((NativeMarkdownView)d).Render()));
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public NativeMarkdownView() { HorizontalContentAlignment = HorizontalAlignment.Stretch; IsTabStop = false; }
    private void Render()
    {
        var panel = new StackPanel { Spacing = 9 };
        try { foreach (var block in Markdown.Parse(Text ?? "", pipeline)) panel.Children.Add(BlockView(block)); }
        catch { panel.Children.Add(new TextBlock { Text = Text ?? "", TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true }); }
        Content = panel;
    }
    private static RichTextBlock Rich(LeafBlock block, double size = 15)
    {
        var view = new RichTextBlock { FontSize = size, LineHeight = size * 1.7, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, FontFamily = (FontFamily)Application.Current.Resources["ContentControlThemeFontFamily"] };
        var paragraph = new Paragraph(); if (block.Inline is not null) Inlines(block.Inline, paragraph.Inlines); view.Blocks.Add(paragraph); return view;
    }
    private static UIElement BlockView(Block block)
    {
        if (block is HeadingBlock heading) { var rich = Rich(heading, heading.Level == 1 ? 22 : heading.Level == 2 ? 19 : 16); rich.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; rich.Margin = new Thickness(0, 7, 0, 1); return rich; }
        if (block is CodeBlock code)
        {
            var content = code.Lines.ToString(); var stack = new StackPanel { Spacing = 8 };
            var copy = new Button { Content = "复制代码", FontSize = 12, Padding = new Thickness(6, 2, 6, 2), HorizontalAlignment = HorizontalAlignment.Right };
            copy.Click += (_, _) => { var data = new DataPackage(); data.SetText(content); Clipboard.SetContent(data); copy.Content = "已复制"; };
            stack.Children.Add(copy); stack.Children.Add(new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = new TextBlock { Text = content, FontFamily = new("Consolas"), FontSize = 13, IsTextSelectionEnabled = true, TextWrapping = TextWrapping.NoWrap } });
            return new Border { Style = (Style)Application.Current.Resources["Card"], Padding = new Thickness(10), Child = stack };
        }
        if (block is Table table)
        {
            var grid = new Grid(); var rows = table.OfType<TableRow>().ToArray(); var columns = rows.Select(r => r.Count).DefaultIfEmpty(1).Max();
            for (var i = 0; i < columns; i++) grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            for (var r = 0; r < rows.Length; r++)
            {
                grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
                for (var c = 0; c < rows[r].Count; c++)
                {
                    var cell = new StackPanel { Spacing = 4 }; foreach (var child in (TableCell)rows[r][c]) cell.Children.Add(BlockView(child));
                    var border = new Border { Style = (Style)Application.Current.Resources["Card"], CornerRadius = new CornerRadius(0), Padding = new Thickness(6), Child = cell }; Grid.SetRow(border, r); Grid.SetColumn(border, c); grid.Children.Add(border);
                }
            }
            return grid;
        }
        if (block is ListBlock list)
        {
            var stack = new StackPanel { Spacing = 7 }; var number = int.TryParse(list.OrderedStart, out var start) ? start : 1;
            foreach (var item in list.OfType<ListItemBlock>())
            {
                var row = new Grid { ColumnSpacing = 7 }; row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                row.Children.Add(new TextBlock { Text = list.IsOrdered ? $"{number++}." : "•", FontSize = 15, Margin = new Thickness(0, 3, 0, 0) });
                var body = new StackPanel { Spacing = 5 }; foreach (var child in item) body.Children.Add(BlockView(child)); Grid.SetColumn(body, 1); row.Children.Add(body); stack.Children.Add(row);
            }
            return stack;
        }
        if (block is QuoteBlock quote) { var body = new StackPanel { Spacing = 7 }; foreach (var child in quote) body.Children.Add(BlockView(child)); return new Border { BorderBrush = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"], BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(12, 0, 0, 0), Child = body }; }
        if (block is ThematicBreakBlock) return new Border { Height = 1, Background = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"], Margin = new Thickness(0, 8, 0, 8) };
        if (block is LeafBlock leaf) return Rich(leaf);
        var fallback = new StackPanel { Spacing = 6 }; if (block is ContainerBlock container) foreach (var child in container) fallback.Children.Add(BlockView(child)); return fallback;
    }
    private static void Inlines(ContainerInline container, InlineCollection target)
    { for (var inline = container.FirstChild; inline is not null; inline = inline.NextSibling) InlineView(inline, target); }
    private static void InlineView(MdInline inline, InlineCollection target)
    {
        switch (inline)
        {
            case LiteralInline literal: target.Add(new Run { Text = literal.Content.ToString() }); break;
            case CodeInline code: target.Add(new Run { Text = code.Content, FontFamily = new("Consolas"), FontSize = 14 }); break;
            case LineBreakInline: target.Add(new LineBreak()); break;
            case EmphasisInline emphasis:
                Span span = emphasis.DelimiterCount >= 2 ? new Bold() : new Italic(); Inlines(emphasis, span.Inlines); target.Add(span); break;
            case LinkInline link:
                var hyperlink = new Hyperlink(); Inlines(link, hyperlink.Inlines); if (link.IsImage) hyperlink.Inlines.Insert(0, new Run { Text = "图片：" });
                if (Uri.TryCreate(link.Url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http") hyperlink.Click += async (_, _) => await Windows.System.Launcher.LaunchUriAsync(uri);
                target.Add(hyperlink); break;
            case AutolinkInline auto:
                var automatic = new Hyperlink(); automatic.Inlines.Add(new Run { Text = auto.Url }); if (Uri.TryCreate(auto.Url, UriKind.Absolute, out var address) && address.Scheme is "https" or "http") automatic.Click += async (_, _) => await Windows.System.Launcher.LaunchUriAsync(address); target.Add(automatic); break;
            case ContainerInline children: Inlines(children, target); break;
            case HtmlInline html: target.Add(new Run { Text = html.Tag }); break;
        }
    }
}

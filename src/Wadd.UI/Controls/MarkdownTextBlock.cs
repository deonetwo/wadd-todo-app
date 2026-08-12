using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;

namespace Wadd.UI.Controls;

public class MarkdownTextBlock : SelectableTextBlock
{
    public static readonly StyledProperty<string?> MarkdownTextProperty =
        AvaloniaProperty.Register<MarkdownTextBlock, string?>(nameof(MarkdownText));

    public string? MarkdownText
    {
        get => GetValue(MarkdownTextProperty);
        set => SetValue(MarkdownTextProperty, value);
    }

    static MarkdownTextBlock()
    {
        MarkdownTextProperty.Changed.AddClassHandler<MarkdownTextBlock>((x, e) => x.OnMarkdownTextChanged());
    }

    private void OnMarkdownTextChanged()
    {
        Inlines?.Clear();
        var text = MarkdownText;
        if (string.IsNullOrWhiteSpace(text))
        {
            IsVisible = false;
            return;
        }

        IsVisible = true;
        ParseMarkdownToInlines(text);
    }

    private void ParseMarkdownToInlines(string text)
    {
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (i > 0)
            {
                Inlines?.Add(new LineBreak());
            }

            string processedLine = line;
            if (line.TrimStart().StartsWith("- ") || line.TrimStart().StartsWith("* "))
            {
                Inlines?.Add(new Run("• ") { FontWeight = FontWeight.Bold });
                var bulletIndex = line.IndexOfAny(new[] { '-', '*' });
                if (bulletIndex >= 0 && bulletIndex + 1 < line.Length)
                {
                    processedLine = line.Substring(bulletIndex + 1).TrimStart();
                }
            }

            ParseFormattedLine(processedLine);
        }
    }

    private void ParseFormattedLine(string line)
    {
        // Combined regex for links, bold, italic, and inline code
        // 1. Markdown link: [text](url)
        // 2. Raw URL: https?://\S+
        // 3. Bold: \*\*(.*?)\*\*
        // 4. Italic: \*(.*?)\*
        // 5. Code: `(.*?)`
        var pattern = @"(?<mdlink>\[(?<linktext>[^\]]+)\]\((?<url>https?://[^\s\)]+)\))|(?<rawurl>https?://[^\s\)\>]+)|(?<bold>\*\*([^\*]+)\*\*)|(?<italic>\*([^\*]+)\*)|(?<code>`([^`]+)`)";

        var matches = Regex.Matches(line, pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
        int lastIndex = 0;

        foreach (Match match in matches)
        {
            if (match.Index > lastIndex)
            {
                Inlines?.Add(new Run(line.Substring(lastIndex, match.Index - lastIndex)));
            }

            if (match.Groups["mdlink"].Success)
            {
                AddHyperlink(match.Groups["linktext"].Value, match.Groups["url"].Value);
            }
            else if (match.Groups["rawurl"].Success)
            {
                AddHyperlink(match.Groups["rawurl"].Value, match.Groups["rawurl"].Value);
            }
            else if (match.Groups["bold"].Success)
            {
                var content = match.Value.Substring(2, match.Value.Length - 4);
                Inlines?.Add(new Run(content) { FontWeight = FontWeight.Bold });
            }
            else if (match.Groups["italic"].Success)
            {
                var content = match.Value.Substring(1, match.Value.Length - 2);
                Inlines?.Add(new Run(content) { FontStyle = FontStyle.Italic });
            }
            else if (match.Groups["code"].Success)
            {
                var content = match.Value.Substring(1, match.Value.Length - 2);
                Inlines?.Add(new Run(content)
                {
                    FontFamily = new FontFamily("Consolas, Monospace, Courier New"),
                    Background = new SolidColorBrush(Color.FromArgb(30, 128, 128, 128))
                });
            }

            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < line.Length)
        {
            Inlines?.Add(new Run(line.Substring(lastIndex)));
        }
    }

    private void AddHyperlink(string display, string url)
    {
        var linkButton = new Button
        {
            Content = new TextBlock
            {
                Text = display,
                Foreground = new SolidColorBrush(Color.Parse("#3B82F6")),
                TextDecorations = new TextDecorationCollection { new TextDecoration { Location = TextDecorationLocation.Underline } },
                FontSize = this.FontSize
            },
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            Cursor = new Cursor(StandardCursorType.Hand),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

        linkButton.Click += (s, e) =>
        {
            try
            {
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                    (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = uri.ToString(),
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to open URL {url}: {ex.Message}");
            }
        };

        Inlines?.Add(new InlineUIContainer(linkButton));
    }
}

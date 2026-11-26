using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Pardeh.Core.Updates;
using Pardeh.UI.FullMode;

namespace Pardeh.UI.Rendering;

/// <summary>The desktop "About Pardeh" window: the logo, the facts of this copy, the update check and the project's links.</summary>
internal static class AboutDialog
{
    private sealed class LogoView : Control
    {
        public override void Render(DrawingContext context) => PardehLogo.Draw(context, new Rect(Bounds.Size));
    }

    private static TextBlock Text(string text, double size = 12, bool bold = false, IBrush? ink = null) => new()
    {
        Text = text, FontFamily = UiTheme.Family, FontSize = size, FontWeight = bold ? FontWeight.Bold : FontWeight.Normal,
        Foreground = ink ?? Brushes.Black, TextWrapping = TextWrapping.Wrap,
    };

    private static Button Button(string label) => new() { Content = label, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };

    /// <summary>The window, not yet shown.</summary>
    private static Window Build()
    {
        var facts = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        var row = 0;
        foreach (var (label, value) in AppInfo.Facts())
        {
            facts.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var name = Text(label + ":", ink: Brushes.DimGray);
            name.Margin = new Thickness(0, 1, 12, 1);
            var fact = Text(value);
            fact.Margin = new Thickness(0, 1);
            Grid.SetRow(name, row);
            Grid.SetRow(fact, row);
            Grid.SetColumn(fact, 1);
            facts.Children.Add(name);
            facts.Children.Add(fact);
            row++;
        }

        var status = Text("Pardeh contacts GitHub only when you press the button.", 11, ink: Brushes.DimGray);
        var check = Button("Check for updates");
        var open = Button("Open release page");
        open.IsVisible = false;
        var page = AppInfo.ReleasesPage;
        check.Click += async (_, _) =>
        {
            check.IsEnabled = false;
            open.IsVisible = false;
            status.Text = "Checking...";
            var result = await AppInfo.CheckForUpdate();
            status.Text = result.Message;
            page = result.Page;
            open.IsVisible = result.State == UpdateState.Available;
            check.IsEnabled = true;
        };
        open.Click += (_, _) => PlatformServices.Current.OpenUrl(page);

        var links = new WrapPanel();
        foreach (var (title, url) in AppInfo.Links)
        {
            var link = Button(title);
            link.Margin = new Thickness(0, 0, 6, 6);
            ToolTip.SetTip(link, url);
            link.Click += (_, _) => PlatformServices.Current.OpenUrl(url);
            links.Children.Add(link);
        }

        var copy = Button("Copy details");
        var close = Button("Close");
        close.IsDefault = close.IsCancel = true;

        var heading = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 3, Children = { Text("Pardeh", 20, bold: true), Text(AppInfo.Tagline) } };
        var dialog = new Window
        {
            Title = "About Pardeh",
            Width = 440,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 10,
                Children =
                {
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { new LogoView { Width = 64, Height = 64 }, heading } },
                    facts,
                    Text(AppInfo.Disclaimer, 11, ink: Brushes.DimGray),
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { check, open } },
                    status,
                    links,
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { copy, close } },
                },
            },
        };
        copy.Click += async (_, _) =>
        {
            if (dialog.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(AppInfo.Details());
                copy.Content = "Copied";
            }
        };
        close.Click += (_, _) => dialog.Close();
        UiTheme.Dialog(dialog);
        return dialog;
    }

    public static Task Show(Window owner) => Build().ShowDialog(owner);
}

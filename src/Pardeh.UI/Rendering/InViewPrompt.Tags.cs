using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.TextInput;
using Avalonia.Layout;
using Avalonia.Media;
using Pardeh.Core.Changes;

namespace Pardeh.UI.Rendering;

internal static partial class InViewPrompt
{
    /// <summary>
    /// Edit Info for a head without windows (Android): the fields in a scrolling card with Cancel and Save below them.
    /// Completes with what the user changed, or null. <paramref name="cancelWith"/> receives the action the Back key runs.
    /// </summary>
    public static Task<TagFields?> EditTags(Canvas overlay, double viewWidth, double viewHeight, string title, TagFields current, Action<Action> cancelWith)
    {
        var done = new TaskCompletionSource<TagFields?>();
        var shown = TagForm.Values(current);
        var boxes = shown.Select((value, i) =>
        {
            var box = new TextBox { Text = value, FontSize = 15, MinHeight = 40, VerticalContentAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(8) };
            if (TagForm.IsNumber(i))
                TextInputOptions.SetContentType(box, TextInputContentType.Number);
            return box;
        }).ToArray();

        var fields = new StackPanel { Spacing = 4 };
        for (var i = 0; i < boxes.Length; i++)
        {
            fields.Children.Add(new TextBlock { Text = TagForm.Labels[i], FontSize = 12, Foreground = new SolidColorBrush(Color.Parse("#B8BFD0")) });
            fields.Children.Add(boxes[i]);
        }

        var error = new TextBlock { FontSize = 13, Foreground = new SolidColorBrush(Color.Parse("#FF9C9C")), TextWrapping = TextWrapping.Wrap, IsVisible = false };
        var save = Pill("Save", primary: true);
        var cancel = Pill("Cancel");
        const double top = 44;
        var card = Card(viewWidth, new StackPanel
        {
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis },
                // The fields scroll, so the buttons stay in sight on a short screen and above the keyboard.
                new ScrollViewer
                {
                    MaxHeight = Math.Clamp(viewHeight * 0.55, 160, 460), Content = fields,
                    VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden,
                },
                error,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, save } },
            },
        });
        var host = Host(overlay, card, top);

        void Finish(TagFields? answer)
        {
            overlay.Children.Remove(host);
            done.TrySetResult(answer);
        }

        save.Click += (_, _) =>
        {
            var (change, problem) = TagForm.Read(shown, boxes.Select(b => b.Text ?? string.Empty).ToArray());
            if (change is null)
            {
                error.Text = problem;
                error.IsVisible = true;
                return;
            }

            Finish(change);
        };
        cancel.Click += (_, _) => Finish(null);
        cancelWith(() => Finish(null));
        return done.Task;
    }
}

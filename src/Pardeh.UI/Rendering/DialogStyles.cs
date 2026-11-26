using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Path = Avalonia.Controls.Shapes.Path;

namespace Pardeh.UI.Rendering;

/// <summary>
/// The look of Pardeh's own dialogs (windows with the <see cref="Class"/> class): XP (beige surface, Luna buttons,
/// square blue-grey fields, square check boxes with a green tick, boxed tabs) or Windows 11 (Fluent on a light surface).
/// Applied as app styles, so an open dialog changes as soon as the option does.
/// </summary>
internal static class DialogStyles
{
    public const string Class = "pardeh-dialog";

    private static readonly IBrush XpSurface = Solid("#ECE9D8");
    private static readonly IBrush XpButtonBorder = Solid("#003C74");
    private static readonly IBrush XpHover = Solid("#E8A33C");
    private static readonly IBrush XpFieldBorder = Solid("#7F9DB9");
    private static readonly IBrush XpTabBorder = Solid("#919B9C");
    private static readonly IBrush XpTabPage = Solid("#FCFCFE");
    private static readonly IBrush XpTick = Solid("#21A121");

    private static IBrush Solid(string color) => new SolidColorBrush(Color.Parse(color));

    private static IBrush Vertical(string top, string bottom) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.Parse(top), 0), new GradientStop(Color.Parse(bottom), 1) },
    };

    /// <summary>Adds each pseudo-class of a state such as ":selected:pointerover" (null: none).</summary>
    private static Selector Cls(Selector selector, string? state)
    {
        foreach (var part in (state ?? string.Empty).Split(':', StringSplitOptions.RemoveEmptyEntries))
            selector = selector.Class(":" + part);
        return selector;
    }

    private static Style S(Func<Selector, Selector> selector, params (AvaloniaProperty Property, object? Value)[] setters)
    {
        var style = new Style(x => selector(x.Is<TemplatedControl>().Class(Class))); // dialog windows and the Settings page
        foreach (var (property, value) in setters)
            style.Setters.Add(new Setter(property, value));
        return style;
    }

    public static Styles Create(bool modern, FontFamily family)
    {
        var styles = new Styles
        {
            S(x => x, (TemplatedControl.FontFamilyProperty, family), (TemplatedControl.FontSizeProperty, modern ? 12.0 : 11.0),
                (TemplatedControl.BackgroundProperty, modern ? Solid("#F3F3F3") : XpSurface)),
        };
        if (modern)
            return styles;

        // Buttons: white-to-beige face, dark blue rounded border, orange edge on hover, darker face when pressed.
        var face = Vertical("#FFFFFF", "#E3E1D6");
        styles.Add(S(x => x.Descendant().OfType<Button>(),
            (TemplatedControl.BackgroundProperty, face), (TemplatedControl.BorderBrushProperty, XpButtonBorder),
            (TemplatedControl.BorderThicknessProperty, new Thickness(1)), (TemplatedControl.CornerRadiusProperty, new CornerRadius(3)),
            (TemplatedControl.PaddingProperty, new Thickness(8, 2)), (Layoutable.MinHeightProperty, 23.0),
            (TemplatedControl.ForegroundProperty, Brushes.Black)));
        styles.Add(S(x => x.Descendant().OfType<Button>().Class(":pointerover").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"),
            (ContentPresenter.BackgroundProperty, face), (ContentPresenter.BorderBrushProperty, XpHover),
            (ContentPresenter.ForegroundProperty, Brushes.Black)));
        styles.Add(S(x => x.Descendant().OfType<Button>().Class(":pressed").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"),
            (ContentPresenter.BackgroundProperty, Vertical("#E3E1D6", "#F4F3EE")), (ContentPresenter.BorderBrushProperty, XpButtonBorder),
            (ContentPresenter.ForegroundProperty, Brushes.Black)));

        // Text boxes, numeric boxes, combo boxes and lists: square with the XP blue-grey border, also when hovered or focused.
        foreach (var part in new[] { "PART_BorderElement" })
        {
            foreach (var state in new[] { (string?)null, ":pointerover", ":focus" })
            {
                styles.Add(S(x =>
                    {
                        var box = x.Descendant().OfType<TextBox>();
                        return Cls(box, state).Template().OfType<Border>().Name(part);
                    },
                    (Border.BorderBrushProperty, XpFieldBorder), (Border.BorderThicknessProperty, new Thickness(1)),
                    (Border.CornerRadiusProperty, new CornerRadius(0)), (Border.BackgroundProperty, Brushes.White)));
            }
        }

        styles.Add(S(x => x.Descendant().OfType<TextBox>(),
            (TextBox.SelectionBrushProperty, Solid("#316AC5")), (TextBox.SelectionForegroundBrushProperty, Brushes.White),
            (TemplatedControl.ForegroundProperty, Brushes.Black)));

        foreach (var state in new[] { (string?)null, ":pointerover", ":focus-within", ":dropdownopen" })
        {
            styles.Add(S(x =>
                {
                    var combo = x.Descendant().OfType<ComboBox>();
                    return Cls(combo, state).Template().OfType<Border>().Name("Background");
                },
                (Border.BorderBrushProperty, XpFieldBorder), (Border.BorderThicknessProperty, new Thickness(1)),
                (Border.CornerRadiusProperty, new CornerRadius(0)), (Border.BackgroundProperty, Brushes.White)));
        }

        styles.Add(S(x => x.Descendant().OfType<ListBox>(),
            (TemplatedControl.BorderBrushProperty, XpFieldBorder), (TemplatedControl.BorderThicknessProperty, new Thickness(1)),
            (TemplatedControl.CornerRadiusProperty, new CornerRadius(0)), (TemplatedControl.BackgroundProperty, Brushes.White)));
        styles.Add(S(x => x.Descendant().OfType<ListBoxItem>(), (TemplatedControl.PaddingProperty, new Thickness(4, 1)), (Layoutable.MinHeightProperty, 0.0)));
        foreach (var state in new[] { ":selected", ":selected:pointerover", ":selected:focus" })
        {
            styles.Add(S(x => Cls(x.Descendant().OfType<ListBoxItem>(), state).Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"),
                (ContentPresenter.BackgroundProperty, Solid("#316AC5")), (ContentPresenter.ForegroundProperty, Brushes.White)));
        }

        // Check boxes: a 13-pixel square, dark blue border, white face, green tick.
        foreach (var state in new[] { (string?)null, ":pointerover", ":pressed", ":checked", ":checked:pointerover", ":checked:pressed" })
        {
            styles.Add(S(x =>
                {
                    var check = x.Descendant().OfType<CheckBox>();
                    return Cls(check, state).Template().OfType<Border>().Name("NormalRectangle");
                },
                (Border.BorderBrushProperty, Solid("#1C5180")), (Border.BorderThicknessProperty, new Thickness(1)),
                (Border.CornerRadiusProperty, new CornerRadius(0)), (Border.BackgroundProperty, Vertical("#DCDBD5", "#FFFFFF")),
                (Layoutable.WidthProperty, 13.0), (Layoutable.HeightProperty, 13.0)));
            styles.Add(S(x =>
                {
                    var check = x.Descendant().OfType<CheckBox>();
                    return Cls(check, state).Template().OfType<Path>().Name("CheckGlyph");
                },
                (Shape.FillProperty, XpTick)));
        }

        styles.Add(S(x => x.Descendant().OfType<CheckBox>(), (Layoutable.MinHeightProperty, 18.0), (TemplatedControl.ForegroundProperty, Brushes.Black)));

        // Tabs: small boxed headers; the selected one is the page colour and joins the page below it.
        styles.Add(S(x => x.Descendant().OfType<TabItem>(),
            (Layoutable.MinHeightProperty, 0.0), (TemplatedControl.PaddingProperty, new Thickness(7, 2)),
            (TemplatedControl.FontSizeProperty, 11.0), (Layoutable.MarginProperty, new Thickness(0, 2, 0, 0))));
        foreach (var state in new[] { (string?)null, ":pointerover", ":selected", ":selected:pointerover" })
        {
            var selected = state?.StartsWith(":selected") == true;
            styles.Add(S(x =>
                {
                    var tab = x.Descendant().OfType<TabItem>();
                    return Cls(tab, state).Template().OfType<Border>().Name("PART_LayoutRoot");
                },
                (Border.BackgroundProperty, selected ? XpTabPage : Vertical("#FFFFFF", "#ECEBE6")),
                (Border.BorderBrushProperty, XpTabBorder), (Border.BorderThicknessProperty, new Thickness(1, 1, 1, 0)),
                (Border.CornerRadiusProperty, new CornerRadius(3, 3, 0, 0)),
                (Layoutable.MarginProperty, selected ? new Thickness(0, -2, 0, -1) : new Thickness(0))));
        }

        styles.Add(S(x => x.Descendant().OfType<TabItem>().Template().OfType<Border>().Name("PART_SelectedPipe"), (Visual.IsVisibleProperty, false)));
        foreach (var state in new[] { (string?)null, ":pointerover", ":selected", ":selected:pointerover" })
        {
            styles.Add(S(x =>
                {
                    var tab = x.Descendant().OfType<TabItem>();
                    return Cls(tab, state).Template().OfType<ContentPresenter>().Name("PART_ContentPresenter");
                },
                (ContentPresenter.ForegroundProperty, Brushes.Black), (ContentPresenter.FontSizeProperty, 11.0)));
        }

        styles.Add(S(x => x.Descendant().OfType<TabControl>().Template().OfType<ContentPresenter>().Name("PART_SelectedContentHost"),
            (ContentPresenter.BackgroundProperty, XpTabPage), (ContentPresenter.BorderBrushProperty, XpTabBorder),
            (ContentPresenter.BorderThicknessProperty, new Thickness(1))));
        styles.Add(S(x => x.Descendant().OfType<TabControl>(), (TemplatedControl.PaddingProperty, new Thickness(0))));
        return styles;
    }
}

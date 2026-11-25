using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Path = Avalonia.Controls.Shapes.Path;

namespace Pardeh.UI.Rendering;

/// <summary>
/// Pardeh's context menus and popups, in one of two looks (Appearance › Context menus):
/// <list type="bullet">
/// <item>XP (Luna): white, a 1-pixel grey border, square blue highlight with white text, a small black submenu arrow.</item>
/// <item>Windows 11: a light rounded flyout with a hairline border, inset rounded hover pills, grey shortcuts,
/// a thin chevron and full-width separators.</item>
/// </list>
/// Merged into the app's resources, so every ContextMenu / MenuItem / Separator picks it up.
/// </summary>
internal static class MenuTheme
{
    private sealed record Look(
        IBrush Background, IBrush Border, CornerRadius BoxCorner, Thickness BoxPadding,
        IBrush Text, IBrush HoverBack, IBrush HoverText, IBrush Disabled, IBrush Gesture,
        CornerRadius ItemCorner, Thickness ItemPadding, double ItemMinHeight, double IconColumn, double ChevronColumn,
        IBrush SeparatorBrush, Thickness SeparatorMargin, Geometry Chevron, bool ChevronStroked,
        double SubmenuX, double SubmenuY);

    private static IBrush Solid(string color) => new SolidColorBrush(Color.Parse(color));

    private static readonly Look Xp = new(
        Background: Brushes.White, Border: Solid("#ACA899"), BoxCorner: default, BoxPadding: new Thickness(2),
        Text: Brushes.Black, HoverBack: Solid("#316AC5"), HoverText: Brushes.White, Disabled: Solid("#ACA899"), Gesture: Brushes.Black,
        ItemCorner: default, ItemPadding: new Thickness(0, 2), ItemMinHeight: 17, IconColumn: 20, ChevronColumn: 14,
        SeparatorBrush: Solid("#ACA899"), SeparatorMargin: new Thickness(1, 3), Chevron: Geometry.Parse("M0,0 L4,3.5 L0,7 Z"), ChevronStroked: false,
        SubmenuX: -3, SubmenuY: -3);

    /// <summary>Windows 11 dark (while a dark full mode such as Ember or Fluent is showing).</summary>
    private static readonly Look Windows11Dark = new(
        Background: Solid("#202020"), Border: Solid("#353535"), BoxCorner: new CornerRadius(8), BoxPadding: new Thickness(4),
        Text: Solid("#E8E8E8"), HoverBack: new SolidColorBrush(Color.FromArgb(0x14, 255, 255, 255)), HoverText: Solid("#FFFFFF"),
        Disabled: Solid("#6E6E6E"), Gesture: Solid("#9D9D9D"),
        ItemCorner: new CornerRadius(4), ItemPadding: new Thickness(0, 8), ItemMinHeight: 34, IconColumn: 36, ChevronColumn: 24,
        SeparatorBrush: Solid("#353535"), SeparatorMargin: new Thickness(-4, 3), Chevron: Geometry.Parse("M0,0 L4,4 L0,8"), ChevronStroked: true,
        SubmenuX: -2, SubmenuY: -5);

    private static readonly Look Windows11 = new(
        Background: Solid("#F9F9F9"), Border: new SolidColorBrush(Color.FromArgb(0x24, 0, 0, 0)), BoxCorner: new CornerRadius(8), BoxPadding: new Thickness(4),
        Text: Solid("#1A1A1A"), HoverBack: new SolidColorBrush(Color.FromArgb(0x0A, 0, 0, 0)), HoverText: Solid("#1A1A1A"),
        Disabled: new SolidColorBrush(Color.FromArgb(0x5C, 0, 0, 0)), Gesture: Solid("#5F5F5F"),
        ItemCorner: new CornerRadius(4), ItemPadding: new Thickness(0, 6), ItemMinHeight: 30, IconColumn: 32, ChevronColumn: 24,
        SeparatorBrush: new SolidColorBrush(Color.FromArgb(0x14, 0, 0, 0)), SeparatorMargin: new Thickness(-4, 3), Chevron: Geometry.Parse("M0,0 L4,4 L0,8"), ChevronStroked: true,
        SubmenuX: -2, SubmenuY: -5);

    private static readonly IValueConverter GestureText =
        new FuncValueConverter<KeyGesture?, string?>(g => g?.ToString("p", null));

    public static ResourceDictionary Create(bool modern, FontFamily family, double size, bool dark = false)
    {
        var look = dark ? Windows11Dark : modern ? Windows11 : Xp;
        return new ResourceDictionary
        {
            [typeof(ContextMenu)] = ContextMenuTheme(look, family, size),
            [typeof(MenuItem)] = MenuItemTheme(look),
            [typeof(Separator)] = SeparatorTheme(look),
        };
    }

    private static ControlTheme SeparatorTheme(Look look) => new(typeof(Separator))
    {
        Setters =
        {
            new Setter(Separator.BackgroundProperty, look.SeparatorBrush),
            new Setter(Layoutable.HeightProperty, 1.0),
            new Setter(Layoutable.MarginProperty, look.SeparatorMargin),
            new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<Separator>((_, _) =>
                new Border { [!Border.BackgroundProperty] = new TemplateBinding(TemplatedControl.BackgroundProperty) })),
        },
    };

    private static Control MenuBox(Look look, INameScope scope, bool templateBound)
    {
        var items = new ItemsPresenter { Name = "PART_ItemsPresenter" };
        items[!ItemsPresenter.ItemsPanelProperty] = new TemplateBinding(ItemsControl.ItemsPanelProperty);
        Grid.SetIsSharedSizeScope(items, true);
        KeyboardNavigation.SetTabNavigation(items, KeyboardNavigationMode.Continue);
        items.RegisterInNameScope(scope);

        var border = new Border
        {
            Background = look.Background,
            BorderBrush = look.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = look.BoxCorner,
            Padding = look.BoxPadding,
            ClipToBounds = true,
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = items,
            },
        };
        if (templateBound)
        {
            border[!Border.BackgroundProperty] = new TemplateBinding(TemplatedControl.BackgroundProperty);
            border[!Border.BorderBrushProperty] = new TemplateBinding(TemplatedControl.BorderBrushProperty);
        }

        return border;
    }

    private static ControlTheme ContextMenuTheme(Look look, FontFamily family, double size) => new(typeof(ContextMenu))
    {
        Setters =
        {
            new Setter(TemplatedControl.BackgroundProperty, look.Background),
            new Setter(TemplatedControl.BorderBrushProperty, look.Border),
            new Setter(TemplatedControl.ForegroundProperty, look.Text),
            new Setter(TemplatedControl.FontFamilyProperty, family),
            new Setter(TemplatedControl.FontSizeProperty, size),
            new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<ContextMenu>((_, scope) => MenuBox(look, scope, true))),
        },
    };

    private static ControlTheme MenuItemTheme(Look look) => new(typeof(MenuItem))
    {
        Setters =
        {
            new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent),
            new Setter(TemplatedControl.ForegroundProperty, look.Text),
            new Setter(TemplatedControl.PaddingProperty, look.ItemPadding),
            new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<MenuItem>((item, scope) => MenuItemTemplate(look, item, scope))),
        },
        Children =
        {
            new Style(x => x.Nesting().Class(":selected").Template().OfType<Border>().Name("PART_LayoutRoot"))
            {
                Setters = { new Setter(Border.BackgroundProperty, look.HoverBack) },
            },
            new Style(x => x.Nesting().Class(":selected"))
            {
                Setters = { new Setter(TemplatedControl.ForegroundProperty, look.HoverText) },
            },
            new Style(x => x.Nesting().Class(":disabled"))
            {
                Setters = { new Setter(TemplatedControl.ForegroundProperty, look.Disabled) },
            },
            new Style(x => x.Nesting().Class(":empty").Template().OfType<Path>().Name("PART_ChevronPath"))
            {
                Setters = { new Setter(Visual.IsVisibleProperty, false) },
            },
        },
    };

    private static Control MenuItemTemplate(Look look, MenuItem item, INameScope scope)
    {
        var icon = new ContentPresenter
        {
            Name = "PART_IconPresenter",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        icon[!ContentPresenter.ContentProperty] = new TemplateBinding(MenuItem.IconProperty);

        var header = new ContentPresenter
        {
            Name = "PART_HeaderPresenter",
            VerticalAlignment = VerticalAlignment.Center,
            RecognizesAccessKey = true,
            Margin = new Thickness(2, 0, 12, 0),
        };
        header[!ContentPresenter.ContentProperty] = new TemplateBinding(HeaderedSelectingItemsControl.HeaderProperty);
        header[!ContentPresenter.ContentTemplateProperty] = new TemplateBinding(HeaderedSelectingItemsControl.HeaderTemplateProperty);

        var gesture = new TextBlock { Name = "PART_InputGestureText", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 4, 0) };
        gesture[!TextBlock.TextProperty] = new TemplateBinding(MenuItem.InputGestureProperty) { Converter = GestureText };
        if (look.ChevronStroked)
            gesture.Foreground = look.Gesture; // Windows 11: shortcuts in grey; XP: same colour as the text

        var chevron = new Path
        {
            Name = "PART_ChevronPath",
            Data = look.Chevron,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (look.ChevronStroked)
        {
            chevron.StrokeThickness = 1.2;
            chevron.StrokeLineCap = PenLineCap.Round;
            chevron.StrokeJoin = PenLineJoin.Round;
            chevron[!Shape.StrokeProperty] = new TemplateBinding(TemplatedControl.ForegroundProperty);
        }
        else
        {
            chevron[!Shape.FillProperty] = new TemplateBinding(TemplatedControl.ForegroundProperty);
        }

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(look.IconColumn, GridUnitType.Pixel),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto) { SharedSizeGroup = "PardehMenuGesture" },
                new ColumnDefinition(look.ChevronColumn, GridUnitType.Pixel),
            },
        };
        Grid.SetColumn(header, 1);
        Grid.SetColumn(gesture, 2);
        Grid.SetColumn(chevron, 3);
        grid.Children.AddRange([icon, header, gesture, chevron]);

        var layout = new Border { Name = "PART_LayoutRoot", Child = grid, MinHeight = look.ItemMinHeight, CornerRadius = look.ItemCorner };
        layout[!Border.BackgroundProperty] = new TemplateBinding(TemplatedControl.BackgroundProperty);
        layout[!Decorator.PaddingProperty] = new TemplateBinding(TemplatedControl.PaddingProperty);

        var popup = new Popup
        {
            Name = "PART_Popup",
            Placement = PlacementMode.RightEdgeAlignedTop,
            HorizontalOffset = look.SubmenuX,
            VerticalOffset = look.SubmenuY,
            IsLightDismissEnabled = false,
            Child = MenuBox(look, scope, false),
        };
        popup[!Popup.IsOpenProperty] = new TemplateBinding(MenuItem.IsSubMenuOpenProperty) { Mode = BindingMode.TwoWay };

        foreach (var named in new Control[] { icon, header, gesture, chevron, layout, popup })
            named.RegisterInNameScope(scope);
        return new Panel { Children = { layout, popup } };
    }
}

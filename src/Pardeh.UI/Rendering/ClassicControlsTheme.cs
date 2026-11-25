using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input.GestureRecognizers;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Path = Avalonia.Controls.Shapes.Path;

namespace Pardeh.UI.Rendering;

/// <summary>
/// XP (Luna) scrollbars and tooltips: 17-pixel blue scrollbars with arrow buttons and a grip on the thumb,
/// and square pale-yellow tooltips. Merged into the app's resources when Appearance › Scrollbars and tooltips is classic.
/// </summary>
internal static class ClassicControlsTheme
{
    private const double Size = 17;

    private static readonly IBrush TrackBrush = new SolidColorBrush(Color.Parse("#F7F6F1"));
    private static readonly IBrush PartBorder = new SolidColorBrush(Color.Parse("#B6C8F2"));
    private static readonly IBrush Arrow = new SolidColorBrush(Color.Parse("#4D6185"));
    private static readonly IBrush Grip = new SolidColorBrush(Color.Parse("#EEF4FE"));
    private static readonly IBrush GripShadow = new SolidColorBrush(Color.Parse("#8CB0F8"));

    private static IBrush PartFace(bool vertical) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = vertical ? new RelativePoint(1, 0, RelativeUnit.Relative) : new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.Parse("#CAD9FC"), 0), new GradientStop(Color.Parse("#C1D3FB"), 0.5),
            new GradientStop(Color.Parse("#B3C8F5"), 1),
        },
    };

    public static ResourceDictionary Create(FontFamily family) => new()
    {
        [typeof(ScrollBar)] = ScrollBarTheme(),
        [typeof(ScrollViewer)] = ScrollViewerTheme(),
        [typeof(ToolTip)] = ToolTipTheme(family),
    };

    private static ControlTheme ToolTipTheme(FontFamily family) => new(typeof(ToolTip))
    {
        Setters =
        {
            new Setter(TemplatedControl.BackgroundProperty, new SolidColorBrush(Color.Parse("#FFFFE1"))),
            new Setter(TemplatedControl.BorderBrushProperty, Brushes.Black),
            new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)),
            new Setter(TemplatedControl.ForegroundProperty, Brushes.Black),
            new Setter(TemplatedControl.FontFamilyProperty, family),
            new Setter(TemplatedControl.FontSizeProperty, 11.0),
            new Setter(TemplatedControl.PaddingProperty, new Thickness(3, 1, 3, 2)),
            new Setter(Layoutable.MaxWidthProperty, 400.0),
            new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<ToolTip>((_, scope) =>
            {
                var content = new ContentPresenter { Name = "PART_ContentPresenter" };
                content[!ContentPresenter.ContentProperty] = new TemplateBinding(ContentControl.ContentProperty);
                content[!ContentPresenter.ContentTemplateProperty] = new TemplateBinding(ContentControl.ContentTemplateProperty);
                content.RegisterInNameScope(scope);
                var border = new Border { Child = content };
                border[!Border.BackgroundProperty] = new TemplateBinding(TemplatedControl.BackgroundProperty);
                border[!Border.BorderBrushProperty] = new TemplateBinding(TemplatedControl.BorderBrushProperty);
                border[!Border.BorderThicknessProperty] = new TemplateBinding(TemplatedControl.BorderThicknessProperty);
                border[!Decorator.PaddingProperty] = new TemplateBinding(TemplatedControl.PaddingProperty);
                return border;
            })),
        },
        Children =
        {
            // Text tooltips wrap like XP's did instead of running off the screen.
            new Style(x => x.Nesting().Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
            {
                Setters = { new Setter(ContentPresenter.TextWrappingProperty, TextWrapping.Wrap) },
            },
        },
    };

    private static ControlTheme ScrollViewerTheme() => new(typeof(ScrollViewer))
    {
        Setters =
        {
            new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<ScrollViewer>((viewer, scope) =>
            {
                // Bars beside the content (not overlaid like Fluent's), with the corner square filled.
                var presenter = new ScrollContentPresenter { Name = "PART_ContentPresenter" };
                presenter[!ContentPresenter.ContentProperty] = new TemplateBinding(ContentControl.ContentProperty);
                presenter[!ContentPresenter.ContentTemplateProperty] = new TemplateBinding(ContentControl.ContentTemplateProperty);
                presenter[!ContentPresenter.BackgroundProperty] = new TemplateBinding(TemplatedControl.BackgroundProperty);
                presenter[!Decorator.PaddingProperty] = new TemplateBinding(TemplatedControl.PaddingProperty);
                if (OperatingSystem.IsAndroid())
                {
                    // Fluent's template carries the touch-drag recognizer on its presenter; without it a finger cannot
                    // scroll (and flings have no inertia). Mouse input is ignored by the recognizer, so this is touch only.
                    var recognizer = new ScrollGestureRecognizer();
                    recognizer.Bind(ScrollGestureRecognizer.CanHorizontallyScrollProperty, new Binding("CanHorizontallyScroll") { Source = presenter });
                    recognizer.Bind(ScrollGestureRecognizer.CanVerticallyScrollProperty, new Binding("CanVerticallyScroll") { Source = presenter });
                    recognizer.Bind(ScrollGestureRecognizer.IsScrollInertiaEnabledProperty, new Binding("IsScrollInertiaEnabled") { Source = viewer });
                    presenter.GestureRecognizers.Add(recognizer);
                }

                var horizontal = new ScrollBar { Name = "PART_HorizontalScrollBar", Orientation = Orientation.Horizontal };
                var vertical = new ScrollBar { Name = "PART_VerticalScrollBar", Orientation = Orientation.Vertical };
                var corner = new Panel { Name = "PART_ScrollBarsSeparator", Background = new SolidColorBrush(Color.Parse("#ECE9D8")) };
                Grid.SetRow(horizontal, 1);
                Grid.SetColumn(vertical, 1);
                Grid.SetRow(corner, 1);
                Grid.SetColumn(corner, 1);
                corner[!Visual.IsVisibleProperty] = new MultiBinding
                {
                    Converter = Avalonia.Data.Converters.BoolConverters.And,
                    Bindings =
                    {
                        new Binding("IsVisible") { Source = horizontal },
                        new Binding("IsVisible") { Source = vertical },
                    },
                };
                foreach (var named in new Control[] { presenter, horizontal, vertical, corner })
                    named.RegisterInNameScope(scope);
                return new Grid
                {
                    ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
                    RowDefinitions = { new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) },
                    Children = { presenter, horizontal, vertical, corner },
                };
            })),
        },
    };

    private static ControlTheme ScrollBarTheme() => new(typeof(ScrollBar))
    {
        Setters =
        {
            new Setter(ScrollBar.AllowAutoHideProperty, false),
            new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<ScrollBar>(ScrollBarTemplate)),
        },
        Children =
        {
            new Style(x => x.Nesting().Class(":vertical")) { Setters = { new Setter(Layoutable.WidthProperty, Size) } },
            new Style(x => x.Nesting().Class(":horizontal")) { Setters = { new Setter(Layoutable.HeightProperty, Size) } },
        },
    };

    private static Control ScrollBarTemplate(ScrollBar bar, INameScope scope)
    {
        var vertical = bar.Orientation == Orientation.Vertical;

        var thumb = new Thumb { Name = "thumb", Template = new FuncControlTemplate<Thumb>((_, _) => ThumbVisual(vertical)) };
        var track = new Track
        {
            Name = "PART_Track",
            Orientation = bar.Orientation,
            IsDirectionReversed = vertical,
            Thumb = thumb,
            DecreaseButton = PageButton("PART_PageUpButton"),
            IncreaseButton = PageButton("PART_PageDownButton"),
        };
        track[!Track.MinimumProperty] = new TemplateBinding(RangeBase.MinimumProperty);
        track[!Track.MaximumProperty] = new TemplateBinding(RangeBase.MaximumProperty);
        track[!Track.ValueProperty] = new TemplateBinding(RangeBase.ValueProperty) { Mode = BindingMode.TwoWay };
        track[!Track.ViewportSizeProperty] = new TemplateBinding(ScrollBar.ViewportSizeProperty);

        // Arrows point up / down (vertical) or left / right (horizontal).
        var up = LineButton("PART_LineUpButton", vertical ? "M0,4 L3.5,0 L7,4 L5.6,4 L3.5,1.6 L1.4,4 Z" : "M4,0 L0,3.5 L4,7 L4,5.6 L1.6,3.5 L4,1.4 Z", vertical);
        var down = LineButton("PART_LineDownButton", vertical ? "M0,0 L3.5,4 L7,0 L5.6,0 L3.5,2.4 L1.4,0 Z" : "M0,0 L4,3.5 L0,7 L0,5.6 L2.4,3.5 L0,1.4 Z", vertical);

        foreach (var named in new Control[] { track, up, down })
            named.RegisterInNameScope(scope);

        var grid = new Grid { Background = TrackBrush };
        if (vertical)
        {
            grid.RowDefinitions.AddRange([new RowDefinition(Size, GridUnitType.Pixel), new RowDefinition(GridLength.Star), new RowDefinition(Size, GridUnitType.Pixel)]);
            Grid.SetRow(track, 1);
            Grid.SetRow(down, 2);
        }
        else
        {
            grid.ColumnDefinitions.AddRange([new ColumnDefinition(Size, GridUnitType.Pixel), new ColumnDefinition(GridLength.Star), new ColumnDefinition(Size, GridUnitType.Pixel)]);
            Grid.SetColumn(track, 1);
            Grid.SetColumn(down, 2);
        }

        grid.Children.AddRange([up, track, down]);
        return grid;
    }

    private static RepeatButton PageButton(string name) => new()
    {
        Name = name,
        Focusable = false,
        Template = new FuncControlTemplate<RepeatButton>((_, _) => new Border { Background = Brushes.Transparent }),
    };

    private static RepeatButton LineButton(string name, string arrow, bool vertical) => new()
    {
        Name = name,
        Focusable = false,
        Template = new FuncControlTemplate<RepeatButton>((_, _) => new Border
        {
            Margin = new Thickness(1),
            Background = PartFace(vertical),
            BorderBrush = PartBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Child = new Path
            {
                Data = Geometry.Parse(arrow),
                Fill = Arrow,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        }),
    };

    private static Control ThumbVisual(bool vertical)
    {
        // The XP grip: four light lines with a blue shadow across the middle of the thumb.
        var grip = new StackPanel
        {
            Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 0,
        };
        for (var i = 0; i < 4; i++)
        {
            grip.Children.Add(vertical
                ? new Border { Width = 7, Height = 1, Background = Grip }
                : new Border { Width = 1, Height = 7, Background = Grip });
            grip.Children.Add(vertical
                ? new Border { Width = 7, Height = 1, Background = GripShadow }
                : new Border { Width = 1, Height = 7, Background = GripShadow });
        }

        return new Border
        {
            Margin = new Thickness(1),
            Background = PartFace(vertical),
            BorderBrush = PartBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            MinHeight = vertical ? 8 : 0,
            MinWidth = vertical ? 0 : 8,
            Child = grip,
        };
    }
}

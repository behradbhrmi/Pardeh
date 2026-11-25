using Avalonia;
using Avalonia.Themes.Fluent;

namespace Pardeh.UI;

/// <summary>The Avalonia application every head shares (name, theme); heads add their own lifetime handling.</summary>
public abstract class PardehApplicationBase : Application
{
    public override void Initialize()
    {
        Name = "Pardeh";
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
    }
}

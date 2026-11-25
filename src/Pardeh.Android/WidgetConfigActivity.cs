using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;

namespace Pardeh.Android;

/// <summary>Customize screen of a Pardeh widget: opens when a widget is placed and when it is reconfigured (long-press the widget). Plain Android views, no Avalonia.</summary>
[Activity(Name = "org.pardeh.player.WidgetConfigActivity", Label = "Customize widget", Exported = true, Theme = "@android:style/Theme.Material.NoActionBar")]
[IntentFilter(new[] { "android.appwidget.action.APPWIDGET_CONFIGURE" })]
public sealed class WidgetConfigActivity : Activity
{
    private int _id = AppWidgetManager.InvalidAppwidgetId;
    private WidgetConfig _cfg = new();

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetResult(Result.Canceled); // backing out of the first configuration must not add the widget
        _id = Intent?.GetIntExtra(AppWidgetManager.ExtraAppwidgetId, AppWidgetManager.InvalidAppwidgetId) ?? AppWidgetManager.InvalidAppwidgetId;
        if (_id == AppWidgetManager.InvalidAppwidgetId)
        {
            Finish();
            return;
        }

        _cfg = WidgetConfig.Load(this, _id);
        var d = Resources!.DisplayMetrics!.Density;
        int Dp(int v) => (int)(v * d);

        var panel = new LinearLayout(this) { Orientation = Orientation.Vertical };
        panel.SetPadding(Dp(20), Dp(16), Dp(20), Dp(24));

        TextView Heading(string t)
        {
            var v = new TextView(this) { Text = t, TextSize = 14 };
            v.SetPadding(0, Dp(18), 0, Dp(4));
            v.SetTypeface(v.Typeface, global::Android.Graphics.TypefaceStyle.Bold);
            return v;
        }

        Spinner Choice(string[] names, int selected, Action<int> set)
        {
            var spinner = new Spinner(this) { Adapter = new ArrayAdapter<string>(this, global::Android.Resource.Layout.SimpleSpinnerDropDownItem, names) };
            spinner.SetSelection(selected);
            spinner.ItemSelected += (_, e) => set(e.Position);
            return spinner;
        }

        Switch Toggle(string label, bool value, Action<bool> set)
        {
            var sw = new Switch(this) { Text = label, Checked = value };
            sw.SetPadding(0, Dp(8), 0, Dp(8));
            sw.CheckedChange += (_, e) => set(e.IsChecked);
            return sw;
        }

        var title = new TextView(this) { Text = "Customize widget", TextSize = 22 };
        panel.AddView(title);
        panel.AddView(Heading("Look"));
        panel.AddView(Choice(WidgetConfig.ThemeNames, _cfg.Theme, i => _cfg.Theme = i));
        var opacityLabel = new TextView(this) { Text = $"Background opacity: {_cfg.Opacity}%" };
        opacityLabel.SetPadding(0, Dp(12), 0, 0);
        panel.AddView(opacityLabel);
        var opacity = new SeekBar(this) { Max = 80, Progress = _cfg.Opacity - 20 };
        opacity.ProgressChanged += (_, e) =>
        {
            _cfg.Opacity = e.Progress + 20;
            opacityLabel.Text = $"Background opacity: {_cfg.Opacity}%";
        };
        panel.AddView(opacity);
        var textLabel = new TextView(this) { Text = "Text size" };
        textLabel.SetPadding(0, Dp(12), 0, 0);
        panel.AddView(textLabel);
        panel.AddView(Choice(WidgetConfig.TextNames, _cfg.TextSize, i => _cfg.TextSize = i));

        panel.AddView(Heading("Show"));
        panel.AddView(Toggle("Cover", _cfg.Cover, v => _cfg.Cover = v));
        panel.AddView(Toggle("Seekbar and time (tap the bar to jump)", _cfg.Seekbar, v => _cfg.Seekbar = v));
        panel.AddView(Toggle("Back / forward 10 seconds", _cfg.Skip, v => _cfg.Skip = v));
        panel.AddView(Toggle("Shuffle", _cfg.Shuffle, v => _cfg.Shuffle = v));
        panel.AddView(Toggle("Repeat", _cfg.Repeat, v => _cfg.Repeat = v));
        panel.AddView(Toggle("Favourite heart", _cfg.Favorite, v => _cfg.Favorite = v));

        var hint = new TextView(this) { Text = "Make the widget larger to show the seekbar; a small widget hides parts to fit. Long-press the widget to customize it again.", TextSize = 12 };
        hint.SetPadding(0, Dp(16), 0, Dp(8));
        panel.AddView(hint);

        var done = new Button(this) { Text = "Done" };
        done.Click += (_, _) => Save();
        panel.AddView(done);

        var scroll = new ScrollView(this);
        // Edge-to-edge (Android 15+): keep the content clear of the status and navigation bars.
        scroll.SetOnApplyWindowInsetsListener(new InsetsPadding());
        scroll.AddView(panel);
        SetContentView(scroll);
    }

    private void Save()
    {
        _cfg.Save(this, _id);
        PardehWidget.Refresh(this, _id);
        SetResult(Result.Ok, new Intent().PutExtra(AppWidgetManager.ExtraAppwidgetId, _id));
        Finish();
    }
}

/// <summary>Pads a view by the system bars so nothing hides under them.</summary>
internal sealed class InsetsPadding : Java.Lang.Object, View.IOnApplyWindowInsetsListener
{
    public WindowInsets? OnApplyWindowInsets(View? v, WindowInsets? insets)
    {
        if (v is not null && insets is not null)
            v.SetPadding(insets.SystemWindowInsetLeft, insets.SystemWindowInsetTop, insets.SystemWindowInsetRight, insets.SystemWindowInsetBottom);
        return insets;
    }
}

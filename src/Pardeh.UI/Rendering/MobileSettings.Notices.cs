using Avalonia.Controls;

namespace Pardeh.UI.Rendering;

/// <summary>The phone settings page for on-screen messages.</summary>
internal sealed partial class MobileSettingsView
{
    private Control NoticesPage()
    {
        void Set(Action change)
        {
            change();
            _settings.Save();
        }

        return new StackPanel
        {
            Children =
            {
                Note("A short note over the player after a command: the volume, the time after a skip, the audio track and so on."),
                Card(
                    Toggle("Show on-screen messages", null, _settings.OnScreenMessages, v => Set(() => _settings.OnScreenMessages = v)),
                    Choice("Place", "Over the video or visualization", NoticeChoices.PlaceNames,
                        () => NoticeChoices.IndexOf(NoticeChoices.PlaceKeys, _settings.NoticePlace), i => Set(() => _settings.NoticePlace = NoticeChoices.PlaceKeys[i])),
                    Choice("Time on screen", null, NoticeChoices.TimeKeys,
                        () => NoticeChoices.IndexOf(NoticeChoices.TimeKeys, _settings.NoticeTime, 1), i => Set(() => _settings.NoticeTime = NoticeChoices.TimeKeys[i])),
                    Choice("Title of a new track", null, NoticeChoices.SongTitleNames,
                        () => NoticeChoices.IndexOf(NoticeChoices.SongTitleKeys, _settings.NoticeSongTitle), i => Set(() => _settings.NoticeSongTitle = NoticeChoices.SongTitleKeys[i]))),
                Heading("Which messages"),
                Card([.. NoticeChoices.Groups.Select(g => Toggle(g.Name, g.Description, !_settings.HiddenNotices.Contains(g.Group.ToString()),
                    v => NoticeChoices.SetGroup(_settings, g.Group, v)))]),
            },
        };
    }
}

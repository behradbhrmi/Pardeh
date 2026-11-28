using Avalonia.Controls;
using Pardeh.Core.Notices;

namespace Pardeh.UI.Rendering;

/// <summary>The choices for on-screen messages, shared by the desktop options and the phone settings.</summary>
internal static class NoticeChoices
{
    public static readonly string[] PlaceKeys = ["Top", "TopLeft", "TopRight", "Centre", "Bottom"];
    public static readonly string[] PlaceNames = ["Top", "Top left", "Top right", "Centre", "Bottom"];
    public static readonly string[] TimeKeys = ["Short", "Normal", "Long"];
    public static readonly string[] SongTitleKeys = [nameof(SongTitleNotices.FullScreenAndVideo), nameof(SongTitleNotices.Always), nameof(SongTitleNotices.Never)];
    public static readonly string[] SongTitleNames = ["In full screen and with video", "Always", "Never"];

    public static readonly (NoticeCategory Group, string Name, string Description)[] Groups =
    [
        (NoticeCategory.Position, "Position", "Skipping, the seek bar and resuming: time, time left and a progress bar"),
        (NoticeCategory.Playback, "Playback", "Play and pause, speed, shuffle, repeat, A-B repeat and the sleep timer"),
        (NoticeCategory.Sound, "Sound", "Volume, mute, balance and the equalizer"),
        (NoticeCategory.Tracks, "Audio and subtitles", "The audio track, subtitles and their timing"),
        (NoticeCategory.Picture, "Picture and window", "Aspect ratio, picture settings, full screen and skin size"),
        (NoticeCategory.Library, "Library", "The queue, favourites, markers and bookmarks, and finished file work"),
    ];

    public static int IndexOf(string[] keys, string value, int fallback = 0) => Array.IndexOf(keys, value) is >= 0 and var i ? i : fallback;

    public static void SetGroup(AppSettings settings, NoticeCategory group, bool shown)
    {
        settings.HiddenNotices.Remove(group.ToString());
        if (!shown)
            settings.HiddenNotices.Add(group.ToString());
        settings.Save();
    }
}

internal static partial class OptionsDialog
{
    private sealed partial class SettingsBuilder
    {
        /// <summary>The "On-screen messages" part of the Playback page.</summary>
        private Control[] NoticeRows()
        {
            void Set(Action change)
            {
                change();
                _settings.Save();
            }

            var rows = new List<Control>
            {
                Section("On-screen messages"),
                Toggle("Show on-screen messages", "A short note over the player after a command: the volume, the time after a skip, the audio track and so on",
                    _settings.OnScreenMessages, v => Set(() => _settings.OnScreenMessages = v)),
                Row("Place", "Over the video or visualization",
                    Choice(NoticeChoices.PlaceNames, NoticeChoices.IndexOf(NoticeChoices.PlaceKeys, _settings.NoticePlace),
                        i => Set(() => _settings.NoticePlace = NoticeChoices.PlaceKeys[i]), 140)),
                Row("Time on screen", null,
                    Choice(NoticeChoices.TimeKeys, NoticeChoices.IndexOf(NoticeChoices.TimeKeys, _settings.NoticeTime, 1),
                        i => Set(() => _settings.NoticeTime = NoticeChoices.TimeKeys[i]), 140)),
                Row("Title of a new track", "With the artist, the album and its place in the queue",
                    Choice(NoticeChoices.SongTitleNames, NoticeChoices.IndexOf(NoticeChoices.SongTitleKeys, _settings.NoticeSongTitle),
                        i => Set(() => _settings.NoticeSongTitle = NoticeChoices.SongTitleKeys[i]))),
            };
            rows.AddRange(NoticeChoices.Groups.Select(g => Toggle(g.Name + " messages", g.Description,
                !_settings.HiddenNotices.Contains(g.Group.ToString()), v => NoticeChoices.SetGroup(_settings, g.Group, v))));
            return [.. rows];
        }
    }
}

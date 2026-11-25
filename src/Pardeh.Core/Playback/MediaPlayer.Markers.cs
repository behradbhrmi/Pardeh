using Pardeh.Core.Library;

namespace Pardeh.Core.Playback;

/// <summary>Named markers inside the playing track ("chorus 1:12"): adding, jumping, and A-B repeat between them.</summary>
public sealed partial class MediaPlayer
{
    /// <summary>A mark set within this many seconds of a marker lands on the marker (A and B of a repeated section).</summary>
    public const double MarkerSnapSeconds = 1.5;

    /// <summary>Where markers are kept (null: no markers). The head sets it.</summary>
    public BookmarkStore? Bookmarks { get; set; }

    /// <summary>The playing track's markers, ascending.</summary>
    public IReadOnlyList<Marker> CurrentMarkers => CurrentMedia is { } media && Bookmarks is { } store ? store.GetMarkers(media.Source) : [];

    /// <summary>What a marker is called: its name, else "Marker n".</summary>
    public static string MarkerLabel(Marker marker, int index) => marker.Name.Length > 0 ? marker.Name : $"Marker {index + 1}";

    /// <summary>Marks <paramref name="at"/> (default: the current position). Returns the marker's index, or -1 when nothing is open.</summary>
    public int AddMarker(string name = "", double? at = null)
    {
        if (!_engine.IsOpen || CurrentMedia is not { } media || Bookmarks is not { } store || RefuseSecret(media))
            return -1;
        var seconds = Math.Round(Math.Max(0, at ?? CurrentPosition), 1);
        var index = store.Add(media.Source, seconds, name);
        SetStatus($"{MarkerLabel(store.GetMarkers(media.Source)[index], index)} at {FormatTime(seconds)}");
        return index;
    }

    public void RenameMarker(int index, string name)
    {
        if (CurrentMedia is { } media)
            Bookmarks?.Rename(media.Source, index, name);
    }

    public void RemoveMarker(int index)
    {
        if (CurrentMedia is { } media)
            Bookmarks?.RemoveAt(media.Source, index);
    }

    public bool JumpToMarker(int index)
    {
        var markers = CurrentMarkers;
        if (!_engine.IsOpen || index < 0 || index >= markers.Count)
            return false;
        // Leaving the repeated section on purpose ends the repeat; otherwise the loop would pull the position straight back.
        if (SectionLoopActive && (markers[index].Seconds < LoopStart - 0.5 || markers[index].Seconds >= LoopEnd))
            ClearSectionLoop();
        CurrentPosition = markers[index].Seconds;
        SetStatus($"{MarkerLabel(markers[index], index)} ({FormatTime(markers[index].Seconds)})");
        return true;
    }

    /// <summary>
    /// The next (<paramref name="direction"/> &gt; 0) or previous marker from the current position. Going back from just after a
    /// marker goes to the one before it, as the previous-track button does.
    /// </summary>
    public bool StepMarker(int direction)
    {
        var markers = CurrentMarkers;
        var now = CurrentPosition;
        var index = -1;
        for (var i = 0; i < markers.Count; i++)
        {
            if (direction > 0 && markers[i].Seconds > now + 0.5)
            {
                index = i;
                break;
            }

            if (direction < 0 && markers[i].Seconds < now - MarkerSnapSeconds)
                index = i;
        }

        return index >= 0 && JumpToMarker(index);
    }

    /// <summary>Repeats from marker <paramref name="index"/> to the next one (to the end of the track after the last), from its start.</summary>
    public bool LoopMarkerSection(int index)
    {
        var markers = CurrentMarkers;
        if (!_engine.IsOpen || index < 0 || index >= markers.Count)
            return false;
        var a = markers[index].Seconds;
        var next = markers.Skip(index + 1).FirstOrDefault(m => m.Seconds >= a + 0.5);
        // A little before the end: at the very end the track would finish before the loop could turn back.
        var b = next?.Seconds ?? Duration - 0.3;
        if (b < a + 0.5)
            return false;
        LoopStart = a;
        LoopEnd = b;
        CurrentPosition = a;
        SetStatus($"Repeating {MarkerLabel(markers[index], index)}: {FormatTime(a)} to {FormatTime(b)}");
        SectionLoopChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Makes marker <paramref name="index"/> the A (start) or B (end) of the repeated section. False when B would not follow A.</summary>
    public bool SetSectionPointAtMarker(int index, bool end)
    {
        var markers = CurrentMarkers;
        if (!_engine.IsOpen || index < 0 || index >= markers.Count)
            return false;
        return end ? SetSectionEndAt(markers[index].Seconds) : SetSectionStartAt(markers[index].Seconds);
    }

    /// <summary>The position, moved onto a marker that is close by.</summary>
    private double Snapped(double seconds)
    {
        var near = CurrentMarkers.OrderBy(m => Math.Abs(m.Seconds - seconds)).FirstOrDefault();
        return near is not null && Math.Abs(near.Seconds - seconds) <= MarkerSnapSeconds ? near.Seconds : seconds;
    }
}

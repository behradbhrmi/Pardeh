using System.Security.Cryptography;
using System.Text;

namespace Pardeh.Nava;

/// <summary>One ID3v2 frame as it is in the file.</summary>
internal sealed record RawFrame(string Id, byte[] Body, bool Encoded);

/// <summary>
/// Reads the frames of a file's ID3v2 tag without TagLib#. It is the second opinion on what a write kept: TagLib# empties
/// user web address frames (WXXX) when it saves, and a comparison of its own two readings does not notice that.
/// </summary>
internal static class RawId3
{
    private const int MaxTag = 64 * 1024 * 1024;

    /// <summary>The frames, an empty list without a tag, or null for a tag this reader can't take apart (version 2.2).</summary>
    public static List<RawFrame>? Read(Stream stream)
    {
        if (!FindTag(stream, out var start))
            return [];
        stream.Position = start;
        var header = new byte[10];
        if (!Fill(stream, header))
            return [];
        var version = header[3];
        if (version is < 3 or > 4)
            return null;
        var size = Syncsafe(header, 6);
        if (size <= 0 || size > MaxTag || size > stream.Length - stream.Position)
            return [];
        var data = new byte[size];
        if (!Fill(stream, data))
            return [];
        // Version 2.3 undoes "unsynchronisation" over the whole tag, 2.4 frame by frame.
        if (version == 3 && (header[5] & 0x80) != 0)
            data = Resynchronise(data);

        var at = 0;
        if ((header[5] & 0x40) != 0 && data.Length >= 4)
            at = version == 4 ? Syncsafe(data, 0) : Plain(data, 0) + 4;

        var frames = new List<RawFrame>();
        while (at >= 0 && at + 10 <= data.Length && data[at] != 0)
        {
            var id = Encoding.Latin1.GetString(data, at, 4);
            var length = version == 4 ? Syncsafe(data, at + 4) : Plain(data, at + 4);
            var format = data[at + 9];
            at += 10;
            if (length < 0 || length > data.Length - at || !id.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9'))
                break;
            var body = data[at..(at + length)];
            at += length;
            if (version == 4 && (format & 0x02) != 0)
                body = Resynchronise(body);
            // Compressed, encrypted or otherwise wrapped bodies are compared as they are.
            var encoded = version == 4 ? (format & 0x4D) != 0 : (format & 0xE0) != 0;
            frames.Add(new RawFrame(id, body, encoded));
        }

        return frames;
    }

    private static bool Fill(Stream stream, byte[] buffer) => stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false) == buffer.Length;

    private static bool FindTag(Stream stream, out long start)
    {
        start = 0;
        stream.Position = 0;
        var head = new byte[12];
        if (!Fill(stream, head))
            return false;
        if (head.AsSpan(0, 3).SequenceEqual("ID3"u8))
            return true;
        var riff = head.AsSpan(0, 4).SequenceEqual("RIFF"u8);
        if (!riff && !head.AsSpan(0, 4).SequenceEqual("FORM"u8))
            return false;
        // WAV and AIFF keep the tag in a chunk of its own.
        var chunk = new byte[8];
        long position = 12;
        while (position + 8 <= stream.Length)
        {
            stream.Position = position;
            if (!Fill(stream, chunk))
                return false;
            var length = riff ? BitConverter.ToUInt32(chunk, 4) : (uint)Plain(chunk, 4);
            if (Encoding.Latin1.GetString(chunk, 0, 4).Equals("ID3 ", StringComparison.OrdinalIgnoreCase))
            {
                start = position + 8;
                return true;
            }

            position += 8 + length + (length & 1);
        }

        return false;
    }

    private static int Syncsafe(byte[] data, int at) => (data[at] & 127) << 21 | (data[at + 1] & 127) << 14 | (data[at + 2] & 127) << 7 | (data[at + 3] & 127);

    private static int Plain(byte[] data, int at) => data[at] << 24 | data[at + 1] << 16 | data[at + 2] << 8 | data[at + 3];

    private static byte[] Resynchronise(byte[] data)
    {
        var result = new List<byte>(data.Length);
        for (var i = 0; i < data.Length; i++)
        {
            result.Add(data[i]);
            if (data[i] == 0xFF && i + 1 < data.Length && data[i + 1] == 0)
                i++;
        }

        return [.. result];
    }

    // ---- text in a frame ------------------------------------------------------------------------

    private static Encoding EncodingOf(byte kind) => kind switch
    {
        1 => Encoding.Unicode,
        2 => Encoding.BigEndianUnicode,
        3 => Encoding.UTF8,
        _ => Encoding.Latin1,
    };

    private static string Decode(byte kind, ReadOnlySpan<byte> data)
    {
        var encoding = EncodingOf(kind);
        if (kind == 1 && data.Length >= 2)
        {
            if (data[0] == 0xFE && data[1] == 0xFF)
            {
                encoding = Encoding.BigEndianUnicode;
                data = data[2..];
            }
            else if (data[0] == 0xFF && data[1] == 0xFE)
                data = data[2..];
        }

        return encoding.GetString(data).TrimStart('﻿');
    }

    // Splits at the first terminator of the encoding (two zero bytes, on an even place, for UTF-16).
    private static (string First, int RestAt) Terminated(byte kind, byte[] body, int from)
    {
        var wide = kind is 1 or 2;
        for (var i = from; i < body.Length; i += wide ? 2 : 1)
        {
            if (body[i] == 0 && (!wide || i + 1 >= body.Length || body[i + 1] == 0))
                return (Decode(kind, body.AsSpan(from, i - from)), Math.Min(body.Length, i + (wide ? 2 : 1)));
        }

        return (Decode(kind, body.AsSpan(from)), body.Length);
    }

    private static string Values(byte kind, byte[] body, int from)
    {
        var values = new List<string>();
        while (from < body.Length)
        {
            var (value, next) = Terminated(kind, body, from);
            values.Add(value);
            from = next;
        }

        while (values.Count > 0 && values[^1].Length == 0)
            values.RemoveAt(values.Count - 1);
        return string.Join('\u001f', values);
    }

    /// <summary>
    /// The release date as "yyyy", or "yyyy-MM-dd" when day and month are known, whether the tag says it as one frame
    /// (TDRC, version 2.4) or as year and day-month (TYER and TDAT "ddMM", version 2.3). Empty without a date.
    /// </summary>
    public static string Date(IReadOnlyList<RawFrame> frames)
    {
        string Text(string id) => frames.FirstOrDefault(f => f.Id == id) is { } frame ? Describe(frame).Content ?? string.Empty : string.Empty;
        if (Text("TDRC") is { Length: > 0 } recorded)
            return recorded;
        var year = Text("TYER");
        var day = Text("TDAT");
        return day.Length == 4 && year.Length == 4 ? $"{year}-{day[2..]}-{day[..2]}" : year;
    }

    /// <summary>A user web address frame: its description and address.</summary>
    public static (string Description, string Url) UserUrl(RawFrame frame)
    {
        if (frame.Encoded || frame.Body.Length < 1)
            return (string.Empty, string.Empty);
        var (description, at) = Terminated(frame.Body[0], frame.Body, 1);
        return (description, Encoding.Latin1.GetString(frame.Body, at, frame.Body.Length - at).TrimEnd('\0'));
    }

    public static byte[] UserUrlBody(string description, string url) =>
        [3, .. Encoding.UTF8.GetBytes(description), 0, .. Encoding.Latin1.GetBytes(url)];

    private static string Digest(ReadOnlySpan<byte> data) => Convert.ToHexString(SHA256.HashData(data));

    /// <summary>
    /// The id a frame is changed under (see <see cref="TagTexts"/>), and what it says, however it happens to be encoded.
    /// Null content: the frame says nothing, and may be left out.
    /// </summary>
    public static (string Key, string? Content) Describe(RawFrame frame)
    {
        var (id, body) = (frame.Id, frame.Body);
        if (frame.Encoded || body.Length == 0)
            return ("id3v2:" + id, body.Length == 0 ? null : Digest(body));

        if (id == "WXXX")
        {
            var (description, url) = UserUrl(frame);
            return ("id3v2:WXXX:" + description, url.Length + description.Length == 0 ? null : url);
        }

        if (id == "POPM")
        {
            // Who rated, the stars and the play count: the count may be written in fewer or more bytes.
            var (user, at) = Terminated(0, body, 0);
            var count = 0UL;
            foreach (var digit in body.AsSpan(Math.Min(body.Length, at + 1)))
                count = (count << 8) | digit;
            return ("id3v2:POPM", $"{user}|{(at < body.Length ? body[at] : 0)}|{count}");
        }

        var kind = body[0];
        if (id == "TXXX")
        {
            var (description, at) = Terminated(kind, body, 1);
            var value = Values(kind, body, at);
            return ("id3v2:TXXX:" + description, value.Length == 0 ? null : value);
        }

        if (id[0] == 'T')
        {
            var value = Values(kind, body, 1);
            return ("id3v2:" + id, value.Length == 0 ? null : value);
        }

        if (id is "COMM" or "USLT" && body.Length >= 4)
        {
            var language = Encoding.Latin1.GetString(body, 1, 3);
            var (description, at) = Terminated(kind, body, 4);
            var value = Values(kind, body, at);
            return ($"id3v2:{id}:{language}:{description}", value.Length == 0 ? null : value);
        }

        if (id == "APIC")
        {
            var (mime, at) = Terminated(0, body, 1);
            if (at < body.Length)
            {
                var type = body[at];
                var (description, dataAt) = Terminated(kind, body, at + 1);
                return ("id3v2:APIC", $"{mime}|{type}|{description}|{Digest(body.AsSpan(dataAt))}");
            }
        }

        return ("id3v2:" + id, id[0] == 'W' ? Encoding.Latin1.GetString(body).TrimEnd('\0') : Digest(body));
    }
}

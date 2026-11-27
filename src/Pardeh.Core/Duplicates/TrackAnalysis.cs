using Pardeh.Core.Changes;

namespace Pardeh.Core.Duplicates;

/// <summary>What a read through a file's sound says about it.</summary>
/// <param name="Digest">A hash of the compressed sound without tags: equal digests are the same recording byte for byte.</param>
/// <param name="Seconds">How much sound is really there (0 when the format does not say per packet).</param>
/// <param name="DeclaredSeconds">How long the file says it is (0 when it does not say).</param>
public sealed record AudioScan(string Digest, double Seconds, double DeclaredSeconds, string Codec, bool Lossless, int SampleRate)
{
    /// <summary>The file has moving pictures (a cover picture does not count).</summary>
    public bool HasVideo { get; init; }

    /// <summary>Less sound than the file says it has: a download that stopped early.</summary>
    public bool CutShort => Seconds > 0 && DeclaredSeconds > 0 && DeclaredSeconds - Seconds > Math.Max(3, DeclaredSeconds * 0.03);
}

/// <summary>What is read from a file's start and tags.</summary>
/// <param name="Tags">Its tags, without the picture data.</param>
/// <param name="CoverBytes">The size of the first embedded picture, or 0.</param>
/// <param name="Seconds">Its length as the tag reader has it.</param>
/// <param name="Kbps">The bitrate the file states, or 0.</param>
/// <param name="Start">A digest of the first stretch of sound: files with different ones are not the same audio.</param>
public sealed record AudioFacts(string FileName, TagFields Tags, string? CoverHash, int CoverBytes, double Seconds, int Kbps, string? Start);

/// <summary>Reads files for the duplicate check, from any kind of source.</summary>
public interface IDuplicateAnalyzer
{
    /// <summary>Null when the file can't be read.</summary>
    AudioFacts? Read(string source);

    /// <summary>Null when the sound can't be read to its end.</summary>
    AudioScan? Scan(string source, CancellationToken cancel);

    /// <summary>A hash of every byte of the file, or null.</summary>
    string? ContentHash(string source, CancellationToken cancel);
}

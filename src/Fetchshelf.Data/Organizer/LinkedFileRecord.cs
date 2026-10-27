namespace Fetchshelf.Data.Organizer;

/// <summary>One file linked into a torrent's library folder; the shape stored in `TorrentEntity.LinkedFilesJson`.</summary>
public sealed record LinkedFileRecord(string RelativePath, long SizeBytes);

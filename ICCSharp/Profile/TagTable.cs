using System;
using System.Collections.Generic;
using ICCSharp.IO;

namespace ICCSharp.Profile;

/// <summary>
/// Tag table for an ICC profile (ICC.1:2010 §7.3). Lives immediately after the 128-byte header.
/// Preserves entries in file order and additionally indexes them by signature; if a profile
/// declares the same signature twice (illegal per spec but seen in the wild) the first wins
/// in the index but both remain in <see cref="Entries"/>.
/// </summary>
public sealed class TagTable
{
    private readonly Dictionary<uint, TagDirectoryEntry> _bySignature;

    public IReadOnlyList<TagDirectoryEntry> Entries { get; }

    private TagTable(IReadOnlyList<TagDirectoryEntry> entries, Dictionary<uint, TagDirectoryEntry> bySignature)
    {
        Entries = entries;
        _bySignature = bySignature;
    }

    public int Count => Entries.Count;

    public bool TryGet(IccSignature signature, out TagDirectoryEntry entry)
        => _bySignature.TryGetValue(signature.Value, out entry);

    public bool Contains(IccSignature signature) => _bySignature.ContainsKey(signature.Value);

    public TagDirectoryEntry this[IccSignature signature]
    {
        get
        {
            if (!_bySignature.TryGetValue(signature.Value, out TagDirectoryEntry e))
                throw new KeyNotFoundException($"Tag '{signature}' not present in profile.");
            return e;
        }
    }

    /// <summary>
    /// Parses the tag table from a reader positioned at offset 128 of a profile.
    /// <paramref name="profileSize"/> is used to validate that each entry's data range
    /// stays within the profile.
    /// </summary>
    public static TagTable Parse(IccBinaryReader reader, uint profileSize)
    {
        if (reader is null) throw new ArgumentNullException(nameof(reader));
        if (reader.Remaining < 4)
            throw new IccParseException("Tag table truncated before tag count.");

        uint count = reader.ReadUInt32();

        // Sanity: tagCount × 12 bytes must fit in remaining buffer.
        long bytesNeeded = (long)count * 12L;
        if (bytesNeeded > reader.Remaining)
            throw new IccParseException(
                $"Tag count {count} requires {bytesNeeded} bytes but only {reader.Remaining} remain.");

        List<TagDirectoryEntry> entries = new((int)Math.Min(count, 1024));
        Dictionary<uint, TagDirectoryEntry> bySig = new();

        for (uint i = 0; i < count; i++)
        {
            IccSignature sig = reader.ReadSignature();
            uint offset = reader.ReadUInt32();
            uint size = reader.ReadUInt32();

            if (offset < ProfileHeader.Size)
                throw new IccParseException(
                    $"Tag #{i} '{sig}' offset {offset} lies inside the 128-byte header.");

            long end = (long)offset + size;
            if (end > profileSize)
                throw new IccParseException(
                    $"Tag #{i} '{sig}' (offset {offset}, size {size}) extends past profile end {profileSize}.");

            TagDirectoryEntry entry = new(sig, offset, size);
            entries.Add(entry);
            // First occurrence wins for index lookup; duplicates are preserved in Entries.
            if (!bySig.ContainsKey(sig.Value))
                bySig.Add(sig.Value, entry);
        }

        return new TagTable(entries, bySig);
    }
}

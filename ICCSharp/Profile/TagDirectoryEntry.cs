using ICCSharp.IO;

namespace ICCSharp.Profile;

/// <summary>
/// A single entry in the tag table (ICC.1:2010 §7.3). The offset is absolute from the
/// start of the profile; multiple entries may reference the same offset (tag aliasing).
/// </summary>
public readonly record struct TagDirectoryEntry(IccSignature Signature, uint Offset, uint Size);

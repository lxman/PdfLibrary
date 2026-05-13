using System;

namespace Jp2Codec.Jp2File
{
    /// <summary>
    /// JP2 channel definition box (ISO/IEC 15444-1 I.5.3.6, "cdef"). Maps each
    /// codestream component (Cn) to a colour-channel association (Asoc), so
    /// downstream colour conversion can find Y / Cb / Cr (or R / G / B,
    /// or palette columns) regardless of the order they appear in the
    /// codestream.
    ///
    /// <para>
    /// Asoc values per Table I.18:
    /// </para>
    /// <list type="bullet">
    ///   <item><c>0</c> — unspecified.</item>
    ///   <item><c>1, 2, 3</c> — for sRGB: R, G, B. For sYCC: Y, Cb, Cr. For sGreyScale: Y.</item>
    ///   <item><c>0xFFFF</c> — channel does not contribute to a colour.</item>
    /// </list>
    /// </summary>
    internal sealed class JpChannelDefinition
    {
        /// <summary>Per-entry codestream component index (Cn).</summary>
        public ushort[] ComponentIndex { get; }

        /// <summary>Per-entry channel type (Typ). 0 = colour, 1 = opacity, 2 = pre-multiplied opacity.</summary>
        public ushort[] ChannelType { get; }

        /// <summary>Per-entry colour-channel association (Asoc).</summary>
        public ushort[] Association { get; }

        public JpChannelDefinition(ushort[] componentIndex, ushort[] channelType, ushort[] association)
        {
            if (componentIndex is null) throw new ArgumentNullException(nameof(componentIndex));
            if (channelType is null) throw new ArgumentNullException(nameof(channelType));
            if (association is null) throw new ArgumentNullException(nameof(association));
            if (componentIndex.Length != channelType.Length || channelType.Length != association.Length)
                throw new ArgumentException("cdef per-entry arrays must have identical lengths.");

            ComponentIndex = componentIndex;
            ChannelType = channelType;
            Association = association;
        }

        /// <summary>
        /// Returns the codestream component index (Cn) whose association equals
        /// <paramref name="association"/>; <c>-1</c> if no such entry exists.
        /// </summary>
        public int ComponentForAssociation(int association)
        {
            for (var i = 0; i < Association.Length; i++)
            {
                if (Association[i] == association)
                    return ComponentIndex[i];
            }
            return -1;
        }
    }
}

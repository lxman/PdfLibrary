using System;

namespace JpegCodec.Segments;

// SOS (Start Of Scan) segment, T.81 §B.2.3.
internal sealed class ScanHeader
{
    public byte NumberOfComponents { get; }
    public ScanComponent[] Components { get; }
    public byte SpectralStart { get; }   // Ss
    public byte SpectralEnd { get; }     // Se
    public byte ApproxHigh { get; }      // Ah
    public byte ApproxLow { get; }       // Al

    private ScanHeader(byte ns, ScanComponent[] components, byte ss, byte se, byte ah, byte al)
    {
        NumberOfComponents = ns;
        Components = components;
        SpectralStart = ss;
        SpectralEnd = se;
        ApproxHigh = ah;
        ApproxLow = al;
    }

    public static ScanHeader Parse(ReadOnlySpan<byte> payload)
    {
        // Layout:
        //   1 byte    Number of components in scan (Ns)
        //   Per component (2 bytes):
        //     1 byte  Component selector (Csj)
        //     1 byte  DC table (Tdj << 4) | AC table (Taj)
        //   1 byte    Spectral start (Ss)
        //   1 byte    Spectral end   (Se)
        //   1 byte    Approx high (Ah << 4) | Approx low (Al)
        if (payload.Length < 1)
            throw new InvalidOperationException("SOS payload empty.");

        byte ns = payload[0];
        int expected = 1 + 2 * ns + 3;
        if (payload.Length != expected)
            throw new InvalidOperationException(
                $"SOS payload size {payload.Length} mismatched for Ns={ns} (expected {expected}).");

        var components = new ScanComponent[ns];
        for (var i = 0; i < ns; i++)
        {
            int off = 1 + 2 * i;
            byte cs = payload[off];
            byte tdta = payload[off + 1];
            components[i] = new ScanComponent(
                componentSelector: cs,
                dcTableId: (byte)(tdta >> 4),
                acTableId: (byte)(tdta & 0x0F));
        }

        int tailOff = 1 + 2 * ns;
        byte ss = payload[tailOff];
        byte se = payload[tailOff + 1];
        byte ahal = payload[tailOff + 2];
        return new ScanHeader(
            ns,
            components,
            ss,
            se,
            ah: (byte)(ahal >> 4),
            al: (byte)(ahal & 0x0F));
    }
}

internal readonly struct ScanComponent
{
    public byte ComponentSelector { get; }
    public byte DcTableId { get; }
    public byte AcTableId { get; }

    public ScanComponent(byte componentSelector, byte dcTableId, byte acTableId)
    {
        ComponentSelector = componentSelector;
        DcTableId = dcTableId;
        AcTableId = acTableId;
    }
}

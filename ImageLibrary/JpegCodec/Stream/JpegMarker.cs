namespace JpegCodec.Stream;

// ISO/IEC 10918-1 (T.81) Table B.1 — marker code assignments.
// The second byte of every two-byte marker; the first byte is always 0xFF.
public enum JpegMarker : byte
{
    None = 0x00,

    // Start Of Frame markers, non-differential, Huffman coding
    Sof0 = 0xC0,  // Baseline DCT
    Sof1 = 0xC1,  // Extended sequential DCT
    Sof2 = 0xC2,  // Progressive DCT
    Sof3 = 0xC3,  // Lossless (sequential)

    // Huffman table specification
    Dht = 0xC4,

    // Start Of Frame markers, differential, Huffman coding
    Sof5 = 0xC5,
    Sof6 = 0xC6,
    Sof7 = 0xC7,

    // Reserved for JPEG extensions / arithmetic coding
    Jpg = 0xC8,
    Sof9 = 0xC9,   // Extended sequential, arithmetic
    Sof10 = 0xCA,  // Progressive, arithmetic
    Sof11 = 0xCB,  // Lossless, arithmetic
    Dac = 0xCC,    // Arithmetic conditioning table
    Sof13 = 0xCD,
    Sof14 = 0xCE,
    Sof15 = 0xCF,

    // Restart interval termination
    Rst0 = 0xD0,
    Rst1 = 0xD1,
    Rst2 = 0xD2,
    Rst3 = 0xD3,
    Rst4 = 0xD4,
    Rst5 = 0xD5,
    Rst6 = 0xD6,
    Rst7 = 0xD7,

    // Other
    Soi = 0xD8,   // Start of image
    Eoi = 0xD9,   // End of image
    Sos = 0xDA,   // Start of scan
    Dqt = 0xDB,   // Quantization table
    Dnl = 0xDC,   // Number of lines
    Dri = 0xDD,   // Restart interval
    Dhp = 0xDE,   // Hierarchical progression
    Exp = 0xDF,   // Expand reference component

    // Application segments
    App0 = 0xE0,  // JFIF
    App1 = 0xE1,  // Exif / XMP
    App2 = 0xE2,
    App3 = 0xE3,
    App4 = 0xE4,
    App5 = 0xE5,
    App6 = 0xE6,
    App7 = 0xE7,
    App8 = 0xE8,
    App9 = 0xE9,
    App10 = 0xEA,
    App11 = 0xEB,
    App12 = 0xEC,
    App13 = 0xED,
    App14 = 0xEE, // Adobe color transform
    App15 = 0xEF,

    // Comment
    Com = 0xFE,

    // Temporary private use in arithmetic coding
    Tem = 0x01,
}

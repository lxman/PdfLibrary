using System;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Compressors.Ccitt.Tests
{
    /// <summary>
    /// Integration tests using real PDF image data from q700_page6_v2.pdf.
    /// The test data was extracted using mutool.
    /// </summary>
    public class Q700IntegrationTests
    {
        private readonly ITestOutputHelper _output;

        // Image parameters from PDF object 16
        private const int Width = 2129;
        private const int Height = 466;
        private const int K = -1; // Group 4

        public Q700IntegrationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private string GetTestDataPath(string filename)
        {
            // Find TestData directory relative to test assembly
            var assemblyDir = Path.GetDirectoryName(typeof(Q700IntegrationTests).Assembly.Location);
            var testDataDir = Path.Combine(assemblyDir!, "..", "..", "..", "TestData");
            return Path.Combine(testDataDir, filename);
        }

        [Fact]
        public void Decode_Q700_Im1_MatchesMutoolReference()
        {
            // Load test data
            var compressedPath = GetTestDataPath("q700_im1_stream.bin");
            var referencePath = GetTestDataPath("q700_im1_raw.bin");

            if (!File.Exists(compressedPath))
            {
                _output.WriteLine($"Skipping test: compressed data not found at {compressedPath}");
                return;
            }

            if (!File.Exists(referencePath))
            {
                _output.WriteLine($"Skipping test: reference data not found at {referencePath}");
                return;
            }

            var compressed = File.ReadAllBytes(compressedPath);
            var reference = File.ReadAllBytes(referencePath);

            _output.WriteLine($"Compressed data: {compressed.Length} bytes");
            _output.WriteLine($"Reference data: {reference.Length} bytes");
            _output.WriteLine($"Image: {Width}x{Height}, K={K}");

            int bytesPerRow = (Width + 7) / 8;
            int expectedBytes = bytesPerRow * Height;
            _output.WriteLine($"Expected decoded bytes: {expectedBytes} ({bytesPerRow} bytes/row)");

            // Decode using our decoder
            // ImageMask with no BlackIs1 specified means default (false in PDF)
            var options = new CcittOptions
            {
                Group = CcittGroup.Group4,
                K = -1,
                Width = Width,
                Height = Height,
                BlackIs1 = false, // PDF default
                EndOfBlock = true
            };

            var decoder = new CcittDecoder(options);
            var decoded = decoder.Decode(compressed);

            _output.WriteLine($"Decoded data: {decoded.Length} bytes");
            _output.WriteLine($"Reference data: {reference.Length} bytes");

            // Compare lengths
            Assert.Equal(reference.Length, decoded.Length);

            // Find differences
            int differences = 0;
            int firstDiffByte = -1;
            int firstDiffRow = -1;

            for (int i = 0; i < decoded.Length && i < reference.Length; i++)
            {
                if (decoded[i] != reference[i])
                {
                    if (firstDiffByte < 0)
                    {
                        firstDiffByte = i;
                        firstDiffRow = i / bytesPerRow;
                    }
                    differences++;
                }
            }

            if (differences > 0)
            {
                _output.WriteLine($"\nFound {differences} differing bytes!");
                _output.WriteLine($"First difference at byte {firstDiffByte} (row {firstDiffRow})");

                // Show context around first difference
                int contextStart = Math.Max(0, firstDiffByte - 10);
                int contextEnd = Math.Min(decoded.Length - 1, firstDiffByte + 10);

                _output.WriteLine($"\nContext around first difference (bytes {contextStart}-{contextEnd}):");
                _output.WriteLine($"Reference: {BitConverter.ToString(reference, contextStart, contextEnd - contextStart + 1)}");
                _output.WriteLine($"Decoded:   {BitConverter.ToString(decoded, contextStart, contextEnd - contextStart + 1)}");

                // Show first few different rows
                _output.WriteLine("\nFirst 5 rows with differences:");
                int diffRowsShown = 0;
                for (int row = 0; row < Height && diffRowsShown < 5; row++)
                {
                    int rowStart = row * bytesPerRow;
                    bool rowHasDiff = false;

                    for (int col = 0; col < bytesPerRow; col++)
                    {
                        if (decoded[rowStart + col] != reference[rowStart + col])
                        {
                            rowHasDiff = true;
                            break;
                        }
                    }

                    if (rowHasDiff)
                    {
                        _output.WriteLine($"\nRow {row} (bytes {rowStart}-{rowStart + bytesPerRow - 1}):");

                        // Find first differing byte in row
                        for (int col = 0; col < bytesPerRow; col++)
                        {
                            if (decoded[rowStart + col] != reference[rowStart + col])
                            {
                                int byteIdx = rowStart + col;
                                int start = Math.Max(rowStart, byteIdx - 3);
                                int end = Math.Min(rowStart + bytesPerRow - 1, byteIdx + 3);

                                _output.WriteLine($"  First diff at col {col} (pixel ~{col * 8}):");
                                _output.WriteLine($"  Ref: {BitConverter.ToString(reference, start, end - start + 1)}");
                                _output.WriteLine($"  Dec: {BitConverter.ToString(decoded, start, end - start + 1)}");
                                break;
                            }
                        }

                        diffRowsShown++;
                    }
                }
            }

            Assert.Equal(0, differences);
        }

        [Fact]
        public void Debug_DecodeRow55()
        {
            var compressedPath = GetTestDataPath("q700_im1_stream.bin");
            var referencePath = GetTestDataPath("q700_im1_raw.bin");

            if (!File.Exists(compressedPath) || !File.Exists(referencePath))
            {
                _output.WriteLine("Test data not found");
                return;
            }

            var compressed = File.ReadAllBytes(compressedPath);
            var reference = File.ReadAllBytes(referencePath);

            int bytesPerRow = (Width + 7) / 8;

            // Show reference row 54 and 55
            _output.WriteLine("Reference row 54 (first 30 bytes):");
            int row54Start = 54 * bytesPerRow;
            _output.WriteLine(BitConverter.ToString(reference, row54Start, Math.Min(30, bytesPerRow)));

            _output.WriteLine("\nReference row 55 (first 30 bytes):");
            int row55Start = 55 * bytesPerRow;
            _output.WriteLine(BitConverter.ToString(reference, row55Start, Math.Min(30, bytesPerRow)));

            // Check if rows 54 and 55 are identical
            bool identical = true;
            for (int i = 0; i < bytesPerRow; i++)
            {
                if (reference[row54Start + i] != reference[row55Start + i])
                {
                    identical = false;
                    _output.WriteLine($"\nRow 54 and 55 differ at byte {i}: {reference[row54Start + i]:X2} vs {reference[row55Start + i]:X2}");
                    break;
                }
            }
            if (identical)
                _output.WriteLine("\nRows 54 and 55 are identical");

            // Find first non-white byte in row 55
            for (int i = 0; i < bytesPerRow; i++)
            {
                if (reference[row55Start + i] != 0xFF)
                {
                    _output.WriteLine($"\nFirst non-white byte at col {i}: {reference[row55Start + i]:X2}");
                    _output.WriteLine($"  Pixels {i * 8} to {i * 8 + 7}");
                    break;
                }
            }

            // Show compressed data around bit 843
            _output.WriteLine("\nCompressed data around bit 843:");
            int byteStart = 843 / 8;
            _output.WriteLine($"Byte {byteStart}: bit offset = {843 % 8}");
            _output.WriteLine($"Bytes {byteStart - 5} to {byteStart + 10}:");
            _output.WriteLine(BitConverter.ToString(compressed, Math.Max(0, byteStart - 5), Math.Min(20, compressed.Length - byteStart + 5)));

            // Show as bits
            _output.WriteLine("\nBits around position 843:");
            int showStart = Math.Max(0, (843 / 8) - 2) * 8;
            for (int b = showStart / 8; b < Math.Min(compressed.Length, showStart / 8 + 8); b++)
            {
                string bits = Convert.ToString(compressed[b], 2).PadLeft(8, '0');
                int bitStart = b * 8;
                _output.WriteLine($"Byte {b} (bits {bitStart}-{bitStart + 7}): {bits}");
            }
        }

        [Fact]
        public void Debug_ShowCompressedDataStructure()
        {
            var compressedPath = GetTestDataPath("q700_im1_stream.bin");

            if (!File.Exists(compressedPath))
            {
                _output.WriteLine($"Skipping test: data not found at {compressedPath}");
                return;
            }

            var compressed = File.ReadAllBytes(compressedPath);

            _output.WriteLine($"Compressed data: {compressed.Length} bytes");
            _output.WriteLine($"First 100 bytes: {BitConverter.ToString(compressed, 0, Math.Min(100, compressed.Length))}");

            // Show as bits
            _output.WriteLine("\nFirst 200 bits:");
            var bits = new System.Text.StringBuilder();
            for (int i = 0; i < Math.Min(25, compressed.Length); i++)
            {
                bits.Append(Convert.ToString(compressed[i], 2).PadLeft(8, '0'));
                bits.Append(' ');
            }
            _output.WriteLine(bits.ToString());

            // Count how many rows we can decode
            _output.WriteLine("\nAttempting decode...");

            var options = new CcittOptions
            {
                Group = CcittGroup.Group4,
                K = -1,
                Width = Width,
                Height = Height,
                BlackIs1 = false,
                EndOfBlock = true
            };

            var decoder = new CcittDecoder(options);
            var decoded = decoder.Decode(compressed);

            int bytesPerRow = (Width + 7) / 8;
            int rowsDecoded = decoded.Length / bytesPerRow;
            _output.WriteLine($"Decoded {decoded.Length} bytes = {rowsDecoded} rows (expected {Height})");

            // Count non-white bytes in decoded data
            int nonWhiteBytes = 0;
            for (int i = 0; i < decoded.Length; i++)
            {
                if (decoded[i] != 0xFF) nonWhiteBytes++;
            }
            _output.WriteLine($"Non-white bytes (0xFF): {nonWhiteBytes}");
        }

        [Fact]
        public void Debug_TraceRow55Decoding()
        {
            var compressedPath = GetTestDataPath("q700_im1_stream.bin");
            var referencePath = GetTestDataPath("q700_im1_raw.bin");

            if (!File.Exists(compressedPath) || !File.Exists(referencePath))
            {
                _output.WriteLine("Test data not found");
                return;
            }

            var compressed = File.ReadAllBytes(compressedPath);
            var reference = File.ReadAllBytes(referencePath);

            int bytesPerRow = (Width + 7) / 8;

            // Show reference row 0
            _output.WriteLine("Reference row 0:");
            int row0Start = 0 * bytesPerRow;
            var changes0 = new System.Collections.Generic.List<int>();
            bool lastColor0 = false;
            for (int i = 0; i < Width; i++)
            {
                int byteIdx = row0Start + i / 8;
                int bitIdx = 7 - (i % 8);
                bool bit = ((reference[byteIdx] >> bitIdx) & 1) != 0;
                bool isBlack = !bit;
                if (i == 0 && isBlack) { changes0.Add(0); lastColor0 = true; }
                else if (isBlack != lastColor0) { changes0.Add(i); lastColor0 = isBlack; }
            }
            _output.WriteLine($"Row 0 changing elements ({changes0.Count}): {string.Join(", ", changes0.Take(30))}{(changes0.Count > 30 ? "..." : "")}");

            // Show first 20 bytes of reference row 0
            _output.WriteLine($"Row 0 first 20 bytes: {BitConverter.ToString(reference, row0Start, 20)}");

            // Find first row with content in reference
            _output.WriteLine("\nFirst 60 reference rows with content:");
            bool lastColor = false;
            int rowsShown = 0;
            for (int rowNum = 0; rowNum < 60 && rowsShown < 20; rowNum++)
            {
                int rowStart = rowNum * bytesPerRow;
                var changes = new System.Collections.Generic.List<int>();
                lastColor = false;
                for (int i = 0; i < Width; i++)
                {
                    int byteIdx = rowStart + i / 8;
                    int bitIdx = 7 - (i % 8);
                    bool bit = ((reference[byteIdx] >> bitIdx) & 1) != 0;
                    bool isBlack = !bit;
                    if (i == 0 && isBlack) { changes.Add(0); lastColor = true; }
                    else if (isBlack != lastColor) { changes.Add(i); lastColor = isBlack; }
                }
                if (changes.Count > 0)
                {
                    _output.WriteLine($"Ref row {rowNum}: {changes.Count} changes - {string.Join(", ", changes.Take(10))}{(changes.Count > 10 ? "..." : "")}");
                    rowsShown++;
                }
            }

            // Show reference rows 50-53 (leading up to where error happens)
            _output.WriteLine("\nRows 50-53:");
            for (int rowNum = 50; rowNum <= 53; rowNum++)
            {
                int rowStart = rowNum * bytesPerRow;
                var changes = new System.Collections.Generic.List<int>();
                lastColor = false;
                for (int i = 0; i < Width; i++)
                {
                    int byteIdx = rowStart + i / 8;
                    int bitIdx = 7 - (i % 8);
                    bool bit = ((reference[byteIdx] >> bitIdx) & 1) != 0;
                    bool isBlack = !bit;
                    if (i == 0 && isBlack) { changes.Add(0); lastColor = true; }
                    else if (isBlack != lastColor) { changes.Add(i); lastColor = isBlack; }
                }
                _output.WriteLine($"Ref row {rowNum}: {changes.Count} changes - {string.Join(", ", changes.Take(15))}{(changes.Count > 15 ? "..." : "")}");
            }

            // Show reference row 54 (the reference line for row 55)
            _output.WriteLine("\nReference line for row 55 (row 54):");
            int row54Start = 54 * bytesPerRow;

            // Count changing elements in row 54
            var changes54 = new System.Collections.Generic.List<int>();
            lastColor = false; // white (BlackIs1=false, so 1=white, 0xFF = all white)
            for (int i = 0; i < Width; i++)
            {
                int byteIdx = row54Start + i / 8;
                int bitIdx = 7 - (i % 8);
                bool bit = ((reference[byteIdx] >> bitIdx) & 1) != 0;
                bool isBlack = !bit; // BlackIs1=false means bit=0 is black

                if (i == 0 && isBlack)
                {
                    changes54.Add(0);
                    lastColor = true;
                }
                else if (isBlack != lastColor)
                {
                    changes54.Add(i);
                    lastColor = isBlack;
                }
            }
            _output.WriteLine($"Row 54 changing elements ({changes54.Count}): {string.Join(", ", changes54.Take(30))}{(changes54.Count > 30 ? "..." : "")}");

            // Show reference row 55
            _output.WriteLine("\nRow 55 to decode:");
            int row55Start = 55 * bytesPerRow;
            var changes55 = new System.Collections.Generic.List<int>();
            lastColor = false;
            for (int i = 0; i < Width; i++)
            {
                int byteIdx = row55Start + i / 8;
                int bitIdx = 7 - (i % 8);
                bool bit = ((reference[byteIdx] >> bitIdx) & 1) != 0;
                bool isBlack = !bit;

                if (i == 0 && isBlack)
                {
                    changes55.Add(0);
                    lastColor = true;
                }
                else if (isBlack != lastColor)
                {
                    changes55.Add(i);
                    lastColor = isBlack;
                }
            }
            _output.WriteLine($"Row 55 changing elements ({changes55.Count}): {string.Join(", ", changes55.Take(30))}{(changes55.Count > 30 ? "..." : "")}");

            // Manual decoding simulation with detailed tracing
            _output.WriteLine("\n--- Decode trace for rows 53-56 ---");

            // Decode with tracing enabled for specific rows
            var options = new CcittOptions
            {
                Group = CcittGroup.Group4,
                K = -1,
                Width = Width,
                Height = 150, // Decode rows up to 149 to focus on row 142 error
                BlackIs1 = false,
                EndOfBlock = true
            };
            var testDecoder = new CcittDecoder(options);
            testDecoder.EnableTracing = true;

            // Capture console output
            var sw = new System.IO.StringWriter();
            var oldOut = Console.Out;
            Console.SetOut(sw);

            var partial = testDecoder.Decode(compressed);

            Console.SetOut(oldOut);

            // Filter trace to show rows around first mismatch (row 45)
            var trace = sw.ToString();
            var lines = trace.Split('\n');
            bool inRange = false;
            foreach (var line in lines)
            {
                // Show rows 44-47
                if (line.StartsWith("Row 44:") || line.StartsWith("Row 45:") || line.StartsWith("Row 46:") || line.StartsWith("Row 47:"))
                    inRange = true;
                else if (line.StartsWith("Row 48:"))
                    inRange = false;

                if (inRange || line.Contains("[CCITT DEBUG]"))
                    _output.WriteLine(line);
            }

            // Show reference rows 44 and 45
            _output.WriteLine("\n--- Reference row 44 (ref line for row 45 decode) ---");
            int row44Start = 44 * bytesPerRow;
            var ref44Changes = new System.Collections.Generic.List<int>();
            lastColor = false;
            for (int i = 0; i < Width; i++)
            {
                int byteIdx = row44Start + i / 8;
                int bitIdx = 7 - (i % 8);
                bool bit = ((reference[byteIdx] >> bitIdx) & 1) != 0;
                bool isBlack = !bit;
                if (i == 0 && isBlack) { ref44Changes.Add(0); lastColor = true; }
                else if (isBlack != lastColor) { ref44Changes.Add(i); lastColor = isBlack; }
            }
            _output.WriteLine($"Ref row 44: {string.Join(", ", ref44Changes)}");

            // Also show decoded row 44
            _output.WriteLine("\n--- Decoded row 44 (for comparison) ---");
            int dec44Start = 44 * bytesPerRow;
            if (dec44Start + bytesPerRow <= partial.Length)
            {
                var dec44Changes = new System.Collections.Generic.List<int>();
                lastColor = false;
                for (int i = 0; i < Width; i++)
                {
                    int byteIdx = dec44Start + i / 8;
                    int bitIdx = 7 - (i % 8);
                    bool bit = ((partial[byteIdx] >> bitIdx) & 1) != 0;
                    bool isBlack = !bit;
                    if (i == 0 && isBlack) { dec44Changes.Add(0); lastColor = true; }
                    else if (isBlack != lastColor) { dec44Changes.Add(i); lastColor = isBlack; }
                }
                _output.WriteLine($"Decoded row 44: {string.Join(", ", dec44Changes)}");
            }

            _output.WriteLine("\n--- Reference row 45 ---");
            int row45Start = 45 * bytesPerRow;
            var ref45Changes = new System.Collections.Generic.List<int>();
            lastColor = false;
            for (int i = 0; i < Width; i++)
            {
                int byteIdx = row45Start + i / 8;
                int bitIdx = 7 - (i % 8);
                bool bit = ((reference[byteIdx] >> bitIdx) & 1) != 0;
                bool isBlack = !bit;
                if (i == 0 && isBlack) { ref45Changes.Add(0); lastColor = true; }
                else if (isBlack != lastColor) { ref45Changes.Add(i); lastColor = isBlack; }
            }
            _output.WriteLine($"Ref row 45: {string.Join(", ", ref45Changes)}");

            _output.WriteLine($"\nDecoded {partial.Length} bytes ({partial.Length / bytesPerRow} rows)");

            // Compare decoded vs reference - find first mismatch
            _output.WriteLine("\n--- Finding first mismatch ---");
            int decodedRows = partial.Length / bytesPerRow;
            int firstMismatch = -1;
            for (int rowNum = 0; rowNum < decodedRows; rowNum++)
            {
                int rowStart = rowNum * bytesPerRow;
                bool match = true;
                for (int b = 0; b < bytesPerRow && match; b++)
                {
                    if (partial[rowStart + b] != reference[rowStart + b])
                        match = false;
                }
                if (!match && firstMismatch < 0)
                {
                    firstMismatch = rowNum;
                    _output.WriteLine($"First row mismatch at row {rowNum}");
                    break;
                }
            }

            // Compare decoded vs reference for rows around first mismatch and end
            _output.WriteLine("\n--- Decoded vs Reference comparison ---");
            int startRow = firstMismatch >= 0 ? Math.Max(0, firstMismatch - 2) : Math.Max(0, decodedRows - 6);
            for (int rowNum = startRow; rowNum < Math.Min(startRow + 10, decodedRows); rowNum++)
            {
                int rowStart = rowNum * bytesPerRow;

                // Compute changing elements for decoded row
                var decodedChanges = new System.Collections.Generic.List<int>();
                bool decLastColor = false;
                for (int i = 0; i < Width; i++)
                {
                    int byteIdx = rowStart + i / 8;
                    int bitIdx = 7 - (i % 8);
                    bool bit = ((partial[byteIdx] >> bitIdx) & 1) != 0;
                    bool isBlack = !bit; // BlackIs1=false
                    if (i == 0 && isBlack) { decodedChanges.Add(0); decLastColor = true; }
                    else if (isBlack != decLastColor) { decodedChanges.Add(i); decLastColor = isBlack; }
                }

                // Compute changing elements for reference row
                var refChanges = new System.Collections.Generic.List<int>();
                bool refLastColor = false;
                for (int i = 0; i < Width; i++)
                {
                    int byteIdx = rowStart + i / 8;
                    int bitIdx = 7 - (i % 8);
                    bool bit = ((reference[byteIdx] >> bitIdx) & 1) != 0;
                    bool isBlack = !bit;
                    if (i == 0 && isBlack) { refChanges.Add(0); refLastColor = true; }
                    else if (isBlack != refLastColor) { refChanges.Add(i); refLastColor = isBlack; }
                }

                bool match = decodedChanges.Count == refChanges.Count;
                if (match)
                {
                    for (int i = 0; i < decodedChanges.Count; i++)
                    {
                        if (decodedChanges[i] != refChanges[i]) { match = false; break; }
                    }
                }

                string status = match ? "MATCH" : "MISMATCH";
                _output.WriteLine($"Row {rowNum}: {status}");
                if (!match)
                {
                    // Show all elements for rows around first mismatch
                    if (rowNum >= 45 && rowNum <= 47)
                    {
                        _output.WriteLine($"  Decoded ({decodedChanges.Count}): {string.Join(", ", decodedChanges)}");
                        _output.WriteLine($"  Reference ({refChanges.Count}): {string.Join(", ", refChanges)}");

                        // Find first different element
                        for (int i = 0; i < Math.Min(decodedChanges.Count, refChanges.Count); i++)
                        {
                            if (decodedChanges[i] != refChanges[i])
                            {
                                _output.WriteLine($"  First diff at index {i}: decoded={decodedChanges[i]}, ref={refChanges[i]}");
                                break;
                            }
                        }
                        if (decodedChanges.Count != refChanges.Count)
                            _output.WriteLine($"  Count diff: decoded={decodedChanges.Count}, ref={refChanges.Count}");
                    }
                    else
                    {
                        _output.WriteLine($"  Decoded ({decodedChanges.Count}): {string.Join(", ", decodedChanges.Take(15))}{(decodedChanges.Count > 15 ? "..." : "")}");
                        _output.WriteLine($"  Reference ({refChanges.Count}): {string.Join(", ", refChanges.Take(15))}{(refChanges.Count > 15 ? "..." : "")}");
                    }
                }
            }
        }
    }
}

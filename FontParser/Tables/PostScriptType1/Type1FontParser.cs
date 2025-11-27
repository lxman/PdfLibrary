using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using FontParser.Tables.Cff;

namespace FontParser.Tables.PostScriptType1
{
    /// <summary>
    /// Parses PostScript Type 1 fonts embedded in PDF files
    /// Handles both PFB (binary) format and raw PDF FontFile streams
    /// </summary>
    public class Type1FontParser
    {
        private string _fontName;
        private string _familyName;
        private string _fullName;
        private float[] _fontMatrix;
        private float[] _fontBBox;
        private int _lenIV = 4; // Default number of random bytes in charstrings

        private readonly Dictionary<string, byte[]> _charStrings = new Dictionary<string, byte[]>();
        private readonly List<List<byte>> _subrs = new List<List<byte>>();
        private readonly Dictionary<string, int> _encoding = new Dictionary<string, int>();

        private Type1CharstringInterpreter _interpreter;

        /// <summary>
        /// Font name (PostScript name)
        /// </summary>
        public string FontName { get { return _fontName; } }

        /// <summary>
        /// Font family name
        /// </summary>
        public string FamilyName { get { return _familyName; } }

        /// <summary>
        /// Full font name
        /// </summary>
        public string FullName { get { return _fullName; } }

        /// <summary>
        /// Font matrix [a b c d tx ty] for transforming glyph coordinates
        /// Typically [0.001 0 0 0.001 0 0] meaning 1000 units per em
        /// </summary>
        public float[] FontMatrix { get { return _fontMatrix; } }

        /// <summary>
        /// Font bounding box [llx lly urx ury]
        /// </summary>
        public float[] FontBBox { get { return _fontBBox; } }

        /// <summary>
        /// Units per em, derived from FontMatrix
        /// </summary>
        public int UnitsPerEm
        {
            get
            {
                if (_fontMatrix != null && _fontMatrix.Length >= 1 && _fontMatrix[0] > 0)
                {
                    return (int)System.Math.Round(1.0 / _fontMatrix[0]);
                }
                return 1000; // Default
            }
        }

        /// <summary>
        /// Number of glyphs in the font
        /// </summary>
        public int GlyphCount { get { return _charStrings.Count; } }

        /// <summary>
        /// Whether the font was successfully parsed
        /// </summary>
        public bool IsValid { get; private set; }

        /// <summary>
        /// List of glyph names in the font
        /// </summary>
        public IReadOnlyCollection<string> GlyphNames { get { return _charStrings.Keys; } }

        /// <summary>
        /// Get glyph name by index (for CID font glyph ID mapping)
        /// </summary>
        /// <param name="index">Index into the CharStrings dictionary</param>
        /// <returns>Glyph name at that index, or null if out of range</returns>
        public string? GetGlyphNameByIndex(int index)
        {
            if (index < 0 || index >= _charStrings.Count)
                return null;

            // Return the glyph name at the specified index
            // Note: Dictionary order may not be insertion order in .NET Framework,
            // but in .NET Core/5+ it preserves insertion order
            int i = 0;
            foreach (var key in _charStrings.Keys)
            {
                if (i == index)
                    return key;
                i++;
            }
            return null;
        }

        /// <summary>
        /// Parse a Type 1 font from PDF FontFile stream data
        /// </summary>
        /// <param name="data">Raw font data from PDF stream</param>
        /// <param name="length1">Length of ASCII portion (from PDF stream dictionary)</param>
        /// <param name="length2">Length of binary/eexec portion</param>
        /// <param name="length3">Length of trailer portion (optional)</param>
        public Type1FontParser(byte[] data, int length1, int length2, int length3 = 0)
        {
            try
            {
                Parse(data, length1, length2, length3);
                IsValid = _charStrings.Count > 0;
            }
            catch
            {
                IsValid = false;
            }
        }

        /// <summary>
        /// Parse a Type 1 font from PFB format data
        /// </summary>
        /// <param name="pfbData">PFB format font data</param>
        public Type1FontParser(byte[] pfbData)
        {
            try
            {
                ParsePfb(pfbData);
                IsValid = _charStrings.Count > 0;
            }
            catch
            {
                IsValid = false;
            }
        }

        /// <summary>
        /// Get glyph outline for a glyph by name
        /// </summary>
        public GlyphOutline GetGlyphOutline(string glyphName)
        {
            byte[] encryptedData;
            if (!_charStrings.TryGetValue(glyphName, out encryptedData))
                return null;

            if (_interpreter == null)
                _interpreter = new Type1CharstringInterpreter(_subrs);

            byte[] decrypted = Type1Decryptor.DecryptCharstring(encryptedData, _lenIV);
            return _interpreter.Interpret(decrypted);
        }

        /// <summary>
        /// Get glyph outline by character code (using encoding)
        /// </summary>
        public GlyphOutline GetGlyphOutlineByCode(int charCode)
        {
            string glyphName = GetGlyphName(charCode);
            if (glyphName == null)
                return null;

            return GetGlyphOutline(glyphName);
        }

        /// <summary>
        /// Get glyph name for a character code
        /// </summary>
        public string GetGlyphName(int charCode)
        {
            foreach (var kvp in _encoding)
            {
                if (kvp.Value == charCode)
                    return kvp.Key;
            }

            // Try standard encoding if not found
            return GetStandardEncodingName(charCode);
        }

        /// <summary>
        /// Check if a glyph exists by name
        /// </summary>
        public bool HasGlyph(string glyphName)
        {
            return _charStrings.ContainsKey(glyphName);
        }

        private void Parse(byte[] data, int length1, int length2, int length3)
        {
            // Check if this is PFA format (ASCII hex-encoded eexec)
            // PFA format has the entire font as ASCII with hex-encoded eexec section
            // If length1 == data.Length or length1 == length2, treat as PFA
            if (length1 >= data.Length || length1 == length2)
            {
                ParsePfa(data);
                return;
            }

            // Split the data according to lengths (PFB-style binary eexec)
            byte[] asciiPart = new byte[System.Math.Min(length1, data.Length)];
            Array.Copy(data, 0, asciiPart, 0, asciiPart.Length);

            int eexecStart = length1;
            int eexecLength = System.Math.Min(length2, data.Length - eexecStart);
            byte[] eexecPart = new byte[eexecLength];
            if (eexecStart < data.Length)
            {
                Array.Copy(data, eexecStart, eexecPart, 0, eexecLength);
            }

            // Parse ASCII header
            string asciiText = Encoding.ASCII.GetString(asciiPart);
            ParseAsciiHeader(asciiText);

            // Decrypt and parse eexec portion
            byte[] decrypted = Type1Decryptor.DecryptEexec(eexecPart);
            // Use byte[] overload to preserve binary charstring data
            ParsePrivateDict(decrypted);
        }

        /// <summary>
        /// Parse PFA format (ASCII header with eexec section that may be hex or binary)
        /// </summary>
        private void ParsePfa(byte[] data)
        {
            string text = Encoding.ASCII.GetString(data);
            System.Diagnostics.Debug.WriteLine($"[TYPE1-PFA] Text length: {text.Length}");

            // Find the eexec keyword
            int eexecIndex = text.IndexOf("eexec", StringComparison.Ordinal);
            System.Diagnostics.Debug.WriteLine($"[TYPE1-PFA] eexec index: {eexecIndex}");
            if (eexecIndex < 0)
            {
                // No eexec section, just parse header
                ParseAsciiHeader(text);
                return;
            }

            // Parse ASCII header (everything before eexec)
            string asciiPart = text.Substring(0, eexecIndex);
            ParseAsciiHeader(asciiPart);
            System.Diagnostics.Debug.WriteLine($"[TYPE1-PFA] FontName after header: {_fontName}");

            // Find the start of eexec data (skip "eexec" and any whitespace)
            int eexecStart = eexecIndex + 5;
            while (eexecStart < data.Length && (data[eexecStart] == ' ' || data[eexecStart] == '\r' || data[eexecStart] == '\n' || data[eexecStart] == '\t'))
                eexecStart++;

            System.Diagnostics.Debug.WriteLine($"[TYPE1-PFA] eexecStart: {eexecStart}, first bytes: {(eexecStart < data.Length ? data[eexecStart].ToString("X2") : "N/A")}");

            // Determine if the eexec section is hex-encoded or binary
            // Hex-encoded will have bytes in ranges 0-9 (0x30-0x39), A-F (0x41-0x46), a-f (0x61-0x66)
            // Binary will have bytes outside these ranges
            bool isHexEncoded = true;
            for (int i = eexecStart; i < System.Math.Min(eexecStart + 20, data.Length); i++)
            {
                byte b = data[i];
                // Skip whitespace
                if (b == ' ' || b == '\r' || b == '\n' || b == '\t')
                    continue;
                // Check if valid hex character
                bool isHexChar = (b >= '0' && b <= '9') || (b >= 'A' && b <= 'F') || (b >= 'a' && b <= 'f');
                if (!isHexChar)
                {
                    isHexEncoded = false;
                    break;
                }
            }

            System.Diagnostics.Debug.WriteLine($"[TYPE1-PFA] isHexEncoded: {isHexEncoded}");

            byte[] binaryEexec;
            if (isHexEncoded)
            {
                // Find the end of hex data (look for cleartomark or end of valid hex)
                int hexEnd = text.IndexOf("cleartomark", eexecStart, StringComparison.Ordinal);
                if (hexEnd < 0)
                    hexEnd = text.Length;

                string hexPart = text.Substring(eexecStart, hexEnd - eexecStart).Trim();

                // Remove trailing zeros pattern (512 zeros typically)
                int lastNonZero = hexPart.Length - 1;
                while (lastNonZero > 0 && hexPart[lastNonZero] == '0')
                    lastNonZero--;
                if (lastNonZero < hexPart.Length - 64)
                    hexPart = hexPart.Substring(0, lastNonZero + 1);

                // Convert hex to binary
                binaryEexec = Type1Decryptor.HexToBinary(hexPart);
            }
            else
            {
                // Binary eexec - extract directly from byte array
                // Find end by looking for cleartomark pattern or trailing zeros
                int eexecEnd = data.Length;

                // Look for cleartomark in the data
                byte[] cleartomarkBytes = Encoding.ASCII.GetBytes("cleartomark");
                for (int i = eexecStart; i < data.Length - cleartomarkBytes.Length; i++)
                {
                    bool found = true;
                    for (int j = 0; j < cleartomarkBytes.Length; j++)
                    {
                        if (data[i + j] != cleartomarkBytes[j])
                        {
                            found = false;
                            break;
                        }
                    }
                    if (found)
                    {
                        eexecEnd = i;
                        break;
                    }
                }

                // Also look for trailing zeros (at least 512 zeros)
                int zeroCount = 0;
                for (int i = eexecEnd - 1; i >= eexecStart; i--)
                {
                    if (data[i] == '0' || data[i] == 0)
                        zeroCount++;
                    else
                        break;
                }
                if (zeroCount >= 64)
                    eexecEnd -= zeroCount;

                int eexecLength = eexecEnd - eexecStart;
                binaryEexec = new byte[eexecLength];
                Array.Copy(data, eexecStart, binaryEexec, 0, eexecLength);
                System.Diagnostics.Debug.WriteLine($"[TYPE1-PFA] Binary eexec length: {eexecLength}");
            }

            System.Diagnostics.Debug.WriteLine($"[TYPE1-PFA] binaryEexec length: {binaryEexec.Length}");

            // Decrypt and parse
            byte[] decrypted = Type1Decryptor.DecryptEexec(binaryEexec);
            System.Diagnostics.Debug.WriteLine($"[TYPE1-PFA] decrypted length: {decrypted.Length}");
            // Use byte[] overload to preserve binary charstring data
            ParsePrivateDict(decrypted);
            System.Diagnostics.Debug.WriteLine($"[TYPE1-PFA] CharStrings count: {_charStrings.Count}");
        }

        private void ParsePfb(byte[] data)
        {
            List<byte> asciiData = new List<byte>();
            List<byte> binaryData = new List<byte>();

            int i = 0;
            while (i < data.Length)
            {
                if (data[i] != 0x80)
                    break;

                byte segmentType = data[i + 1];
                if (segmentType == 3) // EOF
                    break;

                int segmentLength = data[i + 2] | (data[i + 3] << 8) | (data[i + 4] << 16) | (data[i + 5] << 24);
                i += 6;

                if (i + segmentLength > data.Length)
                    break;

                if (segmentType == 1) // ASCII
                {
                    for (int j = 0; j < segmentLength; j++)
                        asciiData.Add(data[i + j]);
                }
                else if (segmentType == 2) // Binary
                {
                    for (int j = 0; j < segmentLength; j++)
                        binaryData.Add(data[i + j]);
                }

                i += segmentLength;
            }

            // Parse
            string asciiText = Encoding.ASCII.GetString(asciiData.ToArray());
            ParseAsciiHeader(asciiText);

            byte[] decrypted = Type1Decryptor.DecryptEexec(binaryData.ToArray());
            // Use byte[] overload to preserve binary charstring data
            ParsePrivateDict(decrypted);
        }

        private void ParseAsciiHeader(string text)
        {
            // Extract font name
            var fontNameMatch = Regex.Match(text, @"/FontName\s*/(\S+)\s+def");
            if (fontNameMatch.Success)
                _fontName = fontNameMatch.Groups[1].Value;

            // Extract family name
            var familyMatch = Regex.Match(text, @"/FamilyName\s*\(([^)]+)\)\s*def");
            if (familyMatch.Success)
                _familyName = familyMatch.Groups[1].Value;

            // Extract full name
            var fullNameMatch = Regex.Match(text, @"/FullName\s*\(([^)]+)\)\s*def");
            if (fullNameMatch.Success)
                _fullName = fullNameMatch.Groups[1].Value;

            // Extract FontMatrix
            var matrixMatch = Regex.Match(text, @"/FontMatrix\s*\[\s*([\d.\-e+\s]+)\s*\]");
            if (matrixMatch.Success)
            {
                string[] parts = matrixMatch.Groups[1].Value.Split(new char[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                _fontMatrix = new float[parts.Length];
                for (int i = 0; i < parts.Length; i++)
                {
                    float.TryParse(parts[i], out _fontMatrix[i]);
                }
            }

            // Extract FontBBox
            var bboxMatch = Regex.Match(text, @"/FontBBox\s*\{\s*([\d.\-\s]+)\s*\}");
            if (bboxMatch.Success)
            {
                string[] parts = bboxMatch.Groups[1].Value.Split(new char[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                _fontBBox = new float[parts.Length];
                for (int i = 0; i < parts.Length; i++)
                {
                    float.TryParse(parts[i], out _fontBBox[i]);
                }
            }

            // Parse Encoding if present in header
            ParseEncoding(text);
        }

        private void ParsePrivateDict(string text)
        {
            // Note: This overload is for backwards compatibility
            // It has issues with binary data corruption - use byte[] overload instead

            // Extract lenIV (number of random bytes in charstrings)
            var lenIVMatch = Regex.Match(text, @"/lenIV\s+(\d+)\s+def");
            if (lenIVMatch.Success)
            {
                int.TryParse(lenIVMatch.Groups[1].Value, out _lenIV);
            }

            // Parse Subrs array
            ParseSubrs(text);

            // Parse CharStrings dictionary
            ParseCharStrings(text);
        }

        /// <summary>
        /// Parse the private dictionary from raw bytes (preserves binary charstring data)
        /// </summary>
        private void ParsePrivateDict(byte[] data)
        {
            // Convert to Latin1 to preserve all byte values 0-255
            string text = System.Text.Encoding.GetEncoding("ISO-8859-1").GetString(data);

            // Extract lenIV (number of random bytes in charstrings)
            var lenIVMatch = Regex.Match(text, @"/lenIV\s+(\d+)\s+def");
            if (lenIVMatch.Success)
            {
                int.TryParse(lenIVMatch.Groups[1].Value, out _lenIV);
            }

            // Parse Subrs array from raw bytes
            ParseSubrsFromBytes(data, text);

            // Parse CharStrings dictionary from raw bytes
            ParseCharStringsFromBytes(data, text);
        }

        /// <summary>
        /// Parse Subrs from raw bytes, using text only for finding positions
        /// </summary>
        private void ParseSubrsFromBytes(byte[] data, string text)
        {
            // Find Subrs array
            var subrsMatch = Regex.Match(text, @"/Subrs\s+(\d+)\s+array");
            if (!subrsMatch.Success)
                return;

            int numSubrs = int.Parse(subrsMatch.Groups[1].Value);

            // Initialize subrs list
            for (int i = 0; i < numSubrs; i++)
                _subrs.Add(new List<byte>());

            int searchStart = subrsMatch.Index;
            var subrPattern = new Regex(@"dup\s+(\d+)\s+(\d+)\s+(RD|-\|)\s?");

            var matches = subrPattern.Matches(text, searchStart);
            foreach (Match match in matches)
            {
                int index = int.Parse(match.Groups[1].Value);
                int length = int.Parse(match.Groups[2].Value);

                // Get binary data directly from byte array
                int dataStart = match.Index + match.Length;
                if (dataStart + length <= data.Length && index < _subrs.Count)
                {
                    byte[] encryptedSubrData = new byte[length];
                    Array.Copy(data, dataStart, encryptedSubrData, 0, length);

                    // Decrypt the subroutine - they use the same charstring encryption
                    byte[] decryptedSubrData = Type1Decryptor.DecryptCharstring(encryptedSubrData, _lenIV);
                    _subrs[index] = new List<byte>(decryptedSubrData);
                }
            }
        }

        /// <summary>
        /// Parse CharStrings from raw bytes, using text only for finding positions
        /// </summary>
        private void ParseCharStringsFromBytes(byte[] data, string text)
        {
            // Find CharStrings dictionary
            var charStringsMatch = Regex.Match(text, @"/CharStrings\s+(\d+)\s+dict");
            if (!charStringsMatch.Success)
            {
                charStringsMatch = Regex.Match(text, @"/CharStrings\s+(\d+)");
                if (!charStringsMatch.Success)
                    return;
            }

            int searchStart = charStringsMatch.Index;

            // Parse individual charstrings: /glyphname length RD ...binary... ND
            var charStringPattern = new Regex(@"/(\S+)\s+(\d+)\s+(RD|-\|)\s?");

            var matches = charStringPattern.Matches(text, searchStart);

            foreach (Match match in matches)
            {
                string glyphName = match.Groups[1].Value;
                int length = int.Parse(match.Groups[2].Value);

                // Skip "CharStrings" itself
                if (glyphName == "CharStrings")
                    continue;

                // Get binary data directly from byte array
                int dataStart = match.Index + match.Length;
                if (dataStart + length <= data.Length)
                {
                    byte[] charStringData = new byte[length];
                    Array.Copy(data, dataStart, charStringData, 0, length);
                    _charStrings[glyphName] = charStringData;
                }
            }
        }

        private void ParseEncoding(string text)
        {
            // Look for encoding definitions like: dup 65 /A put
            var encodingMatches = Regex.Matches(text, @"dup\s+(\d+)\s+/(\S+)\s+put");
            foreach (Match match in encodingMatches)
            {
                int code;
                if (int.TryParse(match.Groups[1].Value, out code))
                {
                    _encoding[match.Groups[2].Value] = code;
                }
            }
        }

        private void ParseSubrs(string text)
        {
            // Find Subrs array
            var subrsMatch = Regex.Match(text, @"/Subrs\s+(\d+)\s+array");
            if (!subrsMatch.Success)
                return;

            int numSubrs = int.Parse(subrsMatch.Groups[1].Value);

            // Initialize subrs list
            for (int i = 0; i < numSubrs; i++)
                _subrs.Add(new List<byte>());

            // Parse individual subrs: dup index length RD ...binary... NP
            // RD is typically defined as: /RD {string currentfile exch readstring pop} executeonly def
            int searchStart = subrsMatch.Index;
            var subrPattern = new Regex(@"dup\s+(\d+)\s+(\d+)\s+RD\s");

            var matches = subrPattern.Matches(text, searchStart);
            foreach (Match match in matches)
            {
                int index = int.Parse(match.Groups[1].Value);
                int length = int.Parse(match.Groups[2].Value);

                // The binary data starts right after "RD " (including one space)
                int dataStart = match.Index + match.Length;

                // Need to get bytes from original position
                // This is tricky because we're working with a string representation
                // The binary data was converted to string which may have mangled some bytes

                // For proper parsing, we need to work with the original byte array
                // For now, extract what we can from the string
                if (dataStart + length <= text.Length && index < _subrs.Count)
                {
                    byte[] subrData = new byte[length];
                    for (int i = 0; i < length && dataStart + i < text.Length; i++)
                    {
                        subrData[i] = (byte)text[dataStart + i];
                    }
                    _subrs[index] = new List<byte>(subrData);
                }
            }
        }

        private void ParseCharStrings(string text)
        {
            // Find CharStrings dictionary
            var charStringsMatch = Regex.Match(text, @"/CharStrings\s+(\d+)\s+dict");
            System.Diagnostics.Debug.WriteLine($"[TYPE1-CS] Looking for CharStrings dict, found: {charStringsMatch.Success}");
            if (!charStringsMatch.Success)
            {
                // Try alternative pattern
                charStringsMatch = Regex.Match(text, @"/CharStrings\s+(\d+)");
                System.Diagnostics.Debug.WriteLine($"[TYPE1-CS] Alternative CharStrings pattern, found: {charStringsMatch.Success}");
                if (!charStringsMatch.Success)
                    return;
            }

            int searchStart = charStringsMatch.Index;
            System.Diagnostics.Debug.WriteLine($"[TYPE1-CS] CharStrings at index {searchStart}, context: {text.Substring(searchStart, System.Math.Min(100, text.Length - searchStart))}");

            // Parse individual charstrings: /glyphname length RD ...binary... ND
            // Note: RD can also be -| in some fonts
            var charStringPattern = new Regex(@"/(\S+)\s+(\d+)\s+(RD|-\|)\s?");

            var matches = charStringPattern.Matches(text, searchStart);
            System.Diagnostics.Debug.WriteLine($"[TYPE1-CS] Found {matches.Count} charstring matches with RD/-|");

            // If no matches with RD/-|, look for raw pattern with just bytes after length
            if (matches.Count == 0)
            {
                // Some fonts use different syntax, try simpler pattern
                var simplePattern = new Regex(@"/([A-Za-z0-9_.]+)\s+(\d+)\s+");
                var simpleMatches = simplePattern.Matches(text, searchStart);
                System.Diagnostics.Debug.WriteLine($"[TYPE1-CS] Found {simpleMatches.Count} simple matches");

                // Show first few matches for debugging
                int shown = 0;
                foreach (Match m in simpleMatches)
                {
                    if (shown++ < 5)
                        System.Diagnostics.Debug.WriteLine($"[TYPE1-CS] Simple match: {m.Value}");
                }
            }

            foreach (Match match in matches)
            {
                string glyphName = match.Groups[1].Value;
                int length = int.Parse(match.Groups[2].Value);

                // Skip "CharStrings" itself
                if (glyphName == "CharStrings")
                    continue;

                int dataStart = match.Index + match.Length;

                if (dataStart + length <= text.Length)
                {
                    byte[] charStringData = new byte[length];
                    for (int i = 0; i < length && dataStart + i < text.Length; i++)
                    {
                        charStringData[i] = (byte)text[dataStart + i];
                    }
                    _charStrings[glyphName] = charStringData;
                }
            }

            System.Diagnostics.Debug.WriteLine($"[TYPE1-CS] Final charstrings count: {_charStrings.Count}");
        }

        private static string GetStandardEncodingName(int charCode)
        {
            // Standard encoding mapping (partial - common characters)
            switch (charCode)
            {
                case 32: return "space";
                case 33: return "exclam";
                case 34: return "quotedbl";
                case 35: return "numbersign";
                case 36: return "dollar";
                case 37: return "percent";
                case 38: return "ampersand";
                case 39: return "quoteright";
                case 40: return "parenleft";
                case 41: return "parenright";
                case 42: return "asterisk";
                case 43: return "plus";
                case 44: return "comma";
                case 45: return "hyphen";
                case 46: return "period";
                case 47: return "slash";
                case 48: return "zero";
                case 49: return "one";
                case 50: return "two";
                case 51: return "three";
                case 52: return "four";
                case 53: return "five";
                case 54: return "six";
                case 55: return "seven";
                case 56: return "eight";
                case 57: return "nine";
                case 58: return "colon";
                case 59: return "semicolon";
                case 60: return "less";
                case 61: return "equal";
                case 62: return "greater";
                case 63: return "question";
                case 64: return "at";
                case 65: return "A";
                case 66: return "B";
                case 67: return "C";
                case 68: return "D";
                case 69: return "E";
                case 70: return "F";
                case 71: return "G";
                case 72: return "H";
                case 73: return "I";
                case 74: return "J";
                case 75: return "K";
                case 76: return "L";
                case 77: return "M";
                case 78: return "N";
                case 79: return "O";
                case 80: return "P";
                case 81: return "Q";
                case 82: return "R";
                case 83: return "S";
                case 84: return "T";
                case 85: return "U";
                case 86: return "V";
                case 87: return "W";
                case 88: return "X";
                case 89: return "Y";
                case 90: return "Z";
                case 91: return "bracketleft";
                case 92: return "backslash";
                case 93: return "bracketright";
                case 94: return "asciicircum";
                case 95: return "underscore";
                case 96: return "quoteleft";
                case 97: return "a";
                case 98: return "b";
                case 99: return "c";
                case 100: return "d";
                case 101: return "e";
                case 102: return "f";
                case 103: return "g";
                case 104: return "h";
                case 105: return "i";
                case 106: return "j";
                case 107: return "k";
                case 108: return "l";
                case 109: return "m";
                case 110: return "n";
                case 111: return "o";
                case 112: return "p";
                case 113: return "q";
                case 114: return "r";
                case 115: return "s";
                case 116: return "t";
                case 117: return "u";
                case 118: return "v";
                case 119: return "w";
                case 120: return "x";
                case 121: return "y";
                case 122: return "z";
                case 123: return "braceleft";
                case 124: return "bar";
                case 125: return "braceright";
                case 126: return "asciitilde";
                default: return null;
            }
        }
    }
}

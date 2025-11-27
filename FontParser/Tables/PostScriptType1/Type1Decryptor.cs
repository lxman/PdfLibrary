using System;

namespace FontParser.Tables.PostScriptType1
{
    /// <summary>
    /// Handles decryption of Type 1 font data (eexec and charstring encryption)
    /// </summary>
    public static class Type1Decryptor
    {
        // Encryption constants (Adobe Type 1 Font Format specification)
        private const ushort C1 = 52845;
        private const ushort C2 = 22719;

        // Initial keys
        private const ushort EexecKey = 55665;
        private const ushort CharstringKey = 4330;

        // Number of random bytes to skip after decryption
        private const int EexecSkipBytes = 4;
        private const int DefaultLenIV = 4;

        /// <summary>
        /// Decrypt eexec-encrypted data from a Type 1 font
        /// </summary>
        /// <param name="data">Encrypted binary data</param>
        /// <returns>Decrypted data with random bytes removed</returns>
        public static byte[] DecryptEexec(byte[] data)
        {
            byte[] decrypted = Decrypt(data, EexecKey);

            // Skip the first 4 random bytes
            if (decrypted.Length <= EexecSkipBytes)
                return Array.Empty<byte>();

            byte[] result = new byte[decrypted.Length - EexecSkipBytes];
            Array.Copy(decrypted, EexecSkipBytes, result, 0, result.Length);
            return result;
        }

        /// <summary>
        /// Decrypt a charstring from a Type 1 font
        /// </summary>
        /// <param name="data">Encrypted charstring data</param>
        /// <param name="lenIV">Number of random bytes to skip (typically 4, can be 0)</param>
        /// <returns>Decrypted charstring data</returns>
        public static byte[] DecryptCharstring(byte[] data, int lenIV = DefaultLenIV)
        {
            byte[] decrypted = Decrypt(data, CharstringKey);

            // Skip the lenIV random bytes
            if (decrypted.Length <= lenIV)
                return Array.Empty<byte>();

            byte[] result = new byte[decrypted.Length - lenIV];
            Array.Copy(decrypted, lenIV, result, 0, result.Length);
            return result;
        }

        /// <summary>
        /// Core decryption algorithm used by both eexec and charstring decryption
        /// </summary>
        private static byte[] Decrypt(byte[] data, ushort initialKey)
        {
            byte[] result = new byte[data.Length];
            ushort r = initialKey;

            for (int i = 0; i < data.Length; i++)
            {
                byte cipher = data[i];
                byte plain = (byte)(cipher ^ (r >> 8));
                r = (ushort)((cipher + r) * C1 + C2);
                result[i] = plain;
            }

            return result;
        }

        /// <summary>
        /// Convert hex-encoded ASCII data to binary (for PFA format)
        /// </summary>
        /// <param name="hexData">Hex-encoded ASCII string</param>
        /// <returns>Binary data</returns>
        public static byte[] HexToBinary(string hexData)
        {
            // Remove whitespace
            hexData = hexData.Replace(" ", "").Replace("\n", "").Replace("\r", "").Replace("\t", "");

            if (hexData.Length % 2 != 0)
                hexData = hexData + "0"; // Pad if odd length

            byte[] result = new byte[hexData.Length / 2];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = Convert.ToByte(hexData.Substring(i * 2, 2), 16);
            }
            return result;
        }
    }
}

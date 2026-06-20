using System;
using System.Globalization;

namespace PdfLibrary.Editing.Forms;

/// <summary>Parsed components of an AcroForm /DA (default appearance) string.</summary>
internal sealed record FieldDa(string FontName, double FontSize, string ColorOps);

/// <summary>
/// Parses a PDF /DA content snippet such as <c>/Helv 12 Tf 0 g</c> into its components.
/// Tolerant: malformed or null input falls back to Helvetica/auto-size/black defaults.
/// </summary>
internal static class FieldDaParser
{
    private static readonly FieldDa Default = new("Helv", 0, "0 g");

    // Colour operators and the number of operands each takes (ISO 32000 Table 74/75).
    private static readonly (string Op, int Operands)[] ColorOps =
    [
        ("rg", 3),   // DeviceRGB stroke  – lowercase = non-stroking (fill)
        ("RG", 3),   // DeviceRGB stroke
        ("k",  4),   // DeviceCMYK non-stroking
        ("K",  4),   // DeviceCMYK stroking
        ("g",  1),   // DeviceGray non-stroking
        ("G",  1),   // DeviceGray stroking
    ];

    /// <summary>
    /// Parses <paramref name="da"/> and returns the extracted font name, size, and colour operators.
    /// Returns the Helv/0pt/black default when <paramref name="da"/> is null, empty, or malformed.
    /// </summary>
    public static FieldDa Parse(string? da)
    {
        if (string.IsNullOrWhiteSpace(da))
            return Default;

        try
        {
            return ParseCore(da);
        }
        catch
        {
            return Default;
        }
    }

    private static FieldDa ParseCore(string da)
    {
        string[] tokens = da.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        // --- Locate Tf operator ---
        string fontName = "Helv";
        double fontSize = 0;
        bool foundTf = false;

        for (int i = 0; i < tokens.Length; i++)
        {
            if (tokens[i] == "Tf" && i >= 2)
            {
                // Token before Tf is the size; two before is the font name (with leading /).
                string rawFont = tokens[i - 2];
                string rawSize = tokens[i - 1];

                fontName = rawFont.StartsWith('/') ? rawFont.Substring(1) : rawFont;

                if (!double.TryParse(rawSize, NumberStyles.Number, CultureInfo.InvariantCulture, out fontSize))
                    fontSize = 0;

                foundTf = true;
                break;
            }
        }

        if (!foundTf)
            return new FieldDa("Helv", 0, ExtractColorOps(tokens));

        return new FieldDa(fontName, fontSize, ExtractColorOps(tokens));
    }

    /// <summary>
    /// Scans the token array for the last recognised colour operator and returns the
    /// complete operator string (operands + operator).  Returns "0 g" when none is found.
    /// </summary>
    private static string ExtractColorOps(string[] tokens)
    {
        for (int i = tokens.Length - 1; i >= 0; i--)
        {
            foreach ((string op, int operandCount) in ColorOps)
            {
                if (tokens[i] != op)
                    continue;

                // Ensure there are enough preceding operands.
                if (i < operandCount)
                    break;

                // Collect the operands + operator into a single string.
                var parts = new string[operandCount + 1];
                for (int j = 0; j < operandCount; j++)
                    parts[j] = tokens[i - operandCount + j];
                parts[operandCount] = op;

                return string.Join(' ', parts);
            }
        }

        return "0 g";
    }
}

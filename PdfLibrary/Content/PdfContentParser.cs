using PdfLibrary.Content.Operators;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using Logging;
using PdfLibrary.Parsing;

namespace PdfLibrary.Content;

/// <summary>
/// Parses PDF content streams into operators (ISO 32000-1:2008 section 7.8.2)
/// </summary>
public class PdfContentParser
{
    /// <summary>
    /// Parses a content stream and returns a list of operators
    /// </summary>
    public static List<PdfOperator> Parse(byte[] contentData)
    {
        if (contentData == null || contentData.Length == 0)
            return [];

        using var stream = new MemoryStream(contentData);
        return Parse(stream);
    }

    /// <summary>
    /// Parses a content stream from a stream
    /// </summary>
    public static List<PdfOperator> Parse(Stream stream)
    {
        var operators = new List<PdfOperator>();
        var operands = new Stack<PdfObject>();

        var lexer = new PdfLexer(stream);

        while (true)
        {
            PdfToken token = lexer.NextToken();

            if (token.Type == PdfTokenType.EndOfFile)
                break;

            // Handle operands (objects that come before operators)
            switch (token.Type)
            {
                case PdfTokenType.Integer:
                    operands.Push(new PdfInteger(int.Parse(token.Value)));
                    break;

                case PdfTokenType.Real:
                    operands.Push(new PdfReal(double.Parse(token.Value)));
                    break;

                case PdfTokenType.String:
                    operands.Push(new PdfString(token.Value));
                    break;

                case PdfTokenType.Name:
                    operands.Push(PdfName.Parse(token.Value));
                    break;

                case PdfTokenType.ArrayStart:
                    operands.Push(ParseArray(lexer));
                    break;

                case PdfTokenType.DictionaryStart:
                    operands.Push(ParseDictionary(lexer));
                    break;

                case PdfTokenType.Boolean:
                    operands.Push(token.Value == "true" ? PdfBoolean.True : PdfBoolean.False);
                    break;

                case PdfTokenType.Null:
                    operands.Push(PdfNull.Instance);
                    break;

                case PdfTokenType.Unknown:
                    // Handle inline images specially
                    if (token.Value == "BI")
                    {
                        // Parse inline image and create operator
                        InlineImageOperator? inlineImageOp = ParseInlineImage(lexer);
                        if (inlineImageOp != null)
                        {
                            operators.Add(inlineImageOp);
                        }
                        operands.Clear();
                        break;
                    }

                    // This is likely an operator
                    PdfOperator? op = CreateOperator(token.Value, operands);
                    if (op != null)
                    {
                        // Debug: trace scn/sc operators with operand types and actual values
                        if (token.Value is "scn" or "SCN" or "sc" or "SC")
                        {
                            string types = string.Join(", ", op.Operands.Select(o => $"{o.GetType().Name}:{o}"));
                            // Also show actual numeric values
                            IEnumerable<string> values = op.Operands.Select(o => o switch {
                                PdfReal r => r.Value.ToString("F4"),
                                PdfInteger i => i.Value.ToString(),
                                _ => o.ToString()
                            });
                            PdfLogger.Log(LogCategory.PdfTool, $"[PARSER] {token.Value}: {operands.Count} operands -> [{types}] values=[{string.Join(", ", values)}]");
                        }
                        // Debug: trace Do operators
                        if (token.Value == "Do")
                        {
                            string types = string.Join(", ", operands.Select(o => $"{o.GetType().Name}:{o}"));
                            PdfLogger.Log(LogCategory.PdfTool, $"[PARSER] Do: {operands.Count} operands -> [{types}], created {op.GetType().Name}");
                        }
                        operators.Add(op);
                    }
                    operands.Clear();
                    break;
            }
        }

        return operators;
    }

    /// <summary>
    /// Creates an operator from its name and operands
    /// </summary>
    private static PdfOperator CreateOperator(string name, Stack<PdfObject> operandStack)
    {
        // Convert stack to list (reverse order)
        List<PdfObject> operands = operandStack.Reverse().ToList();

        try
        {
            return name switch
            {
                // Text object operators
                "BT" => new BeginTextOperator(),
                "ET" => new EndTextOperator(),

                // Text showing operators
                "Tj" when operands is [PdfString str, ..] => new ShowTextOperator(str),
                "TJ" when operands is [PdfArray arr, ..] => new ShowTextWithPositioningOperator(arr),
                "'" when operands is [PdfString str, ..] => new MoveToNextLineAndShowTextOperator(str),
                "\"" when operands.Count >= 3 => new SetSpacingMoveAndShowTextOperator(
                    GetReal(operands[0]), GetReal(operands[1]), (PdfString)operands[2]),

                // Text positioning operators
                "Td" when operands.Count >= 2 => new MoveTextPositionOperator(
                    GetNumber(operands[0]), GetNumber(operands[1])),
                "TD" when operands.Count >= 2 => new MoveTextPositionAndSetLeadingOperator(
                    GetNumber(operands[0]), GetNumber(operands[1])),
                "Tm" when operands.Count >= 6 => new SetTextMatrixOperator(
                    GetNumber(operands[0]), GetNumber(operands[1]), GetNumber(operands[2]),
                    GetNumber(operands[3]), GetNumber(operands[4]), GetNumber(operands[5])),
                "T*" => new MoveToNextLineOperator(),

                // Text state operators
                "Tf" when operands is [PdfName font, _, ..] => new SetTextFontOperator(
                    font, GetNumber(operands[1])),
                "Tc" when operands.Count >= 1 => new SetCharSpacingOperator(GetNumber(operands[0])),
                "Tw" when operands.Count >= 1 => new SetWordSpacingOperator(GetNumber(operands[0])),
                "Tz" when operands.Count >= 1 => new SetHorizontalScalingOperator(GetNumber(operands[0])),
                "TL" when operands.Count >= 1 => new SetTextLeadingOperator(GetNumber(operands[0])),
                "Tr" when operands.Count >= 1 => new SetTextRenderingModeOperator(GetInteger(operands[0])),
                "Ts" when operands.Count >= 1 => new SetTextRiseOperator(GetNumber(operands[0])),

                // Graphics state operators
                "q" => new SaveGraphicsStateOperator(),
                "Q" => new RestoreGraphicsStateOperator(),
                "cm" when operands.Count >= 6 => new ConcatenateMatrixOperator(
                    GetNumber(operands[0]), GetNumber(operands[1]), GetNumber(operands[2]),
                    GetNumber(operands[3]), GetNumber(operands[4]), GetNumber(operands[5])),
                "w" when operands.Count >= 1 => new SetLineWidthOperator(GetNumber(operands[0])),
                "J" when operands.Count >= 1 => new SetLineCapOperator(GetInteger(operands[0])),
                "j" when operands.Count >= 1 => new SetLineJoinOperator(GetInteger(operands[0])),
                "M" when operands.Count >= 1 => new SetMiterLimitOperator(GetNumber(operands[0])),
                "d" when operands is [PdfArray arr, _, ..] => new SetDashPatternOperator(
                    arr, GetNumber(operands[1])),
                "gs" when operands is [PdfName dictName, ..] => new SetGraphicsStateOperator(dictName),
                "ri" when operands is [PdfName intent, ..] => new SetRenderingIntentOperator(intent),
                "i" when operands.Count >= 1 => new SetFlatnessOperator(GetNumber(operands[0])),

                // Path construction operators
                "m" when operands.Count >= 2 => new MoveToOperator(
                    GetNumber(operands[0]), GetNumber(operands[1])),
                "l" when operands.Count >= 2 => new LineToOperator(
                    GetNumber(operands[0]), GetNumber(operands[1])),
                "c" when operands.Count >= 6 => new CurveToOperator(
                    GetNumber(operands[0]), GetNumber(operands[1]), GetNumber(operands[2]),
                    GetNumber(operands[3]), GetNumber(operands[4]), GetNumber(operands[5])),
                "re" when operands.Count >= 4 => new RectangleOperator(
                    GetNumber(operands[0]), GetNumber(operands[1]),
                    GetNumber(operands[2]), GetNumber(operands[3])),
                "h" => new ClosePathOperator(),

                // Path painting operators
                "S" => new StrokeOperator(),
                "s" => new CloseAndStrokeOperator(),
                "f" or "F" => new FillOperator(),
                "f*" => new FillEvenOddOperator(),
                "B" => new FillAndStrokeOperator(),
                "B*" => new FillAndStrokeEvenOddOperator(),
                "b" => new CloseAndFillAndStrokeOperator(),
                "b*" => new CloseAndFillAndStrokeEvenOddOperator(),
                "n" => new EndPathOperator(),
                "W" => new ClipOperator(),
                "W*" => new ClipEvenOddOperator(),

                // Color operators - Grayscale
                "g" when operands.Count >= 1 => new SetFillGrayOperator(GetNumber(operands[0])),
                "G" when operands.Count >= 1 => new SetStrokeGrayOperator(GetNumber(operands[0])),

                // Color operators - RGB
                "rg" when operands.Count >= 3 => new SetFillRgbOperator(
                    GetNumber(operands[0]), GetNumber(operands[1]), GetNumber(operands[2])),
                "RG" when operands.Count >= 3 => new SetStrokeRgbOperator(
                    GetNumber(operands[0]), GetNumber(operands[1]), GetNumber(operands[2])),

                // Color operators - CMYK
                "k" when operands.Count >= 4 => new SetFillCmykOperator(
                    GetNumber(operands[0]), GetNumber(operands[1]), GetNumber(operands[2]), GetNumber(operands[3])),
                "K" when operands.Count >= 4 => new SetStrokeCmykOperator(
                    GetNumber(operands[0]), GetNumber(operands[1]), GetNumber(operands[2]), GetNumber(operands[3])),

                // Color operators - Color space
                "cs" when operands is [PdfName colorSpace, ..] => new SetFillColorSpaceOperator(colorSpace),
                "CS" when operands is [PdfName colorSpace, ..] => new SetStrokeColorSpaceOperator(colorSpace),

                // Color operators - Generic color
                "sc" => new SetFillColorOperator(operands),
                "SC" => new SetStrokeColorOperator(operands),
                "scn" => new SetFillColorExtendedOperator(operands),
                "SCN" => new SetStrokeColorExtendedOperator(operands),

                // XObject operator
                "Do" when operands is [PdfName xobjName, ..] => new InvokeXObjectOperator(xobjName),

                // Generic/unknown operator
                _ => new GenericOperator(name, operands)
            };
        }
        catch
        {
            // If operator creation fails, return generic operator
            return new GenericOperator(name, operands);
        }
    }

    private static double GetNumber(PdfObject obj)
    {
        return obj switch
        {
            PdfInteger i => i.Value,
            PdfReal r => r.Value,
            _ => 0
        };
    }

    private static int GetInteger(PdfObject obj)
    {
        return obj switch
        {
            PdfInteger i => i.Value,
            PdfReal r => (int)r.Value,
            _ => 0
        };
    }

    private static PdfReal GetReal(PdfObject obj)
    {
        return obj switch
        {
            PdfReal r => r,
            PdfInteger i => new PdfReal(i.Value),
            _ => new PdfReal(0)
        };
    }

    private static PdfArray ParseArray(PdfLexer lexer)
    {
        var array = new PdfArray();

        while (true)
        {
            PdfToken token = lexer.NextToken();

            if (token.Type is PdfTokenType.ArrayEnd or PdfTokenType.EndOfFile)
                break;

            // Handle different token types
            switch (token.Type)
            {
                case PdfTokenType.Integer:
                    array.Add(new PdfInteger(int.Parse(token.Value)));
                    break;

                case PdfTokenType.Real:
                    array.Add(new PdfReal(double.Parse(token.Value)));
                    break;

                case PdfTokenType.String:
                    array.Add(new PdfString(token.Value));
                    break;

                case PdfTokenType.Name:
                    array.Add(PdfName.Parse(token.Value));
                    break;

                case PdfTokenType.Boolean:
                    array.Add(token.Value == "true" ? PdfBoolean.True : PdfBoolean.False);
                    break;

                case PdfTokenType.Null:
                    array.Add(PdfNull.Instance);
                    break;

                case PdfTokenType.ArrayStart:
                    array.Add(ParseArray(lexer));
                    break;

                case PdfTokenType.DictionaryStart:
                    array.Add(ParseDictionary(lexer));
                    break;
            }
        }

        return array;
    }

    private static PdfDictionary ParseDictionary(PdfLexer lexer)
    {
        var dictionary = new PdfDictionary();
        PdfName? currentKey = null;

        while (true)
        {
            PdfToken token = lexer.NextToken();

            if (token.Type is PdfTokenType.DictionaryEnd or PdfTokenType.EndOfFile)
                break;

            // Dictionary entries are name-object pairs
            if (token.Type == PdfTokenType.Name)
            {
                currentKey = PdfName.Parse(token.Value);
            }
            else if (currentKey != null)
            {
                // Parse the value for the current key
                PdfObject? value = token.Type switch
                {
                    PdfTokenType.Integer => new PdfInteger(int.Parse(token.Value)),
                    PdfTokenType.Real => new PdfReal(double.Parse(token.Value)),
                    PdfTokenType.String => new PdfString(token.Value),
                    PdfTokenType.Name => PdfName.Parse(token.Value),
                    PdfTokenType.Boolean => token.Value == "true" ? PdfBoolean.True : PdfBoolean.False,
                    PdfTokenType.Null => PdfNull.Instance,
                    PdfTokenType.ArrayStart => ParseArray(lexer),
                    PdfTokenType.DictionaryStart => ParseDictionary(lexer),
                    _ => null
                };

                if (value != null)
                {
                    dictionary[currentKey] = value;
                    currentKey = null;
                }
            }
        }

        return dictionary;
    }

    /// <summary>
    /// Parses an inline image (BI...ID...EI) and returns an InlineImageOperator
    /// </summary>
    private static InlineImageOperator? ParseInlineImage(PdfLexer lexer)
    {
        PdfLogger.Log(LogCategory.Images, "[PARSER] Parsing inline image (BI...ID...EI)");

        // Parse the dictionary part (until we see ID)
        var parameters = new PdfDictionary();
        PdfName? currentKey = null;

        while (true)
        {
            PdfToken token = lexer.NextToken();

            if (token.Type == PdfTokenType.EndOfFile)
                return null;

            // ID marks the start of raw image data
            if (token is { Type: PdfTokenType.Unknown, Value: "ID" })
                break;

            // Parse key-value pairs
            if (token.Type == PdfTokenType.Name)
            {
                if (currentKey == null)
                {
                    currentKey = PdfName.Parse(token.Value);
                }
                else
                {
                    parameters[currentKey] = PdfName.Parse(token.Value);
                    currentKey = null;
                }
            }
            else if (currentKey != null)
            {
                PdfObject? value = token.Type switch
                {
                    PdfTokenType.Integer => new PdfInteger(int.Parse(token.Value)),
                    PdfTokenType.Real => new PdfReal(double.Parse(token.Value)),
                    PdfTokenType.String => new PdfString(token.Value),
                    PdfTokenType.Boolean => token.Value == "true" ? PdfBoolean.True : PdfBoolean.False,
                    PdfTokenType.Null => PdfNull.Instance,
                    PdfTokenType.ArrayStart => ParseArray(lexer),
                    PdfTokenType.DictionaryStart => ParseDictionary(lexer),
                    _ => null
                };

                if (value != null)
                {
                    parameters[currentKey] = value;
                    currentKey = null;
                }
            }
        }

        // Now read the raw image data until we find EI
        Stream? stream = lexer.GetStream();
        if (stream is not { CanRead: true })
        {
            lexer.SyncPositionFromStream();
            return null;
        }

        // Skip one whitespace after ID
        int b = stream.ReadByte();
        if (b == -1)
        {
            lexer.SyncPositionFromStream();
            return null;
        }

        // Read until we find EI
        var imageData = new List<byte>();
        var prevPrev = 0;
        var prev = 0;

        while (true)
        {
            b = stream.ReadByte();
            if (b == -1)
            {
                lexer.SyncPositionFromStream();
                return null;
            }

            // Check for EI sequence
            // EI should be preceded by whitespace and followed by whitespace or EOF
            if (prev == 'E' && b == 'I')
            {
                // Check if prevPrev was whitespace
                if (prevPrev is ' ' or '\n' or '\r' or '\t' or 0)
                {
                    // Peek next byte to verify it's whitespace or EOF
                    int next = stream.ReadByte();
                    if (next is -1 or ' ' or '\n' or '\r' or '\t' or 'Q' or 'q')
                    {
                        // Found EI, put back the next byte if it's not EOF
                        if (next != -1 && stream.CanSeek)
                        {
                            stream.Seek(-1, SeekOrigin.Current);
                        }
                        // Remove the 'E' from image data (we added it as prev)
                        if (imageData.Count > 0)
                            imageData.RemoveAt(imageData.Count - 1);
                        // Remove whitespace before 'E' if present
                        if (imageData.Count > 0 && IsWhitespace(imageData[^1]))
                            imageData.RemoveAt(imageData.Count - 1);

                        lexer.SyncPositionFromStream();

                        PdfLogger.Log(LogCategory.Images, $"[PARSER] Inline image parsed: {parameters.Count} params, {imageData.Count} bytes");
                        return new InlineImageOperator(parameters, imageData.ToArray());
                    }
                }
            }

            // Add previous byte to image data (we're one byte behind)
            if (prev != 0)
                imageData.Add((byte)prev);

            prevPrev = prev;
            prev = b;
        }
    }

    private static bool IsWhitespace(byte b)
    {
        return b == ' ' || b == '\n' || b == '\r' || b == '\t';
    }
}

using System.Globalization;
using System.Text;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Functions;

/// <summary>
/// Type 4 (PostScript calculator) function. The stream holds a small, calculator-only subset of
/// PostScript — a brace-delimited program of arithmetic, relational, boolean, stack and conditional
/// operators (ISO 32000-1 §7.10.5, Table 42). The program is parsed once into a nested token tree and
/// executed on a typed stack (numbers / booleans / procedures) for each Evaluate.
/// </summary>
internal class PostScriptCalculatorFunction : PdfFunction
{
    private readonly List<object> _program;

    private PostScriptCalculatorFunction(double[] domain, double[]? range, List<object> program)
    {
        Domain = domain;
        Range = range;
        _program = program;
    }

    public static PostScriptCalculatorFunction? Create(PdfStream? stream, double[] domain, double[]? range, PdfDocument? document)
    {
        // Range is required for type 4: it fixes the number of outputs.
        if (stream is null || range is null) return null;

        byte[] data = stream.GetDecodedData(document?.Decryptor);
        string code = Encoding.Latin1.GetString(data);

        List<object>? program = Parse(code);
        return program is null ? null : new PostScriptCalculatorFunction(domain, range, program);
    }

    public override double[] Evaluate(double[] input)
    {
        int outputCount = Range!.Length / 2;
        var stack = new List<object>(input.Length + 8);

        // Push the (domain-clamped) inputs.
        for (var i = 0; i < input.Length; i++)
        {
            double v = input[i];
            if (Domain.Length >= 2 * (i + 1))
                v = Clamp(v, Domain[2 * i], Domain[2 * i + 1]);
            stack.Add(v);
        }

        try
        {
            Execute(_program, stack);
        }
        catch
        {
            // Malformed program at runtime: fall back to whatever is on the stack (clamped below).
        }

        // The topmost outputCount values are the outputs, top of stack = last output.
        var result = new double[outputCount];
        for (int j = outputCount - 1; j >= 0; j--)
            result[j] = stack.Count > 0 ? ToNum(Pop(stack)) : 0.0;

        for (var j = 0; j < outputCount; j++)
            result[j] = Clamp(result[j], Range[2 * j], Range[2 * j + 1]);

        return result;
    }

    // ==================== Execution ====================

    private static void Execute(List<object> program, List<object> stack)
    {
        foreach (object token in program)
        {
            switch (token)
            {
                case double d: stack.Add(d); break;
                case List<object> proc: stack.Add(proc); break; // a procedure is pushed, not run, until if/ifelse
                case string op: ExecuteOperator(op, stack); break;
            }
        }
    }

    private static void ExecuteOperator(string op, List<object> s)
    {
        switch (op)
        {
            // --- arithmetic ---
            case "add": { double b = PopNum(s), a = PopNum(s); s.Add(a + b); break; }
            case "sub": { double b = PopNum(s), a = PopNum(s); s.Add(a - b); break; }
            case "mul": { double b = PopNum(s), a = PopNum(s); s.Add(a * b); break; }
            case "div": { double b = PopNum(s), a = PopNum(s); s.Add(b != 0 ? a / b : 0); break; }
            case "idiv": { long b = (long)PopNum(s), a = (long)PopNum(s); s.Add((double)(b != 0 ? a / b : 0)); break; }
            case "mod": { long b = (long)PopNum(s), a = (long)PopNum(s); s.Add((double)(b != 0 ? a % b : 0)); break; }
            case "neg": s.Add(-PopNum(s)); break;
            case "abs": s.Add(Math.Abs(PopNum(s))); break;
            case "sqrt": s.Add(Math.Sqrt(Math.Max(0, PopNum(s)))); break;
            case "sin": s.Add(Math.Sin(PopNum(s) * Math.PI / 180.0)); break;
            case "cos": s.Add(Math.Cos(PopNum(s) * Math.PI / 180.0)); break;
            case "atan": { double den = PopNum(s), num = PopNum(s); double a = Math.Atan2(num, den) * 180.0 / Math.PI; if (a < 0) a += 360.0; s.Add(a); break; }
            case "exp": { double e = PopNum(s), b = PopNum(s); s.Add(Math.Pow(b, e)); break; }
            case "ln": s.Add(Math.Log(PopNum(s))); break;
            case "log": s.Add(Math.Log10(PopNum(s))); break;
            case "ceiling": s.Add(Math.Ceiling(PopNum(s))); break;
            case "floor": s.Add(Math.Floor(PopNum(s))); break;
            case "round": s.Add(Math.Round(PopNum(s), MidpointRounding.AwayFromZero)); break;
            case "truncate": case "cvi": s.Add(Math.Truncate(PopNum(s))); break;
            case "cvr": break; // values are already real

            // --- relational / boolean / bitwise ---
            case "eq": { object b = Pop(s), a = Pop(s); s.Add(ObjectsEqual(a, b)); break; }
            case "ne": { object b = Pop(s), a = Pop(s); s.Add(!ObjectsEqual(a, b)); break; }
            case "gt": { double b = PopNum(s), a = PopNum(s); s.Add(a > b); break; }
            case "ge": { double b = PopNum(s), a = PopNum(s); s.Add(a >= b); break; }
            case "lt": { double b = PopNum(s), a = PopNum(s); s.Add(a < b); break; }
            case "le": { double b = PopNum(s), a = PopNum(s); s.Add(a <= b); break; }
            case "and": BinaryBoolOrInt(s, (x, y) => x && y, (x, y) => x & y); break;
            case "or": BinaryBoolOrInt(s, (x, y) => x || y, (x, y) => x | y); break;
            case "xor": BinaryBoolOrInt(s, (x, y) => x ^ y, (x, y) => x ^ y); break;
            case "not": { object o = Pop(s); s.Add(o is bool b ? !b : (double)~(long)ToNum(o)); break; }
            case "bitshift": { int shift = (int)PopNum(s); long v = (long)PopNum(s); s.Add((double)(shift >= 0 ? v << shift : v >> -shift)); break; }
            case "true": s.Add(true); break;
            case "false": s.Add(false); break;

            // --- conditional ---
            case "if": { var proc = (List<object>)Pop(s); bool c = PopBool(s); if (c) Execute(proc, s); break; }
            case "ifelse": { var p2 = (List<object>)Pop(s); var p1 = (List<object>)Pop(s); bool c = PopBool(s); Execute(c ? p1 : p2, s); break; }

            // --- stack ---
            case "pop": Pop(s); break;
            case "exch": { object b = Pop(s), a = Pop(s); s.Add(b); s.Add(a); break; }
            case "dup": s.Add(s[^1]); break;
            case "copy": { int n = (int)PopNum(s); int c = s.Count; for (var i = 0; i < n; i++) s.Add(s[c - n + i]); break; }
            case "index": { int n = (int)PopNum(s); s.Add(s[s.Count - 1 - n]); break; }
            case "roll": { int j = (int)PopNum(s); int n = (int)PopNum(s); Roll(s, n, j); break; }

            default: throw new InvalidOperationException($"unsupported PostScript operator '{op}'");
        }
    }

    private static void Roll(List<object> s, int n, int j)
    {
        if (n <= 0 || n > s.Count) return;
        int start = s.Count - n;
        List<object> seg = s.GetRange(start, n);
        j = ((j % n) + n) % n; // positive j = upward (toward top)
        for (var i = 0; i < n; i++)
            s[start + i] = seg[(i - j + n) % n];
    }

    private static bool ObjectsEqual(object a, object b)
    {
        if (a is bool ba && b is bool bb) return ba == bb;
        return Math.Abs(ToNum(a) - ToNum(b)) < 1e-9;
    }

    private static void BinaryBoolOrInt(List<object> s, Func<bool, bool, bool> boolOp, Func<long, long, long> intOp)
    {
        object b = Pop(s), a = Pop(s);
        if (a is bool ab && b is bool bb)
            s.Add(boolOp(ab, bb));
        else
            s.Add((double)intOp((long)ToNum(a), (long)ToNum(b)));
    }

    private static object Pop(List<object> s)
    {
        if (s.Count == 0) throw new InvalidOperationException("PostScript stack underflow");
        object o = s[^1];
        s.RemoveAt(s.Count - 1);
        return o;
    }

    private static double PopNum(List<object> s) => ToNum(Pop(s));

    private static bool PopBool(List<object> s) => Pop(s) switch { bool b => b, double d => d != 0, _ => false };

    private static double ToNum(object o) => o switch { double d => d, bool b => b ? 1.0 : 0.0, _ => 0.0 };

    // ==================== Parsing ====================

    // Parse the outer { ... } program into a nested token tree of doubles / operator names / sub-programs.
    private static List<object>? Parse(string code)
    {
        List<string> tokens = Tokenize(code);
        var pos = 0;
        while (pos < tokens.Count && tokens[pos] != "{") pos++; // skip to the program's opening brace
        if (pos >= tokens.Count) return null;
        return ParseBlock(tokens, ref pos);
    }

    private static List<object> ParseBlock(List<string> tokens, ref int pos)
    {
        pos++; // consume '{'
        var list = new List<object>();
        while (pos < tokens.Count && tokens[pos] != "}")
        {
            string t = tokens[pos];
            if (t == "{")
            {
                list.Add(ParseBlock(tokens, ref pos)); // recursion advances pos past the nested block
            }
            else
            {
                if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                    list.Add(d);
                else
                    list.Add(t);
                pos++;
            }
        }
        if (pos < tokens.Count) pos++; // consume '}'
        return list;
    }

    private static List<string> Tokenize(string code)
    {
        var tokens = new List<string>();
        int i = 0, n = code.Length;
        while (i < n)
        {
            char c = code[i];
            if (c == '%') // comment to end of line
            {
                while (i < n && code[i] != '\n' && code[i] != '\r') i++;
            }
            else if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (c is '{' or '}')
            {
                tokens.Add(c.ToString());
                i++;
            }
            else
            {
                int start = i;
                while (i < n && !char.IsWhiteSpace(code[i]) && code[i] != '{' && code[i] != '}' && code[i] != '%') i++;
                tokens.Add(code.Substring(start, i - start));
            }
        }
        return tokens;
    }
}

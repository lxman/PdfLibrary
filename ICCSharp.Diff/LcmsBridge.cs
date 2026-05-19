using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace ICCSharp.Diff;

/// <summary>
/// Wraps an out-of-process Python helper (<c>tools/lcms_reference.py</c>) that drives Pillow's
/// ImageCms (lcms2-backed). Used as the reference CMM for differential testing.
///
/// Pillow's exchange is 8-bit per channel, so each measured value is quantized to ~1/255.
/// That noise floor is well below the threshold we care about for algorithmic-divergence checks.
/// </summary>
public static class LcmsBridge
{
    private static readonly string PythonExe = ResolvePython();
    private static readonly string ScriptPath = Path.Combine(
        AppContext.BaseDirectory, "tools", "lcms_reference.py");

    /// <summary>True if Python and Pillow are available and the helper script is reachable.</summary>
    public static bool IsAvailable
    {
        get
        {
            try
            {
                if (!File.Exists(ScriptPath)) return false;
                using Process p = StartPython("--version");
                p.WaitForExit(2000);
                return p.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }
    }

    private static string ResolvePython()
    {
        string? env = Environment.GetEnvironmentVariable("ICCSHARP_PYTHON");
        return string.IsNullOrEmpty(env) ? "python" : env;
    }

    private static Process StartPython(string args)
    {
        ProcessStartInfo psi = new()
        {
            FileName = PythonExe,
            Arguments = args,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        return Process.Start(psi)!;
    }

    /// <summary>
    /// Run the reference CMM with the given profiles, intent, and BPC flag, returning one
    /// output pixel per input pixel.
    /// </summary>
    public static double[][] Transform(
        string sourceProfilePath,
        string destinationProfilePath,
        int intent,
        bool blackPointCompensation,
        IReadOnlyList<double[]> inputPixels,
        int outputChannels)
    {
        if (!File.Exists(sourceProfilePath))
            throw new FileNotFoundException("Source profile not found", sourceProfilePath);
        if (!File.Exists(destinationProfilePath))
            throw new FileNotFoundException("Destination profile not found", destinationProfilePath);
        if (!File.Exists(ScriptPath))
            throw new FileNotFoundException("lcms_reference.py not found", ScriptPath);

        string args = $"\"{ScriptPath}\" \"{sourceProfilePath}\" \"{destinationProfilePath}\" " +
                      $"{intent} {(blackPointCompensation ? 1 : 0)}";

        using Process proc = StartPython(args);

        // Write input pixels.
        StreamWriter stdin = proc.StandardInput;
        foreach (double[] pixel in inputPixels)
        {
            for (int i = 0; i < pixel.Length; i++)
            {
                if (i > 0) stdin.Write(' ');
                stdin.Write(pixel[i].ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            }
            stdin.Write('\n');
        }
        stdin.Close();

        // Read all output.
        string stdout = proc.StandardOutput.ReadToEnd();
        string stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();

        if (proc.ExitCode != 0)
            throw new InvalidOperationException(
                $"lcms_reference.py exited with code {proc.ExitCode}:\n{stderr}");

        // Parse output: one pixel per line, whitespace-separated floats.
        List<double[]> result = new(inputPixels.Count);
        foreach (string line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            string[] parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != outputChannels)
                throw new InvalidOperationException(
                    $"Expected {outputChannels} output channels, got {parts.Length} in line '{trimmed}'.");
            double[] pixel = new double[outputChannels];
            for (int c = 0; c < outputChannels; c++)
                pixel[c] = double.Parse(parts[c], System.Globalization.CultureInfo.InvariantCulture);
            result.Add(pixel);
        }
        if (result.Count != inputPixels.Count)
            throw new InvalidOperationException(
                $"Expected {inputPixels.Count} output pixels, got {result.Count}.");
        return result.ToArray();
    }
}

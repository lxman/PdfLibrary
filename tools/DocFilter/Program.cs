using System;
using System.IO;

namespace PdfLibrary.Build;

public static class Program
{
    // Usage: DocFilter <assembly.dll> <assembly.xml> [<assembly.dll> <assembly.xml> ...]
    // Rewrites each XML in place. Exit 0 on success, 2 on bad arguments, 1 on a missing file.
    public static int Main(string[] args)
    {
        if (args.Length == 0 || args.Length % 2 != 0)
        {
            Console.Error.WriteLine("usage: DocFilter <assembly.dll> <assembly.xml> [pairs...]");
            return 2;
        }
        for (int i = 0; i < args.Length; i += 2)
        {
            string dll = args[i], xml = args[i + 1];
            if (!File.Exists(dll) || !File.Exists(xml))
            {
                Console.Error.WriteLine($"DocFilter: missing {(File.Exists(dll) ? xml : dll)}");
                return 1;
            }
            (int removed, int kept) = DocFilter.FilterFile(dll, xml);
            Console.WriteLine($"DocFilter: {Path.GetFileName(xml)}: kept {kept}, removed {removed}");
        }
        return 0;
    }
}

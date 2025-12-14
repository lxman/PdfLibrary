using PdfLibrary.Builder;
using PdfLibrary.Builder.Page;

// ==================== CONFIGURATION ====================
const string outputPath = @"C:\Users\jorda\RiderProjects\PDF\PdfLibrary.Examples\TestPdfs\bookmarks.pdf";

Console.WriteLine("Bookmarks Example\n");
Console.WriteLine($"Creating document with bookmarks: {outputPath}\n");

// ==================== CREATE PDF WITH BOOKMARKS ====================
PdfDocumentBuilder.Create()
    .WithMetadata(m => m
        .SetTitle("Document with Bookmarks")
        .SetAuthor("PdfLibrary Examples")
        .SetSubject("Navigation Example"))

    // ==================== ADD BOOKMARKS ====================
    // Top-level bookmark to introduction
    .AddBookmark("Introduction", b => b
        .ToPage(0)
        .FitPage()
        .Bold()
        .WithColor(PdfColor.FromHex("#2C3E50")))

    // Chapter 1 with nested sections
    .AddBookmark("Chapter 1: Getting Started", b => b
        .ToPage(1)
        .FitWidth()
        .Bold()
        .WithColor(PdfColor.FromHex("#3498DB"))
        .Expanded())

    .AddBookmark("  1.1 Installation", b => b
        .ToPage(1)
        .AtPosition(top: 600)
        .WithColor(PdfColor.DarkGray))

    .AddBookmark("  1.2 Configuration", b => b
        .ToPage(1)
        .AtPosition(top: 400)
        .WithColor(PdfColor.DarkGray))

    // Chapter 2 with collapsed sections
    .AddBookmark("Chapter 2: Advanced Topics", b => b
        .ToPage(2)
        .FitWidth()
        .Bold()
        .WithColor(PdfColor.FromHex("#3498DB"))
        .Collapsed())

    .AddBookmark("  2.1 Performance", b => b
        .ToPage(2)
        .AtPosition(top: 600)
        .WithColor(PdfColor.DarkGray))

    .AddBookmark("  2.2 Security", b => b
        .ToPage(2)
        .AtPosition(top: 400)
        .WithColor(PdfColor.DarkGray))

    // Chapter 3
    .AddBookmark("Chapter 3: Examples", b => b
        .ToPage(3)
        .FitPage()
        .Bold()
        .WithColor(PdfColor.FromHex("#3498DB")))

    // Appendix
    .AddBookmark("Appendix", b => b
        .ToPage(4)
        .FitPage()
        .Italic()
        .WithColor(PdfColor.FromHex("#95A5A6")))

    // ==================== PAGE 0: INTRODUCTION ====================
    .AddPage(p =>
    {
        p.FromTopLeft();

        p.AddText("INTRODUCTION", 72, 50)
            .Font("Helvetica-Bold", 32)
            .WithColor(PdfColor.FromHex("#2C3E50"));

        p.AddRectangle(72, 90, 468, 2, PdfColor.FromHex("#2C3E50"));

        p.AddText("Welcome to the Bookmarks Example", 72, 120)
            .Font("Helvetica-Bold", 16);

        var introText = new[]
        {
            "This document demonstrates the bookmark (outline) feature of PDF documents.",
            "Bookmarks provide a hierarchical navigation structure that allows readers to",
            "quickly jump to specific sections.",
            "",
            "Features demonstrated:",
            "\u2022 Top-level bookmarks (chapters)",
            "\u2022 Nested bookmarks (sections)",
            "\u2022 Bold and italic styling",
            "\u2022 Custom colors",
            "\u2022 Collapsed and expanded states",
            "\u2022 Different destination types (FitPage, FitWidth, XYZ)",
            "",
            "Try opening this PDF in a PDF viewer and exploring the bookmark panel!"
        };

        double lineY = 160;
        foreach (var line in introText)
        {
            if (string.IsNullOrEmpty(line))
            {
                lineY += 12;
            }
            else
            {
                p.AddText(line, 72, lineY)
                    .Font("Helvetica", 11);
                lineY += 16;
            }
        }
    })

    // ==================== PAGE 1: CHAPTER 1 ====================
    .AddPage(p =>
    {
        p.FromTopLeft();

        p.AddText("CHAPTER 1: GETTING STARTED", 72, 50)
            .Font("Helvetica-Bold", 24)
            .WithColor(PdfColor.FromHex("#3498DB"));

        p.AddRectangle(72, 82, 468, 2, PdfColor.FromHex("#3498DB"));

        // Section 1.1 at y=600 (bookmark target)
        p.AddText("1.1 Installation", 72, 120)
            .Font("Helvetica-Bold", 16);

        var section11Text = new[]
        {
            "This section covers installation procedures.",
            "The bookmark jumps to this specific position using AtPosition(top: 600).",
            "",
            "Installation steps:",
            "1. Download the package",
            "2. Extract to your project directory",
            "3. Reference the library in your project",
            "4. Start coding!"
        };

        double lineY = 145;
        foreach (var line in section11Text)
        {
            if (string.IsNullOrEmpty(line))
            {
                lineY += 12;
            }
            else
            {
                p.AddText(line, 72, lineY)
                    .Font("Helvetica", 11);
                lineY += 16;
            }
        }

        // Section 1.2 at y=400 (bookmark target)
        lineY += 40;
        p.AddText("1.2 Configuration", 72, lineY)
            .Font("Helvetica-Bold", 16);

        lineY += 25;
        var section12Text = new[]
        {
            "This section covers configuration options.",
            "The bookmark jumps here using AtPosition(top: 400).",
            "",
            "Configuration is simple:",
            "\u2022 Set your preferences in the config file",
            "\u2022 Customize colors and fonts",
            "\u2022 Adjust performance settings",
            "\u2022 Enable or disable features as needed"
        };

        foreach (var line in section12Text)
        {
            if (string.IsNullOrEmpty(line))
            {
                lineY += 12;
            }
            else
            {
                p.AddText(line, 72, lineY)
                    .Font("Helvetica", 11);
                lineY += 16;
            }
        }
    })

    // ==================== PAGE 2: CHAPTER 2 ====================
    .AddPage(p =>
    {
        p.FromTopLeft();

        p.AddText("CHAPTER 2: ADVANCED TOPICS", 72, 50)
            .Font("Helvetica-Bold", 24)
            .WithColor(PdfColor.FromHex("#3498DB"));

        p.AddRectangle(72, 82, 468, 2, PdfColor.FromHex("#3498DB"));

        p.AddText("This chapter's bookmark is collapsed by default.", 72, 110)
            .Font("Helvetica-Oblique", 11)
            .WithColor(PdfColor.DarkGray);

        // Section 2.1
        p.AddText("2.1 Performance", 72, 140)
            .Font("Helvetica-Bold", 16);

        var section21Text = new[]
        {
            "Performance optimization is critical for production applications.",
            "",
            "Key strategies:",
            "\u2022 Use efficient algorithms",
            "\u2022 Cache frequently accessed data",
            "\u2022 Profile your code regularly",
            "\u2022 Optimize hot paths first"
        };

        double lineY = 165;
        foreach (var line in section21Text)
        {
            if (string.IsNullOrEmpty(line))
            {
                lineY += 12;
            }
            else
            {
                p.AddText(line, 72, lineY)
                    .Font("Helvetica", 11);
                lineY += 16;
            }
        }

        // Section 2.2
        lineY += 40;
        p.AddText("2.2 Security", 72, lineY)
            .Font("Helvetica-Bold", 16);

        lineY += 25;
        var section22Text = new[]
        {
            "Security best practices:",
            "",
            "\u2022 Validate all input",
            "\u2022 Use encryption for sensitive data",
            "\u2022 Keep dependencies updated",
            "\u2022 Follow the principle of least privilege",
            "\u2022 Implement proper authentication and authorization"
        };

        foreach (var line in section22Text)
        {
            if (string.IsNullOrEmpty(line))
            {
                lineY += 12;
            }
            else
            {
                p.AddText(line, 72, lineY)
                    .Font("Helvetica", 11);
                lineY += 16;
            }
        }
    })

    // ==================== PAGE 3: CHAPTER 3 ====================
    .AddPage(p =>
    {
        p.FromTopLeft();

        p.AddText("CHAPTER 3: EXAMPLES", 72, 50)
            .Font("Helvetica-Bold", 24)
            .WithColor(PdfColor.FromHex("#3498DB"));

        p.AddRectangle(72, 82, 468, 2, PdfColor.FromHex("#3498DB"));

        var examplesText = new[]
        {
            "This chapter contains practical examples.",
            "",
            "Example 1: Hello World",
            "  The simplest possible program to get you started.",
            "",
            "Example 2: Data Processing",
            "  Load, transform, and save data efficiently.",
            "",
            "Example 3: User Interface",
            "  Create interactive applications with rich UI.",
            "",
            "Example 4: Network Communication",
            "  Connect to remote services and exchange data.",
            "",
            "Each example includes complete source code and detailed explanations."
        };

        double lineY = 120;
        foreach (var line in examplesText)
        {
            if (string.IsNullOrEmpty(line))
            {
                lineY += 12;
            }
            else
            {
                var indent = line.StartsWith("  ") ? 90 : 72;
                p.AddText(line, indent, lineY)
                    .Font("Helvetica", 11);
                lineY += 16;
            }
        }
    })

    // ==================== PAGE 4: APPENDIX ====================
    .AddPage(p =>
    {
        p.FromTopLeft();

        p.AddText("APPENDIX", 72, 50)
            .Font("Helvetica-Bold", 24)
            .WithColor(PdfColor.FromHex("#95A5A6"));

        p.AddRectangle(72, 82, 468, 2, PdfColor.FromHex("#95A5A6"));

        var appendixText = new[]
        {
            "Additional Resources",
            "",
            "This appendix contains supplementary material:",
            "",
            "\u2022 Glossary of terms",
            "\u2022 References and further reading",
            "\u2022 Index of topics",
            "\u2022 License information",
            "\u2022 Contact information",
            "",
            "Note: This bookmark is styled with italic text and gray color",
            "to visually distinguish it from the main chapters."
        };

        double lineY = 120;
        foreach (var line in appendixText)
        {
            if (string.IsNullOrEmpty(line))
            {
                lineY += 12;
            }
            else
            {
                var font = line == "Additional Resources" ? "Helvetica-Bold" : "Helvetica";
                var size = line == "Additional Resources" ? 14 : 11;
                p.AddText(line, 72, lineY)
                    .Font(font, size);
                lineY += 16;
            }
        }
    })

    .Save(outputPath);

Console.WriteLine($"  ✓ Document with bookmarks created successfully!\n");
Console.WriteLine($"File: {outputPath}\n");
Console.WriteLine("Bookmarks created:");
Console.WriteLine("  • Introduction (bold, dark blue, FitPage)");
Console.WriteLine("  • Chapter 1: Getting Started (bold, blue, FitWidth, expanded)");
Console.WriteLine("      - 1.1 Installation (position-based)");
Console.WriteLine("      - 1.2 Configuration (position-based)");
Console.WriteLine("  • Chapter 2: Advanced Topics (bold, blue, collapsed)");
Console.WriteLine("      - 2.1 Performance");
Console.WriteLine("      - 2.2 Security");
Console.WriteLine("  • Chapter 3: Examples (bold, blue, FitPage)");
Console.WriteLine("  • Appendix (italic, gray, FitPage)\n");
Console.WriteLine("Open the PDF in a viewer to see the bookmark navigation panel!\n");
Console.WriteLine("Done!");
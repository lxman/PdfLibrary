using PdfLibrary.Builder;
using PdfLibrary.Builder.Page;
using PdfLibrary.Security;

// ==================== CONFIGURATION ====================
const string outputDir = @"C:\Users\jorda\RiderProjects\PDF\PdfLibrary.Examples\TestPdfs";

Console.WriteLine("PDF Encryption Example\n");
Console.WriteLine("This example demonstrates different encryption scenarios:\n");

// ==================== EXAMPLE 1: SIMPLE PASSWORD PROTECTION ====================
Console.WriteLine("1. Creating simple password-protected PDF...");
var simpleEncrypted = Path.Combine(outputDir, "encrypted_simple.pdf");

PdfDocumentBuilder.Create()
    .WithMetadata(m => m
        .SetTitle("Password Protected Document")
        .SetAuthor("PdfLibrary Examples")
        .SetSubject("Simple Encryption"))

    .WithPassword("secret123")  // Simple shortcut: AES256 with all permissions

    .AddPage(p =>
    {
        p.FromTopLeft();

        p.AddText("PASSWORD PROTECTED DOCUMENT", 72, 50)
            .Font("Helvetica-Bold", 28)
            .WithColor(PdfColor.FromHex("#E74C3C"));

        p.AddRectangle(72, 85, 468, 2, PdfColor.FromHex("#E74C3C"));

        p.AddText("Encryption: AES-256", 72, 120)
            .Font("Helvetica-Bold", 14);

        p.AddText("Password: secret123", 72, 145)
            .Font("Helvetica", 12)
            .WithColor(PdfColor.DarkGray);

        var infoText = new[]
        {
            "This PDF is encrypted with AES-256 encryption.",
            "You must enter the password 'secret123' to open this document.",
            "",
            "Permissions:",
            "\u2022 Print - ALLOWED",
            "\u2022 Copy content - ALLOWED",
            "\u2022 Modify content - ALLOWED",
            "\u2022 Annotations - ALLOWED",
            "",
            "This demonstrates the simple .WithPassword() method,",
            "which uses the same password for both user and owner,",
            "grants all permissions, and uses AES-256 encryption."
        };

        double lineY = 180;
        foreach (var line in infoText)
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
    .Save(simpleEncrypted);

Console.WriteLine($"   ✓ Created: {simpleEncrypted}");
Console.WriteLine($"   Password: secret123\n");

// ==================== EXAMPLE 2: RESTRICTED PERMISSIONS ====================
Console.WriteLine("2. Creating read-only PDF (no printing, no copying)...");
var restrictedPdf = Path.Combine(outputDir, "encrypted_restricted.pdf");

PdfDocumentBuilder.Create()
    .WithMetadata(m => m
        .SetTitle("Read-Only Document")
        .SetAuthor("PdfLibrary Examples")
        .SetSubject("Restricted Permissions"))

    .WithEncryption(e => e
        .WithUserPassword("view123")
        .WithOwnerPassword("admin456")
        .WithMethod(PdfEncryptionMethod.Aes256)
        .DenyAll()  // Start with no permissions
        .AllowAccessibility())  // Only allow screen reader access

    .AddPage(p =>
    {
        p.FromTopLeft();

        p.AddText("READ-ONLY DOCUMENT", 72, 50)
            .Font("Helvetica-Bold", 28)
            .WithColor(PdfColor.FromHex("#3498DB"));

        p.AddRectangle(72, 85, 468, 2, PdfColor.FromHex("#3498DB"));

        p.AddText("Encryption: AES-256 with Restricted Permissions", 72, 120)
            .Font("Helvetica-Bold", 14);

        p.AddText("User Password: view123  |  Owner Password: admin456", 72, 145)
            .Font("Helvetica", 12)
            .WithColor(PdfColor.DarkGray);

        var infoText = new[]
        {
            "This PDF has very restrictive permissions.",
            "",
            "User Password (view123) Permissions:",
            "\u2022 Print - DENIED",
            "\u2022 Copy content - DENIED",
            "\u2022 Modify content - DENIED",
            "\u2022 Annotations - DENIED",
            "\u2022 Accessibility - ALLOWED (screen readers)",
            "",
            "Owner Password (admin456) Permissions:",
            "\u2022 Full access to all features",
            "",
            "Note: PDF security is 'honor system' - it relies on PDF readers",
            "respecting these permissions. The content is still decryptable",
            "with the password, but compliant readers will enforce restrictions."
        };

        double lineY = 180;
        foreach (var line in infoText)
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
    .Save(restrictedPdf);

Console.WriteLine($"   ✓ Created: {restrictedPdf}");
Console.WriteLine($"   User Password: view123 (read-only)");
Console.WriteLine($"   Owner Password: admin456 (full access)\n");

// ==================== EXAMPLE 3: SELECTIVE PERMISSIONS ====================
Console.WriteLine("3. Creating PDF with selective permissions...");
var selectivePdf = Path.Combine(outputDir, "encrypted_selective.pdf");

PdfDocumentBuilder.Create()
    .WithMetadata(m => m
        .SetTitle("Selectively Protected Document")
        .SetAuthor("PdfLibrary Examples")
        .SetSubject("Selective Permissions"))

    .WithEncryption(e => e
        .WithUserPassword("user789")
        .WithOwnerPassword("owner999")
        .WithMethod(PdfEncryptionMethod.Aes128)  // AES-128 (older but still secure)
        .DenyAll()
        .AllowPrinting(highQuality: false)  // Low quality printing only
        .AllowCopying()
        .AllowFormFilling()
        .AllowAccessibility())

    .AddPage(p =>
    {
        p.FromTopLeft();

        p.AddText("SELECTIVELY PROTECTED DOCUMENT", 72, 50)
            .Font("Helvetica-Bold", 26)
            .WithColor(PdfColor.FromHex("#27AE60"));

        p.AddRectangle(72, 82, 468, 2, PdfColor.FromHex("#27AE60"));

        p.AddText("Encryption: AES-128 with Custom Permissions", 72, 115)
            .Font("Helvetica-Bold", 14);

        p.AddText("User Password: user789  |  Owner Password: owner999", 72, 140)
            .Font("Helvetica", 12)
            .WithColor(PdfColor.DarkGray);

        var infoText = new[]
        {
            "This PDF demonstrates selective permission control.",
            "",
            "User Password (user789) Permissions:",
            "\u2022 Print - ALLOWED (low quality only)",
            "\u2022 Print high quality - DENIED",
            "\u2022 Copy content - ALLOWED",
            "\u2022 Modify content - DENIED",
            "\u2022 Annotations - DENIED",
            "\u2022 Form filling - ALLOWED",
            "\u2022 Accessibility - ALLOWED",
            "\u2022 Document assembly - DENIED",
            "",
            "This configuration is useful for forms where you want users to:",
            "    * View and print the document (degraded quality)",
            "    * Copy text if needed",
            "    * Fill in form fields",
            "    * But NOT modify the document structure or add annotations"
        };

        double lineY = 175;
        foreach (var line in infoText)
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
    .Save(selectivePdf);

Console.WriteLine($"   ✓ Created: {selectivePdf}");
Console.WriteLine($"   User Password: user789 (selective permissions)");
Console.WriteLine($"   Owner Password: owner999 (full access)\n");

// ==================== SUMMARY ====================
Console.WriteLine("Summary:");
Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
Console.WriteLine("File                         | Encryption | Permissions");
Console.WriteLine("──────────────────────────────────────────────────────────");
Console.WriteLine("encrypted_simple.pdf         | AES-256    | All allowed");
Console.WriteLine("encrypted_restricted.pdf     | AES-256    | Read-only");
Console.WriteLine("encrypted_selective.pdf      | AES-128    | Custom");
Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
Console.WriteLine("\nTry opening these PDFs in a viewer to test the encryption!");
Console.WriteLine("\nSecurity Notes:");
Console.WriteLine("\u2022 AES-256 is recommended for new documents");
Console.WriteLine("\u2022 RC4 encryption (40/128-bit) is legacy and NOT recommended");
Console.WriteLine("\u2022 PDF permissions are enforced by readers, not cryptographically");
Console.WriteLine("\u2022 Owner password grants full access regardless of permissions");
Console.WriteLine("\nDone!");
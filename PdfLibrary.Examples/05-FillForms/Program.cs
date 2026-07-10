using PdfLibrary.Builder;
using PdfLibrary.Editing;
using PdfLibrary.Editing.Forms;

// ==================== Form filling example ====================
// Fills an AcroForm's fields through the typed editor facade. Setting a value on a
// PdfTextField / PdfButtonField / PdfChoiceField rewrites the field AND regenerates its
// appearance stream, so the value renders in any viewer (no reliance on /NeedAppearances).
//
// Usage:
//   dotnet run                 # builds a blank form, fills it, reads it back
//   dotnet run -- <form.pdf>   # fills that form's fullName / subscribe / color fields, if present
//
// Three parts:
//   1. Author a blank form to fill (setup).
//   2. Fill it via the typed facade (the main event).
//   3. Re-open the filled file and read the values back.

Console.WriteLine("PdfLibrary — Form Filling Example\n");

string outputDir = Path.Combine(Path.GetTempPath(), "pdflibrary-examples");
Directory.CreateDirectory(outputDir);

bool useSuppliedForm = args.Length > 0 && File.Exists(args[0]);
string blankPath = useSuppliedForm ? args[0] : Path.Combine(outputDir, "form-blank.pdf");
string filledPath = Path.Combine(outputDir, "form-filled.pdf");

// ---- 1. Author a blank form (skipped when a file argument was supplied) ----
if (useSuppliedForm)
{
    Console.WriteLine($"Using supplied form: {blankPath}\n");
}
else
{
    if (args.Length > 0)
        Console.WriteLine($"(File '{args[0]}' not found — building a sample form instead.)\n");
    BuildBlankForm(blankPath);
    Console.WriteLine($"Built blank form: {blankPath}\n");
}

// ---- 2. Fill the form through the typed facade ----
// Forms[name] returns a PdfFormField?; pattern-match to the concrete type to set a value.
using (PdfDocumentEditor edit = PdfDocumentEditor.Open(blankPath))
{
    if (edit.Forms["fullName"] is PdfTextField name)
        name.Value = "Ada Lovelace";                    // text field → /V + appearance

    if (edit.Forms["subscribe"] is PdfButtonField sub && sub.Kind == ButtonKind.Checkbox)
        sub.Check();                                    // checkbox → on-state

    if (edit.Forms["color"] is PdfChoiceField color)
        color.SelectedValues = ["g"];                   // dropdown → export "g" (Green)

    edit.Save(filledPath);
}
Console.WriteLine($"Filled form saved:  {filledPath}\n");

// ---- 3. Read the values back out of the filled PDF ----
Console.WriteLine("Values read back from the filled PDF:");
using (PdfDocumentEditor check = PdfDocumentEditor.Open(filledPath))
{
    foreach (PdfFormField field in check.Forms)
        Console.WriteLine($"  • {field.FullName,-10} = {Describe(field)}");
}

Console.WriteLine("\nDone!");
return;

// ---- helpers ----

static string Describe(PdfFormField field) => field switch
{
    PdfTextField t => $"\"{t.Value}\"",
    PdfButtonField b => b.IsChecked ? "checked" : "unchecked",
    PdfChoiceField c => c.SelectedValues.Count == 0
        ? "(none)"
        : string.Join(", ", c.SelectedValues.Select(v => DisplayOf(c, v))),
    _ => "(unknown)"
};

static string DisplayOf(PdfChoiceField choice, string export)
{
    foreach ((string ex, string display) in choice.Options)
        if (ex == export)
            return $"{display} [{export}]";
    return export;
}

static void BuildBlankForm(string path)
{
    // A plain page with labels, then three AcroForm fields authored on top of it.
    byte[] plain = PdfDocumentBuilder.Create()
        .WithMetadata(m => m.SetTitle("Newsletter Signup"))
        .AddPage(p =>
        {
            p.FromTopLeft();
            p.AddText("Newsletter Signup", 72, 60, "Helvetica-Bold", 20);
            p.AddText("Full name:", 72, 116, "Helvetica", 12);
            p.AddText("Subscribe to newsletter:", 72, 156, "Helvetica", 12);
            p.AddText("Favorite color:", 72, 196, "Helvetica", 12);
        })
        .ToByteArray();

    using PdfDocumentEditor edit = PdfDocumentEditor.Open(new MemoryStream(plain));

    // Field rects are PDF user space (Y-up, bottom-left origin): (left, bottom, right, top).
    edit.Forms.AddTextField(0, "fullName", new PdfRect(230, 664, 470, 684));
    edit.Forms.AddCheckbox(0, "subscribe", new PdfRect(230, 624, 246, 640));
    edit.Forms.AddDropdown(0, "color", new PdfRect(230, 584, 470, 604),
        new[] { ("r", "Red"), ("g", "Green"), ("b", "Blue") });

    edit.Save(path);
}

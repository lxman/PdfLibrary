using System.Text.Json;
using System.Xml.Linq;

// veraPDF MRR XML  ->  verapdf-verdicts.json (the committed "answer key").
//
// Usage: mrr-to-verdicts <mrr-dir> <output-json> <corpus-commit>
//   <mrr-dir>      folder of "<flavour>.mrr.xml" reports produced by capture.sh
//   <output-json>  destination snapshot path
//   <corpus-commit> short git hash of the veraPDF-corpus checkout (provenance)
//
// The snapshot is keyed by profile then by file NAME (basename). Filenames are unique
// within the corpus (the veraPDF test-suite naming guarantees it); a collision with
// differing verdicts is a hard error rather than a silent overwrite.

if (args.Length != 3)
{
    Console.Error.WriteLine("usage: mrr-to-verdicts <mrr-dir> <output-json> <corpus-commit>");
    return 2;
}

string mrrDir = args[0];
string outputJson = args[1];
string corpusCommit = args[2];

// veraPDF flavour code -> ConformanceProfile enum name (the snapshot's profile key).
var flavourToProfile = new Dictionary<string, string>
{
    ["2b"] = "PdfA2b",
    ["2u"] = "PdfA2u",
    ["3b"] = "PdfA3b",
    ["ua1"] = "PdfUA1",
};

var profiles = new Dictionary<string, ProfileVerdicts>();
Dictionary<string, string> versions = new();
int totalFiles = 0, totalNonCompliant = 0, collisions = 0;

foreach ((string flavour, string profile) in flavourToProfile)
{
    string path = Path.Combine(mrrDir, $"{flavour}.mrr.xml");
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"warn: no report for flavour '{flavour}' at {path} — skipping");
        continue;
    }

    XDocument doc = XDocument.Load(path);

    // buildInformation (take the first non-empty set we see — identical across reports).
    if (versions.Count == 0)
    {
        foreach (XElement rd in doc.Descendants("releaseDetails"))
        {
            string? id = rd.Attribute("id")?.Value;
            string? ver = rd.Attribute("version")?.Value;
            if (id is not null && ver is not null) versions[id] = ver;
        }
    }

    var files = new Dictionary<string, FileVerdict>(StringComparer.Ordinal);

    foreach (XElement job in doc.Descendants("job"))
    {
        string? namePath = job.Element("item")?.Element("name")?.Value;
        if (string.IsNullOrWhiteSpace(namePath)) continue;
        string name = Path.GetFileName(namePath.Trim());

        XElement? report = job.Element("validationReport");
        bool compliant;
        var failed = new List<FailedRule>();

        if (report is null)
        {
            // veraPDF could not produce a verdict (parse failure / exception). A file the
            // reference tool cannot validate is treated as non-compliant with no clause detail.
            compliant = false;
        }
        else
        {
            compliant = string.Equals(report.Attribute("isCompliant")?.Value, "true",
                StringComparison.OrdinalIgnoreCase);

            var seen = new HashSet<(string, int)>();
            foreach (XElement rule in report.Descendants("rule"))
            {
                if (!string.Equals(rule.Attribute("status")?.Value, "failed",
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                string clause = rule.Attribute("clause")?.Value ?? "";
                int test = int.TryParse(rule.Attribute("testNumber")?.Value, out int t) ? t : 0;
                if (clause.Length > 0 && seen.Add((clause, test)))
                    failed.Add(new FailedRule(clause, test));
            }
        }

        var verdict = new FileVerdict(compliant, failed);

        if (files.TryGetValue(name, out FileVerdict? existing))
        {
            collisions++;
            if (!existing.Equals(verdict))
                throw new InvalidOperationException(
                    $"filename collision with DIFFERING verdicts under {profile}: '{name}'. " +
                    "Snapshot keys on filename; this breaks the join and must be resolved.");
        }
        else
        {
            files[name] = verdict;
        }

        totalFiles++;
        if (!compliant) totalNonCompliant++;
    }

    profiles[profile] = new ProfileVerdicts(flavour, files);
    Console.Error.WriteLine($"{profile,-8} ({flavour}): {files.Count} files, "
        + $"{files.Values.Count(v => !v.compliant)} non-compliant");
}

var snapshot = new Snapshot(
    schemaVersion: 1,
    generatedFrom: new GeneratedFrom(versions, corpusCommit, DateTime.UtcNow.ToString("o")),
    profiles: profiles);

var jsonOpts = new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
};
File.WriteAllText(outputJson, JsonSerializer.Serialize(snapshot, jsonOpts));

Console.Error.WriteLine(
    $"\nwrote {outputJson}: {profiles.Count} profiles, {totalFiles} files, "
    + $"{totalNonCompliant} non-compliant, {collisions} same-verdict filename collision(s).");
return 0;

record Snapshot(int schemaVersion, GeneratedFrom generatedFrom, Dictionary<string, ProfileVerdicts> profiles);
record GeneratedFrom(Dictionary<string, string> verapdfVersions, string corpusCommit, string captureDateUtc);
record ProfileVerdicts(string flavour, Dictionary<string, FileVerdict> files);
record FileVerdict(bool compliant, List<FailedRule> failedRules);
record FailedRule(string clause, int testNumber);

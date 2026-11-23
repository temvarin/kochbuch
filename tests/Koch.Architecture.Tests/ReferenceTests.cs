using System.Text.RegularExpressions;

namespace Koch.Architecture.Tests;

/// <summary>
/// Comments and docs here cite files ("db/manual/grants.sql") and decisions ("docs/adr/0008", "ADR-0008") to say why.
/// A citation that points at nothing misleads the reader who follows it, and inaccurate comments are worse than none
/// (Clean Code ch5, "Lying Comments"). Both kinds are checked against the repository.
/// </summary>
public partial class ReferenceTests
{
    private static readonly string Root = FindRoot();

    private static readonly string[] Folders = ["src", "tests", "tools", "infra", "db", "docs", ".github"];

    private static readonly HashSet<string> Extensions =
        [".cs", ".razor", ".md", ".bicep", ".bicepparam", ".sql", ".yml", ".csproj", ".props", ".kql", ".js", ".html", ".css"];

    // A repository path: one of the top folders, then path characters; a {placeholder}, <placeholder> or * ends it.
    [GeneratedRegex(@"\b(?:src|tests|tools|infra|db|docs)/[A-Za-z0-9_./-]*[A-Za-z0-9_]")]
    private static partial Regex PathPattern();

    [GeneratedRegex(@"(?:adr/|ADR-)(?<number>\d{4})")]
    private static partial Regex AdrPattern();

    [Fact]
    public void Every_cited_path_exists()
    {
        var missing = Citations(PathPattern()).Where(c => !Exists(c.Text)).Select(c => $"{c.Where} → {c.Text}").ToList();

        Assert.True(missing.Count == 0, "Cited but not in the repository (fix the citation, or the name):\n" + string.Join('\n', missing));
    }

    [Fact]
    public void Every_cited_decision_has_its_adr()
    {
        var written = Directory.GetFiles(Path.Combine(Root, "docs", "adr"), "*.md").Select(f => Path.GetFileName(f)[..4]).ToHashSet();

        var missing = Citations(AdrPattern()).Where(c => !written.Contains(AdrPattern().Match(c.Text).Groups["number"].Value))
            .Select(c => $"{c.Where} → {c.Text}").ToList();

        Assert.True(missing.Count == 0, "Cites an ADR that isn't written:\n" + string.Join('\n', missing));
    }

    [Fact]
    public void The_check_reads_the_whole_repository() => Assert.True(Sources().Count() > 150);

    private static IEnumerable<(string Where, string Text)> Citations(Regex pattern) =>
        from file in Sources()
        from line in File.ReadLines(file).Select((text, index) => (text, number: index + 1))
        from Match match in pattern.Matches(line.text)
        select ($"{Path.GetRelativePath(Root, file)}:{line.number}", match.Value);

    private static IEnumerable<string> Sources() =>
        Folders.Select(f => Path.Combine(Root, f)).Where(Directory.Exists)
            .SelectMany(f => Directory.EnumerateFiles(f, "*", SearchOption.AllDirectories))
            .Append(Path.Combine(Root, "README.md"))
            .Where(f => Extensions.Contains(Path.GetExtension(f)))
            .Where(f => !f.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"));

    /// <summary>A file, a folder, or the start of a name ("db/recipes/002" for 002-outbox.sql).</summary>
    private static bool Exists(string citation)
    {
        var path = Path.Combine(Root, citation.Replace('/', Path.DirectorySeparatorChar).TrimEnd('.'));
        if (File.Exists(path) || Directory.Exists(path)) return true;
        var folder = Path.GetDirectoryName(path)!;
        return Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder, Path.GetFileName(path) + "*").Any();
    }

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Koch.slnx"))) return dir.FullName;
        }
        throw new InvalidOperationException("Koch.slnx not found above the test's folder.");
    }
}

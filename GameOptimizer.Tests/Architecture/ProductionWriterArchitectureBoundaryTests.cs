using System.Text;
using System.Text.RegularExpressions;

namespace GameOptimizer.Tests.Architecture;

public sealed class ProductionWriterArchitectureBoundaryTests
{
    private static readonly Regex RegistryMutationCall = new(
        @"\b(?:CreateSubKey|DeleteValue)\s*\(|\b(?:[A-Za-z_][A-Za-z0-9_]*(?:[Kk]ey|[Rr]egistry)|Registry(?:\.[A-Za-z_][A-Za-z0-9_]*)?|reg|hive|root)\s*\.\s*SetValue\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ProcessLaunchCall = new(
        @"\b(?:Process\.Start|StartProcess)\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CommandExecutionCall = new(
        @"\b(?:RunProcessAsync|ExecuteCommandAsync|CaptureCommandOutputAsync)\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] DangerousCommands =
    [
        "powercfg",
        "netsh",
        "sc",
        "dism",
        "bcdedit",
        "reg",
        "powershell",
        "cmd",
        "rstrui",
        "regedit"
    ];

    [Fact]
    public void Shell_registry_mutations_are_absent_and_read_port_is_the_only_registry_adapter()
    {
        var root = FindRepositoryRoot();
        var shellFiles = EnumerateShellSources(root).ToArray();
        var mutations = shellFiles
            .SelectMany(path => FindCodeMatches(path, RegistryMutationCall))
            .ToArray();

        Assert.Empty(mutations);

        var registryReaders = shellFiles
            .Where(path => ContainsCode(path, "OpenSubKey") || ContainsCode(path, "OpenBaseKey"))
            .Select(path => Path.GetRelativePath(root, path))
            .ToArray();

        Assert.Equal(
            [@"GameOptimizer.Shell\Services\RegistryInfoReader.cs"],
            registryReaders);
    }

    [Fact]
    public void Dangerous_shell_process_paths_are_contextually_classified_and_new_paths_fail_the_gate()
    {
        var root = FindRepositoryRoot();
        var findings = new List<string>();

        foreach (var path in EnumerateShellSources(root))
        {
            var source = StripComments(File.ReadAllText(path));
            var lines = source.Split(["\r\n", "\n"], StringSplitOptions.None);
            foreach (Match match in ProcessLaunchCall.Matches(source))
            {
                var context = GetContext(lines, source, match.Index, 6);
                if (ContainsDangerousCommand(context))
                {
                    findings.Add($"{Path.GetRelativePath(root, path)}:{context.Trim()}");
                }
            }

            foreach (Match match in CommandExecutionCall.Matches(source))
            {
                var context = GetContext(lines, source, match.Index, 6);
                if (ContainsDangerousCommand(context))
                {
                    findings.Add($"{Path.GetRelativePath(root, path)}:{context.Trim()}");
                }
            }
        }

        var known = findings.Where(IsExplicitlyClassifiedFinding).ToArray();
        var unclassified = findings.Except(known, StringComparer.Ordinal).ToArray();

        Assert.NotEmpty(known);
        Assert.Empty(unclassified);
    }

    private static bool IsExplicitlyClassifiedFinding(string finding) =>
        finding.Contains("GameOptimizer.Shell\\ViewModels\\RepairCenterViewModel.cs", StringComparison.Ordinal)
            || finding.Contains("GameOptimizer.Shell\\Services\\Baseline\\BaselineDomainCollector.cs", StringComparison.Ordinal)
            // powercfg /query is a read-only inventory query; it has no mutation intent.
            || finding.Contains("GameOptimizer.Shell\\Services\\PowerSettingsQueryService.cs", StringComparison.Ordinal)
            || finding.Contains("GameOptimizer.Shell\\Services\\NetworkBackupService.cs", StringComparison.Ordinal)
            || finding.Contains("GameOptimizer.Shell\\Views\\RegistryInfoDialog.xaml.cs", StringComparison.Ordinal);

    private static bool ContainsDangerousCommand(string text) =>
        DangerousCommands.Any(command => text.Contains(command, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> EnumerateShellSources(string root) =>
        Directory.EnumerateFiles(Path.Combine(root, "GameOptimizer.Shell"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

    private static bool ContainsCode(string path, string token) =>
        StripComments(File.ReadAllText(path)).Contains(token, StringComparison.Ordinal);

    private static IEnumerable<string> FindCodeMatches(string path, Regex regex)
    {
        var source = StripComments(File.ReadAllText(path));
        return regex.Matches(source).Select(match => $"{path}:{match.Value}");
    }

    private static string GetLine(string[] lines, string source, int index)
    {
        var line = source[..index].Count(character => character == '\n');
        return lines[Math.Min(line, lines.Length - 1)];
    }

    private static string GetContext(string[] lines, string source, int index, int radius)
    {
        var line = source[..index].Count(character => character == '\n');
        var start = Math.Max(0, line - radius);
        var end = Math.Min(lines.Length - 1, line + radius);
        return string.Join('\n', lines[start..(end + 1)]);
    }

    private static string StripComments(string source)
    {
        var output = new StringBuilder(source.Length);
        var inString = false;
        var inChar = false;
        var inLineComment = false;
        var inBlockComment = false;
        for (var index = 0; index < source.Length; index++)
        {
            var current = source[index];
            var next = index + 1 < source.Length ? source[index + 1] : '\0';
            if (inLineComment)
            {
                if (current == '\n')
                {
                    inLineComment = false;
                    output.Append(current);
                }
                else
                {
                    output.Append(' ');
                }
                continue;
            }

            if (inBlockComment)
            {
                if (current == '*' && next == '/')
                {
                    inBlockComment = false;
                    output.Append("  ");
                    index++;
                }
                else
                {
                    output.Append(current == '\n' ? '\n' : ' ');
                }
                continue;
            }

            if (!inString && !inChar && current == '/' && next == '/')
            {
                inLineComment = true;
                output.Append("  ");
                index++;
                continue;
            }

            if (!inString && !inChar && current == '/' && next == '*')
            {
                inBlockComment = true;
                output.Append("  ");
                index++;
                continue;
            }

            output.Append(current);
            if (current == '"' && !inChar && (index == 0 || source[index - 1] != '\\'))
            {
                inString = !inString;
            }
            else if (current == '\'' && !inString && (index == 0 || source[index - 1] != '\\'))
            {
                inChar = !inChar;
            }
        }

        return output.ToString();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GameOptimizer.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate GameOptimizer.sln.");
    }
}

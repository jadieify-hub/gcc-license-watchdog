namespace GccLicenseWatchdog.Tests;

public sealed class ToolchainTests
{
    private static readonly string[] PublicTextExtensions =
        [".cs", ".csproj", ".json", ".md", ".ps1", ".iss", ".props", ".sln"];

    private static readonly string RepositoryRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void TestRunnerUsesExpectedRuntime()
    {
        Assert.True(OperatingSystem.IsWindows());
        Assert.Equal(8, Environment.Version.Major);
    }

    [Fact]
    public void ProductMetadataIdentifiesKrsAndPublicProject()
    {
        var projectXml = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "GccLicenseWatchdog",
            "GccLicenseWatchdog.csproj"));
        var installerScript = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "installer",
            "GccLicenseWatchdog.iss"));

        Assert.Contains("<Authors>Руслан Керусов</Authors>", projectXml);
        Assert.Contains("<Company>KRS</Company>", projectXml);
        Assert.Contains("<AssemblyTitle>GCC License Watchdog</AssemblyTitle>", projectXml);
        Assert.Contains("<Copyright>© 2026 KRS</Copyright>", projectXml);
        Assert.Contains(
            "<RepositoryUrl>https://github.com/jadieify-hub/gcc-license-watchdog</RepositoryUrl>",
            projectXml);
        Assert.Contains("AppPublisher=KRS", installerScript);
        Assert.Contains(
            "AppPublisherURL=https://github.com/jadieify-hub/gcc-license-watchdog",
            installerScript);
        Assert.Contains("VersionInfoCompany=KRS", installerScript);
        Assert.Contains("VersionInfoCopyright=© 2026 KRS", installerScript);
    }

    [Fact]
    public void PublicCandidateTreeContainsNoCapturedIdentifiers()
    {
        var forbiddenTokens = new[]
        {
            string.Concat("2888", "686850"),
            string.Concat("2320", "981850"),
            string.Concat("S8F", "TBA2"),
            string.Concat("Ю", "ля"),
            string.Concat("Сер", "гей"),
            string.Concat("Пав", "лова"),
            string.Concat("228", "706"),
            string.Concat("228", "680"),
            string.Concat("230", "699"),
            string.Concat("229", "725"),
            string.Concat("2014", "83580"),
            string.Concat("9572", "8735"),
            string.Concat("1787", "642611"),
            string.Concat("127", "88"),
            string.Concat("13", "00"),
            string.Concat("17", "76"),
            string.Concat("248", "52"),
            string.Concat("C:\\Users\\", "Руслан")
        };
        var matches = Directory
            .EnumerateFiles(RepositoryRoot, "*", SearchOption.AllDirectories)
            .Where(IsPublicCandidateTextFile)
            .SelectMany(path => forbiddenTokens
                .Where(token => File.ReadAllText(path).Contains(token, StringComparison.Ordinal))
                .Select(token => $"{Path.GetRelativePath(RepositoryRoot, path)}: {token}"))
            .ToArray();

        Assert.True(matches.Length == 0, string.Join(Environment.NewLine, matches));
    }

    private static bool IsPublicCandidateTextFile(string path)
    {
        var relative = Path.GetRelativePath(RepositoryRoot, path);
        var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (segments.Any(segment => segment is ".git" or ".tools" or "artifacts" or "bin" or "obj"))
        {
            return false;
        }

        if (relative.StartsWith(
            Path.Combine("docs", "superpowers") + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return PublicTextExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    }
}

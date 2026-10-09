using System.Text.RegularExpressions;

namespace ARFastSNTP.Setup;

/// <summary>
/// Auditable allowlist for historical ARFastSNTP *service executable* layouts.
/// Never identify ownership from a process name alone and never adopt a service
/// registered to an unrelated executable. No external time service is modified.
/// </summary>
internal static class ServiceImageIdentity
{
    internal enum Layout
    {
        Foreign = 0,
        CurrentProgramFiles = 1,
        LegacyProgramFilesX86 = 2,
        LegacyProgramFilesRoot = 3
    }

    internal static string? ExtractExecutable(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath)) return null;
        string image = Environment.ExpandEnvironmentVariables(imagePath.Trim());
        // SCM accepts a quoted executable with arguments or an unquoted .exe
        // followed by arguments. Crucially: .exe.bad is NOT an executable suffix.
        var match = Regex.Match(image,
            @"^(?:""(?<quoted>[^""]+\.exe)""|(?<raw>.+?\.exe))(?=\s|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        string? executable = match.Success
            ? (match.Groups["quoted"].Success
                ? match.Groups["quoted"].Value : match.Groups["raw"].Value)
            : null;
        if (executable is null) return null;
        try
        {
            if (!Path.IsPathFullyQualified(executable)) return null;
            string fullPath = Path.GetFullPath(executable);
            if (!Path.IsPathFullyQualified(fullPath)) return null;
            if (!Path.GetFileName(fullPath).Equals("ARFastSNTP.exe",
                StringComparison.OrdinalIgnoreCase)) return null;
            return Path.TrimEndingDirectorySeparator(fullPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException
            or PathTooLongException)
        {
            return null;
        }
    }

    private static bool MatchRoot(string executable, string? root, params string[] suffix)
    {
        if (string.IsNullOrWhiteSpace(root)) return false;
        string expected = Path.GetFullPath(Path.Combine(new[] { root }.Concat(suffix).ToArray()));
        return string.Equals(executable, expected, StringComparison.OrdinalIgnoreCase);
    }

    internal static Layout Classify(string? imagePath)
    {
        string? exe = ExtractExecutable(imagePath);
        if (exe is null) return Layout.Foreign;
        string installedRoot = InstallerOperations.InstallRoot;
        if (MatchRoot(exe, installedRoot, "worker", "ARFastSNTP.exe"))
            return Layout.CurrentProgramFiles;

        // The early PowerShell and WinForms releases placed the executable
        // in the same ARFastSNTP\worker subfolder. Some installs used x86 PF.
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string x86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (MatchRoot(exe, x86, "ARFastSNTP", "worker", "ARFastSNTP.exe"))
            return Layout.LegacyProgramFilesX86;
        if (MatchRoot(exe, pf, "ARFastSNTP", "ARFastSNTP.exe") ||
            MatchRoot(exe, x86, "ARFastSNTP", "ARFastSNTP.exe"))
            return Layout.LegacyProgramFilesRoot;

        // Do NOT silently adopt binaries from arbitrary writable folders
        // (including historic manual C:\\arintp test runs). They are not
        // known installer-owned service locations.
        return Layout.Foreign;
    }

    internal static bool IsOwned(string? imagePath) =>
        Classify(imagePath) != Layout.Foreign;
}

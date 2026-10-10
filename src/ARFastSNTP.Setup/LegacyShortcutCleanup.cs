using System.Reflection;
using System.Runtime.InteropServices;

namespace ARFastSNTP.Setup;

/// <summary>
/// Remove only the obsolete C# / PowerShell desktop shortcut whose target
/// matches AR Fast SNTP's known WinForms installation layout. Never delete an
/// unrelated shortcut just because it uses a familiar display name.
/// </summary>
internal static class LegacyShortcutCleanup
{
    internal static void Reconcile(Action<string> report)
    {
        string link = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            "AR Fast SNTP.lnk");
        if (!File.Exists(link)) return;

        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
        {
            report("Legacy desktop shortcut inspection unavailable; existing shortcut was preserved.");
            return;
        }

        object? shell = null;
        object? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            if (shell is null) return;
            shortcut = shellType.InvokeMember("CreateShortcut",
                BindingFlags.InvokeMethod, null, shell, new object[] { link });
            if (shortcut is null) return;
            string? target = shortcut.GetType().InvokeMember("TargetPath",
                BindingFlags.GetProperty, null, shortcut, null) as string;
            if (!IsKnownOldDashboard(target))
            {
                report("Existing desktop shortcut target is not recognized; it was preserved.");
                return;
            }
            File.Delete(link);
            report("Removed obsolete WinForms/PowerShell desktop shortcut.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException
            or TargetInvocationException or MissingMethodException or ArgumentException
            or NotSupportedException)
        {
            report("Old desktop shortcut was preserved: " + ex.Message);
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut))
                Marshal.FinalReleaseComObject(shortcut);
            if (shell is not null && Marshal.IsComObject(shell))
                Marshal.FinalReleaseComObject(shell);
        }
    }

    internal static bool IsKnownOldDashboard(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)) return false;
        string? programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string? programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        foreach (string root in new[] { programFiles, programFilesX86 })
        {
            if (string.IsNullOrEmpty(root)) continue;
            string expected = Path.Combine(root, "ARFastSNTP", "dashboard",
                "ARFastSNTP.Dashboard.exe");
            if (string.Equals(Path.GetFullPath(target), Path.GetFullPath(expected),
                StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}

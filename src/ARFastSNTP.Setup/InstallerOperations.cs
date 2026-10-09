using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;

namespace ARFastSNTP.Setup;

/// <summary>
/// Bounded, inspectable Windows SCM setup. Does not use PowerShell, shell scripts,
/// or a background daemon in the installer. Only the SCM owns service lifecycle.
/// </summary>
internal static class InstallerOperations
{
    internal const string ServiceName = ARFastSNTP.ServiceIdentity.Name;
    internal static readonly string InstallRoot =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), ServiceName);
    internal static readonly string InstalledWorker =
        Path.Combine(InstallRoot, "worker", "ARFastSNTP.exe");
    internal static readonly string InstalledDashboard =
        Path.Combine(InstallRoot, "dashboard", "ARFastSNTP.Dashboard.exe");
    private static readonly string ScExe = Path.Combine(
        Environment.GetEnvironmentVariable("SystemRoot") ??
        Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "sc.exe");
    internal static readonly string SetupLog = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        ServiceName, "setup.log");

    internal static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    internal static void ValidateBundle(string folder)
    {
        foreach (var relative in new[]
        {
            Path.Combine("worker", "ARFastSNTP.exe"),
            Path.Combine("dashboard", "ARFastSNTP.Dashboard.exe")
        })
        {
            if (!File.Exists(Path.Combine(folder, relative)))
                throw new FileNotFoundException("Incomplete extracted artifact: missing " + relative);
        }
    }

    private static void EnsureServiceControlAvailable()
    {
        if (!File.Exists(ScExe))
            throw new FileNotFoundException("Windows Service Control utility missing: " + ScExe, ScExe);
    }

    internal static string? ExistingServiceImage()
    {
        using var key = Registry.LocalMachine.OpenSubKey(
            @"SYSTEM\CurrentControlSet\Services\" + ServiceName);
        return key?.GetValue("ImagePath") as string;
    }

    internal static void Install(SetupArguments configuration, string bundle, Action<string> report)
    {
        if (!IsAdministrator()) throw new UnauthorizedAccessException("Administrator privileges required.");
        ValidateBundle(bundle);
        EnsureServiceControlAvailable();
        if (ExistingServiceImage() is not null)
            throw new InvalidOperationException(
                "Service ARFastSNTP is already registered. Click REMOVE SERVICE first; existing settings were preserved.");
        if (Directory.Exists(InstallRoot))
            throw new InvalidOperationException(
                "Installation folder already exists. Nothing overwritten: " + InstallRoot);

        if (configuration.Lab)
        {
            var conflicts = new[] { "W32Time", "NTP", "ntpd", "ntpdssl-sag" };
            foreach (string name in conflicts)
            {
                var result = RunSc(new[] { "query", name }, false, report);
                if (result.ExitCode == 0 &&
                    Regex.IsMatch(result.Output, @"\bSTATE\s*:\s*4\b", RegexOptions.IgnoreCase))
                    throw new InvalidOperationException(
                        $"Clock-setting service {name} is RUNNING. Resolve this conflict manually " +
                        "before enabling lab clock steps; installer did not change it.");
            }
        }

        bool created = false;
        try
        {
            report("Copying verified worker and dashboard files...");
            CopyFolder(Path.Combine(bundle, "worker"), Path.Combine(InstallRoot, "worker"));
            CopyFolder(Path.Combine(bundle, "dashboard"), Path.Combine(InstallRoot, "dashboard"));

            // Verify that the exact installed executable parses this configuration.
            RunProcess(InstalledWorker,
                new[] { "--server", configuration.Server.ToString(),
                    "--server-year", configuration.ServerYear.ToString(),
                    "--check-config" }, mustSucceed: true, report);

            string flags = $"--server {configuration.Server} --server-year {configuration.ServerYear}";
            if (configuration.Lab) flags += " --certification-mode --allow-step";
            string binPath = "\"" + InstalledWorker + "\" " + flags;

            report("Registering automatic-start Windows Service (LocalSystem)...");
            RunSc(new[] { "create", ServiceName,
                "binPath=", binPath, "start=", "auto",
                "DisplayName=", "AR Fast SNTP" }, true, report);
            created = true;

            report("Starting Windows Service...");
            RunSc(new[] { "start", ServiceName }, true, report);
            bool started = false;
            for (int i = 0; i < 12; i++)
            {
                Thread.Sleep(500);
                var state = RunSc(new[] { "query", ServiceName }, false, _ => { });
                if (state.ExitCode == 0 &&
                    Regex.IsMatch(state.Output, @"\bSTATE\s*:\s*4\b", RegexOptions.IgnoreCase))
                {
                    started = true;
                    break;
                }
            }
            if (!started)
                throw new InvalidOperationException(
                    "Service did not reach RUNNING within 6 seconds. Check Windows Event Viewer and setup.log.");

            try { CreateDesktopShortcut(); }
            catch (Exception ex)
            {
                report("Service installed; desktop shortcut could not be created: " + ex.Message);
            }
            report("SUCCESS: Windows Service is RUNNING and set to Automatic.");
            report("Mode: " + (configuration.Lab ? "ISOLATED LAB CLOCK CORRECTION" : "MONITOR ONLY"));
            report("Server: " + configuration.Server + ":123");
            report("Dashboard: " + InstalledDashboard);
        }
        catch (Exception ex)
        {
            report("INSTALL FAILED: " + ex.Message);
            if (created)
            {
                RunSc(new[] { "stop", ServiceName }, false, report);
                var deleted = RunSc(new[] { "delete", ServiceName }, false, report);
                if (deleted.ExitCode != 0)
                {
                    report("ROLLBACK INCOMPLETE: Service could not be deleted; binaries retained for safety.");
                    throw;
                }
                Thread.Sleep(750);
            }
            if (Directory.Exists(InstallRoot))
            {
                try { Directory.Delete(InstallRoot, true); }
                catch (Exception cleanUpError)
                {
                    report("Cleanup incomplete: " + cleanUpError.Message);
                }
            }
            throw;
        }
    }

    internal static void Remove(Action<string> report)
    {
        if (!IsAdministrator()) throw new UnauthorizedAccessException("Administrator privileges required.");
        EnsureServiceControlAvailable();
        string? image = ExistingServiceImage();
        if (image is not null)
        {
            // Never delete a service registered by an unrelated installation.
            string prefix = "\"" + InstalledWorker + "\"";
            if (!image.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Registered service has a different ImagePath. Refusing to remove an unowned service: " + image);
            report("Stopping service...");
            RunSc(new[] { "stop", ServiceName }, false, report);
            for (int i = 0; i < 20; i++)
            {
                var state = RunSc(new[] { "query", ServiceName }, false, _ => { });
                if (state.ExitCode != 0 ||
                    Regex.IsMatch(state.Output, @"\bSTATE\s*:\s*1\b", RegexOptions.IgnoreCase))
                    break;
                Thread.Sleep(250);
            }
            report("Unregistering service...");
            RunSc(new[] { "delete", ServiceName }, true, report);
        }
        else report("Service is not registered; checking owned installation files.");

        if (Directory.Exists(InstallRoot))
        {
            if (!File.Exists(InstalledWorker) && !File.Exists(InstalledDashboard))
                throw new InvalidOperationException(
                    "Unknown files in ARFastSNTP installation folder. Refusing to delete this folder.");
            // A failed registration removal must never leave executables missing.
            if (ExistingServiceImage() is not null)
                throw new InvalidOperationException(
                    "Service deletion pending in Windows SCM. Restart Windows, then run REMOVE again; files retained.");
            Directory.Delete(InstallRoot, true);
        }
        string shortcut = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            "AR Fast SNTP.lnk");
        if (File.Exists(shortcut))
        {
            try { File.Delete(shortcut); }
            catch (IOException ex) { report("Shortcut removal skipped: " + ex.Message); }
        }
        report("Service and application removed. Status/history in ProgramData preserved.");
    }

    private static void CopyFolder(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (string folder in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Refusing to copy a directory symlink.");
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, folder)));
        }
        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Refusing to copy a file symlink.");
            string destination = Path.Combine(target, Path.GetRelativePath(source, file));
            File.Copy(file, destination, overwrite: false);
        }
    }

    private static (int ExitCode, string Output) RunSc(
        string[] arguments, bool mustSucceed, Action<string> report) =>
        RunProcess(ScExe, arguments, mustSucceed, report);

    private static (int ExitCode, string Output) RunProcess(
        string executable, string[] arguments, bool mustSucceed, Action<string> report)
    {
        var start = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var proc = Process.Start(start)
            ?? throw new InvalidOperationException("Cannot start setup utility: " + executable);
        // Avoid deadlock if either output stream fills during a Service Control Manager failure.
        Task<string> outputTask = proc.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = proc.StandardError.ReadToEndAsync();
        if (!proc.WaitForExit(20000))
        {
            proc.Kill(entireProcessTree: true);
            throw new TimeoutException("Setup utility timed out: " + executable);
        }
        string output = outputTask.GetAwaiter().GetResult() + errorTask.GetAwaiter().GetResult();
        if (mustSucceed || proc.ExitCode != 0)
        {
            foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .TakeLast(6))
                report(line.TrimEnd());
        }
        if (mustSucceed && proc.ExitCode != 0)
            throw new InvalidOperationException(
                Path.GetFileName(executable) + " failed (" + proc.ExitCode + "): " +
                output.Trim().Replace("\r", "").Replace("\n", " "));
        return (proc.ExitCode, output);
    }

    private static void CreateDesktopShortcut()
    {
        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null) throw new InvalidOperationException("Windows shortcut component unavailable.");
        object shell = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("Unable to start Windows shortcut component.");
        object? shortcut = null;
        try
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                "AR Fast SNTP.lnk");
            shortcut = shellType.InvokeMember("CreateShortcut",
                System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { path });
            if (shortcut is null) throw new InvalidOperationException("Shortcut creation failed.");
            Type type = shortcut.GetType();
            type.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty,
                null, shortcut, new object[] { InstalledDashboard });
            type.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty,
                null, shortcut, new object[] { Path.GetDirectoryName(InstalledDashboard)! });
            type.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod,
                null, shortcut, null);
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut))
                Marshal.FinalReleaseComObject(shortcut);
            if (Marshal.IsComObject(shell))
                Marshal.FinalReleaseComObject(shell);
        }
    }

    internal static void PersistLog(string message)
    {
        try
        {
            string directory = Path.GetDirectoryName(SetupLog)!;
            Directory.CreateDirectory(directory);
            // Bound disk consumption: rotate old setup diagnostics after 256 KB.
            if (File.Exists(SetupLog) && new FileInfo(SetupLog).Length > 256 * 1024)
                File.Move(SetupLog, SetupLog + ".previous", overwrite: true);
            File.AppendAllText(SetupLog, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}",
                new UTF8Encoding(false));
        }
        catch (Exception)
        {
            // Always keep the visible UI diagnostics; a locked log must not halt cleanup.
        }
    }
}

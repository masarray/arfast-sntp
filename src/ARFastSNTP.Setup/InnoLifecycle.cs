using Microsoft.Win32;
using System.Diagnostics;
using System.Text;

namespace ARFastSNTP.Setup;

/// <summary>
/// Headless service lifecycle used exclusively by Inno Setup (never shown to users).
/// Operations are bounded and idempotent. Existing service configuration + worker
/// are staged for recovery before any running binary is overwritten.
/// </summary>
internal static class InnoLifecycle
{
    private static readonly string RecoveryDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        InstallerOperations.ServiceName, "installer-recovery");
    private static readonly string PreviousImageFile = Path.Combine(RecoveryDirectory, "previous-image.txt");
    private static readonly string PreviousWorkerFile = Path.Combine(RecoveryDirectory, "previous-worker.exe");
    private static readonly string PreviousStateFile = Path.Combine(RecoveryDirectory, "previous-state.txt");
    private const string NewMarker = "!NO_PREVIOUS_SERVICE!";

    private static void RequireAdministrator()
    {
        if (!InstallerOperations.IsAdministrator())
            throw new UnauthorizedAccessException("Windows Administrator access is required.");
        InstallerOperations.EnsureServiceControlAvailable();
    }

    private static string? ReadOwnedService()
    {
        string? image = InstallerOperations.ExistingServiceImage();
        if (image is not null && !InstallerOperations.IsOwnedImage(image))
            throw new InvalidOperationException(
                "ARFastSNTP service is registered to an unknown executable. Refusing to modify it: " + image);
        return image;
    }

    internal static void Prepare(Action<string> report)
    {
        RequireAdministrator();
        string? oldImage = ReadOwnedService();
        Directory.CreateDirectory(RecoveryDirectory);
        // Do not overwrite a recovery snapshot left by a failed earlier upgrade.
        if (File.Exists(PreviousImageFile))
            throw new InvalidOperationException(
                "A previous installer recovery snapshot still exists. Resolve the prior failed " +
                "installation before trying again: " + RecoveryDirectory);

        bool recorded = false;
        bool wasRunning = oldImage is not null && InstallerOperations.IsServiceRunning();
        int previousStart = 2;
        if (oldImage is not null)
        {
            using var serviceKey = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\" + InstallerOperations.ServiceName);
            previousStart = Convert.ToInt32(serviceKey?.GetValue("Start", 2));
            if (previousStart is < 2 or > 4)
                throw new InvalidOperationException("Unexpected old Windows Service start mode.");
        }
        try
        {
            string? priorExecutable = ServiceImageIdentity.ExtractExecutable(oldImage);
            string? worker = priorExecutable is not null && File.Exists(priorExecutable)
                ? priorExecutable : null;
            if (oldImage is not null && worker is null)
                throw new FileNotFoundException(
                    "Registered ARFastSNTP service has no owned executable. Repair manually before upgrade.");

            if (worker is not null)
            {
                report("Saving previous worker executable for automatic rollback...");
                File.Copy(worker, PreviousWorkerFile, overwrite: true);
            }

            // Save original SCM start type and whether it was running. A
            // failed upgrade must not silently turn a disabled/stopped service on.
            string stateTemp = PreviousStateFile + ".new";
            File.WriteAllText(stateTemp, $"{(wasRunning ? 1 : 0)}|{previousStart}",
                new UTF8Encoding(false));
            File.Move(stateTemp, PreviousStateFile, overwrite: true);

            // Commit the prepare manifest LAST; Abort ignores incomplete stages.
            string temp = PreviousImageFile + ".new";
            File.WriteAllText(temp, oldImage ?? NewMarker, new UTF8Encoding(false));
            File.Move(temp, PreviousImageFile, overwrite: true);
            recorded = true;

            if (oldImage is not null && wasRunning)
            {
                report("Stopping previously installed ARFastSNTP service before file replacement...");
                InstallerOperations.RunSc(new[] { "stop", InstallerOperations.ServiceName }, true, report);
                if (!InstallerOperations.WaitForState(1))
                    throw new TimeoutException("Windows Service did not stop in time.");
            }

            report(oldImage is null
                ? "No prior ARFastSNTP service; clean installation ready."
                : "Existing owned service stopped. Previous configuration and worker preserved.");
        }
        catch
        {
            // When stopping failed, Inno cannot overwrite the still-running service.
            // Keep recovery data only if the state might have changed.
            if (recorded && oldImage is not null && !InstallerOperations.IsServiceRunning())
                TryRestore(oldImage, report);
            else
                Cleanup(report);
            throw;
        }
    }

    internal static void Activate(SetupArguments options, Action<string> report)
    {
        RequireAdministrator();
        if (!File.Exists(PreviousImageFile))
            throw new InvalidOperationException(
                "Inno service activation missing prior preflight/recovery snapshot.");
        string? previousImage = ReadSavedImage();
        string? currentImage = ReadOwnedService();

        if (!File.Exists(InstallerOperations.InstalledWorker))
            throw new FileNotFoundException(
                "Installer did not deploy the worker: " + InstallerOperations.InstalledWorker);
        string studioPath = Path.Combine(InstallerOperations.InstallRoot,
            "studio", "ARFastSNTP.Studio.exe");
        if (!File.Exists(studioPath))
            throw new FileNotFoundException("Installer did not deploy Avalonia Studio: " + studioPath);

        string[] validation = new[]
        {
            "--server", options.Server.ToString(),
            "--server-year", options.ServerYear.ToString(),
            "--check-config"
        }.Concat(options.Lab ? new[] { "--certification-mode", "--allow-step" } :
            Array.Empty<string>()).ToArray();

        bool registeredHere = false;
        try
        {
            InstallerOperations.RunProcess(InstallerOperations.InstalledWorker, validation,
                mustSucceed: true, report);
            string desired = InstallerOperations.BuildImagePath(options);
            if (currentImage is null)
            {
                report("Registering Windows Service (automatic start)...");
                InstallerOperations.RunSc(new[]
                    { "create", InstallerOperations.ServiceName, "binPath=", desired,
                      "start=", "auto", "DisplayName=", "AR Fast SNTP" }, true, report);
                registeredHere = true;
            }
            else
            {
                report("Updating existing Windows Service in place; no uninstall...");
                InstallerOperations.RunSc(new[]
                    { "config", InstallerOperations.ServiceName, "binPath=", desired,
                      "start=", "auto" }, true, report);
            }
            InstallerOperations.RunSc(new[] { "start", InstallerOperations.ServiceName }, true, report);
            if (!InstallerOperations.WaitForState(4, timeoutMs: 12000))
                throw new TimeoutException(
                    "ARFastSNTP service did not report RUNNING within 12 seconds.");
            if (!string.Equals(InstallerOperations.ExistingServiceImage(),
                desired, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Service ImagePath does not match the configured SNTP server/mode.");

            report("SUCCESS: service RUNNING, startup=Automatic, mode=" +
                (options.Lab ? "CLOCK SYNC" : "MONITOR") + ", source=" + options.Server + ":123");
            // Running ≠ synchronized. The installer records verified health
            // separately for the final wizard; non-response is not a failed install.
            try
            {
                var health = options.Lab
                    ? SourceServiceControl.WaitForFreshResult(TimeSpan.FromSeconds(8), report)
                    : SourceServiceControl.Health.Waiting;
                SourceServiceControl.WriteInstallResult(health);
            }
            catch (Exception healthError)
            {
                report("Service active; sync check unavailable: " + healthError.Message);
                SourceServiceControl.WriteInstallResult(SourceServiceControl.Health.Waiting);
            }
            // Cosmetic cleanup is deliberately non-fatal to the validated service.
            try { LegacyShortcutCleanup.Reconcile(report); }
            catch (Exception ex) { report("Legacy shortcut cleanup skipped: " + ex.Message); }
            Cleanup(report);
        }
        catch (Exception ex)
        {
            report("ACTIVATION FAILED: " + ex.Message);
            try
            {
                if (InstallerOperations.IsServiceRunning())
                {
                    InstallerOperations.RunSc(new[] { "stop", InstallerOperations.ServiceName },
                        false, report);
                    InstallerOperations.WaitForState(1);
                }
                if (registeredHere && previousImage is null)
                {
                    InstallerOperations.RunSc(
                        new[] { "delete", InstallerOperations.ServiceName }, false, report);
                }
                if (previousImage is not null)
                    TryRestore(previousImage, report);
            }
            catch (Exception rollbackError)
            {
                report("RECOVERY ERROR: " + rollbackError.Message +
                    " — previous executable remains in " + RecoveryDirectory);
            }
            throw;
        }
    }

    private static string? ReadSavedImage()
    {
        string value = File.ReadAllText(PreviousImageFile).Trim();
        if (value == NewMarker) return null;
        if (!InstallerOperations.IsOwnedImage(value))
            throw new InvalidOperationException("Invalid/unknown previous service image in recovery snapshot.");
        return value;
    }

    private static void TryRestore(string previousImage, Action<string> report)
    {
        if (!InstallerOperations.IsOwnedImage(previousImage))
            throw new InvalidOperationException("Refusing to restore an unowned service.");
        if (!File.Exists(PreviousWorkerFile))
            throw new FileNotFoundException("Previous worker backup missing.", PreviousWorkerFile);

        if (InstallerOperations.IsServiceRunning())
        {
            InstallerOperations.RunSc(new[] { "stop", InstallerOperations.ServiceName }, true, report);
            if (!InstallerOperations.WaitForState(1))
                throw new TimeoutException("Service still running: cannot restore worker executable.");
        }
        string originalWorker = ServiceImageIdentity.ExtractExecutable(previousImage)
            ?? throw new InvalidOperationException("Stored service executable failed identity validation.");
        // Legacy PowerShell and WinForms releases may have used ProgramFiles(x86)
        // or the historic ARFastSNTP root. Restore to the ORIGINAL executable.
        File.Copy(PreviousWorkerFile, originalWorker, overwrite: true);
        (bool wasRunning, int startType) = ReadPreviousState();
        string startMode = startType == 2 ? "auto" : "demand";
        InstallerOperations.RunSc(new[]
            { "config", InstallerOperations.ServiceName, "binPath=", previousImage,
              "start=", startMode }, true, report);
        if (wasRunning)
        {
            InstallerOperations.RunSc(new[]
                { "start", InstallerOperations.ServiceName }, true, report);
            if (!InstallerOperations.WaitForState(4))
                throw new TimeoutException("Previous service did not restart after recovery.");
        }
        if (startType == 4)
            InstallerOperations.RunSc(new[]
                { "config", InstallerOperations.ServiceName, "start=", "disabled" }, true, report);
        report("RECOVERED: previous executable, start mode and running state restored.");
        Cleanup(report);
    }

    private static (bool WasRunning, int StartType) ReadPreviousState()
    {
        string[] parts = File.ReadAllText(PreviousStateFile).Trim().Split('|');
        if (parts.Length != 2 || (parts[0] != "0" && parts[0] != "1") ||
            !int.TryParse(parts[1], out int startType) || startType is < 2 or > 4)
            throw new InvalidOperationException("Installer recovery state was corrupted.");
        return (parts[0] == "1", startType);
    }

    private static void Cleanup(Action<string> report)
    {
        try
        {
            if (File.Exists(PreviousImageFile)) File.Delete(PreviousImageFile);
            if (File.Exists(PreviousWorkerFile)) File.Delete(PreviousWorkerFile);
            if (File.Exists(PreviousStateFile)) File.Delete(PreviousStateFile);
            if (File.Exists(PreviousStateFile + ".new")) File.Delete(PreviousStateFile + ".new");
            if (File.Exists(PreviousImageFile + ".new")) File.Delete(PreviousImageFile + ".new");
            if (Directory.Exists(RecoveryDirectory) &&
                !Directory.EnumerateFileSystemEntries(RecoveryDirectory).Any())
                Directory.Delete(RecoveryDirectory);
        }
        catch (IOException ex) { report("Recovery cleanup deferred: " + ex.Message); }
        catch (UnauthorizedAccessException ex) { report("Recovery cleanup deferred: " + ex.Message); }
    }

    /// <summary>
    /// Called when Inno aborts/cancels after preflight. No effect after a
    /// successful installation (the snapshot has already been removed).
    /// </summary>
    internal static void Abort(Action<string> report)
    {
        RequireAdministrator();
        if (!File.Exists(PreviousImageFile))
            return;
        string? previous = ReadSavedImage();
        if (previous is not null)
        {
            TryRestore(previous, report);
            return;
        }
        string? current = ReadOwnedService();
        if (current is not null)
        {
            if (InstallerOperations.IsServiceRunning())
            {
                InstallerOperations.RunSc(new[]
                    { "stop", InstallerOperations.ServiceName }, true, report);
                if (!InstallerOperations.WaitForState(1))
                    throw new TimeoutException("Cannot stop incomplete new service.");
            }
            InstallerOperations.RunSc(new[]
                { "delete", InstallerOperations.ServiceName }, true, report);
        }
        Cleanup(report);
        report("Cancelled installation: no previous service to restore.");
    }

    internal static void Uninstall(Action<string> report)
    {
        RequireAdministrator();
        string? image = ReadOwnedService();
        if (image is null)
        {
            report("No registered ARFastSNTP service to remove.");
            return;
        }
        if (InstallerOperations.IsServiceRunning())
        {
            InstallerOperations.RunSc(new[]
                { "stop", InstallerOperations.ServiceName }, true, report);
            if (!InstallerOperations.WaitForState(1))
                throw new TimeoutException("Service did not stop. Uninstall aborted to preserve binaries.");
        }
        InstallerOperations.RunSc(new[]
            { "delete", InstallerOperations.ServiceName }, true, report);
        report("Service unregistered. Inno will remove application binaries; log/history remain intact.");
    }

    // Offline pure checks run under CI; no Windows Service or system clock mutation.
    internal static bool SelfTest()
    {
        string canonical = InstallerOperations.InstalledWorker;
        string currentImage = "\"" + canonical + "\" --server 192.16.1.175";
        string x86 = Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.ProgramFilesX86), "ARFastSNTP", "worker", "ARFastSNTP.exe");
        string oldRoot = Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.ProgramFiles), "ARFastSNTP", "ARFastSNTP.exe");
        string developer = Path.Combine(
            Path.GetPathRoot(InstallerOperations.InstallRoot) ?? @"C:\", "arintp", "worker", "ARFastSNTP.exe");
        bool classifier =
            ServiceImageIdentity.Classify(currentImage) ==
                ServiceImageIdentity.Layout.CurrentProgramFiles &&
            ServiceImageIdentity.IsOwned("\"" + x86 + "\" --server 192.16.1.175") &&
            ServiceImageIdentity.IsOwned("\"" + oldRoot + "\" --server 192.16.1.175") &&
            !ServiceImageIdentity.IsOwned("\"" + developer + "\" --server 192.16.1.175") &&
            !ServiceImageIdentity.IsOwned(@"""C:\OtherApp\worker\ARFastSNTP.exe"" --server 127.0.0.1") &&
            !ServiceImageIdentity.IsOwned(@"""C:\Program Files\ARFastSNTP\worker\ARFastSNTP.exe.bad"" --server 127.0.0.1") &&
            !ServiceImageIdentity.IsOwned(@"""C:\Program Files\ARFastSNTP\worker\ntpd.exe""");
        if (!classifier) return false;
        string oldDashboard = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "ARFastSNTP", "dashboard", "ARFastSNTP.Dashboard.exe");
        if (!LegacyShortcutCleanup.IsKnownOldDashboard(oldDashboard) ||
            LegacyShortcutCleanup.IsKnownOldDashboard(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    "System32", "notepad.exe")))
            return false;
        var sync = SetupArguments.Parse(new[] { "--server", "192.16.1.175",
            "--year", "2026", "--lab" });
        var monitor = SetupArguments.Parse(new[] { "--server", "192.16.1.175",
            "--year", "2026", "--monitor-only" });
        var active = InstallerOperations.BuildImagePath(sync);
        return InstallerOperations.IsOwnedImage(active) &&
            InstallerOperations.ImageHasLabMode(active) &&
            !InstallerOperations.ImageHasLabMode(InstallerOperations.BuildImagePath(monitor)) &&
            active.Contains("--server 192.16.1.175", StringComparison.Ordinal);
    }
}

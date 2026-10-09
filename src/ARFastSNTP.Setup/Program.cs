using System.Windows.Forms;

namespace ARFastSNTP.Setup;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Does not launch a window, require elevation, register services, or use the network.
        if (args.Length == 1 && args[0] == "--self-test")
        {
            try
            {
                var defaults = SetupArguments.Parse(Array.Empty<string>());
                var sync = SetupArguments.Parse(new[]
                    { "--server", "192.16.1.175", "--year", "2026", "--lab", "--auto-apply" });
                var monitor = SetupArguments.Parse(new[]
                    { "--server", "192.16.1.175", "--year", "2026", "--monitor-only" });
                bool valid = defaults.Lab &&
                    sync.Lab && sync.AutoApply && sync.CommandLine().Contains("--auto-apply") &&
                    monitor.Lab == false && monitor.CommandLine().Contains("--monitor-only") &&
                    sync.Server.ToString() == "192.16.1.175" &&
                    sync.ServerYear == 2026;
                try
                {
                    SetupArguments.Parse(new[] { "--server", "not-an-ip" });
                    valid = false;
                }
                catch (ArgumentException) { }
                string syncImage = InstallerOperations.BuildImagePath(sync);
                string monitorImage = InstallerOperations.BuildImagePath(monitor);
                valid &= InstallerOperations.IsOwnedImage(syncImage) &&
                    InstallerOperations.ImageHasLabMode(syncImage) &&
                    !InstallerOperations.ImageHasLabMode(monitorImage) &&
                    InstallerOperations.ServiceModeLabel(syncImage) == "LAB CLOCK CORRECTION" &&
                    InstallerOperations.ServiceModeLabel(monitorImage) == "MONITOR ONLY";
                Environment.ExitCode = valid ? 0 : 3;
            }
            catch { Environment.ExitCode = 3; }
            return;
        }

        SetupArguments options;
        try { options = SetupArguments.Parse(args); }
        catch (ArgumentException ex)
        {
            MessageBox.Show(ex.Message, "AR Fast SNTP Setup - invalid argument",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            Environment.ExitCode = 2;
            return;
        }

        if (options.VerifyPackage)
        {
            // Offline packaging check: NO elevation, NO service install, NO NTP traffic.
            try
            {
                InstallerOperations.ValidateBundle(AppContext.BaseDirectory);
                Environment.ExitCode = 0;
            }
            catch (Exception ex)
            {
                // Windows-subsystem EXE has no reliable console stderr.
                // Keep a deterministic diagnostic beside the bundle for CI and users.
                try
                {
                    File.WriteAllText(Path.Combine(AppContext.BaseDirectory,
                        "setup-validation-error.txt"), ex.ToString());
                }
                catch (Exception) { }
                Environment.ExitCode = 2;
            }
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new SetupForm(options));
    }
}

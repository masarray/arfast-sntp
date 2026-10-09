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
                var optionsToTest = SetupArguments.Parse(new[]
                {
                    "--server", "192.16.1.175", "--year", "2026", "--lab"
                });
                bool valid = optionsToTest.Lab &&
                    optionsToTest.CommandLine().Contains("--lab") &&
                    optionsToTest.Server.ToString() == "192.16.1.175" &&
                    optionsToTest.ServerYear == 2026;
                try
                {
                    SetupArguments.Parse(new[] { "--server", "not-an-ip" });
                    valid = false;
                }
                catch (ArgumentException) { }
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
            catch (Exception)
            {
                Environment.ExitCode = 2;
            }
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new SetupForm(options));
    }
}

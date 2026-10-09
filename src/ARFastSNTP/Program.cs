using System.Diagnostics;
using ARFastSNTP;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// If someone double-clicks the worker EXE instead of the dashboard,
// open the dashboard rather than flashing and closing the console window.
if (args.Length == 0)
{
    if (!Environment.UserInteractive)
    {
        Console.Error.WriteLine("Missing --server IPv4 parameter for unattended service.");
        Environment.ExitCode = 2;
        return;
    }
    string dashboard = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
        "..", "dashboard", "ARFastSNTP.Dashboard.exe"));
    if (File.Exists(dashboard))
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(dashboard)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(dashboard)!
            });
            return;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            Console.Error.WriteLine($"Dashboard launch failed: {ex.Message}");
        }
    }
    Console.WriteLine("AR Fast SNTP worker expects --server <IPv4>.");
    Console.WriteLine("Open dashboard\\ARFastSNTP.Dashboard.exe to enter your SNTP server IP.");
    Console.WriteLine(SyncOptions.Usage);
    if (!Console.IsInputRedirected)
    {
        Console.WriteLine("Press Enter to exit...");
        Console.ReadLine();
    }
    return;
}

if (args.Contains("--help", StringComparer.OrdinalIgnoreCase))
{
    Console.WriteLine(SyncOptions.Usage);
    return;
}

bool checkOnly = args.Contains("--check-config", StringComparer.OrdinalIgnoreCase);
string[] workerArgs = args.Where(argument => argument != "--check-config").ToArray();
SyncOptions options;
try
{
    options = SyncOptions.Parse(workerArgs);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine(SyncOptions.Usage);
    Environment.ExitCode = 2;
    return;
}

if (checkOnly)
{
    Console.WriteLine($"Configuration valid: server={options.Server}, year={options.ServerYear}, " +
        $"mode={(options.AllowStep ? "CERTIFICATION STEP" : "MONITOR ONLY")}");
    return; // Smoke-check only; no network access or Windows time changes.
}

await Host.CreateDefaultBuilder()
    .UseWindowsService(service => service.ServiceName = ServiceIdentity.Name)
    .ConfigureServices((_, services) =>
        services.AddHostedService(provider =>
            new SyncWorker(options, provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SyncWorker>>())))
    .Build()
    .RunAsync();

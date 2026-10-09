using ARFastSNTP;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
    .UseWindowsService(service => service.ServiceName = "AR Fast SNTP")
    .ConfigureServices((_, services) =>
        services.AddHostedService(provider =>
            new SyncWorker(options, provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SyncWorker>>())))
    .Build()
    .RunAsync();

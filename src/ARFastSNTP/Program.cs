using ARFastSNTP;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

if (args.Contains("--help", StringComparer.OrdinalIgnoreCase))
{
    Console.WriteLine(SyncOptions.Usage);
    return;
}

SyncOptions options;
try
{
    options = SyncOptions.Parse(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine(SyncOptions.Usage);
    Environment.ExitCode = 2;
    return;
}

await Host.CreateDefaultBuilder(args)
    .UseWindowsService(service => service.ServiceName = "AR Fast SNTP")
    .ConfigureServices((_, services) =>
        services.AddHostedService(provider =>
            new SyncWorker(options, provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SyncWorker>>())))
    .Build()
    .RunAsync();

using System.Net;
using System.Net.Sockets;

namespace ARFastSNTP;

internal sealed record SyncOptions(IPAddress Server, bool AllowStep, int ServerYear, string StatusPath)
{
    internal const string Usage =
        "ARFastSNTP --server <IPv4> [--server-year 2026] [--status-file <path>] " +
        "[--certification-mode --allow-step] [--check-config]\n" +
        "MONITOR ONLY by default. Clock stepping requires both certification flags.";

    internal static SyncOptions Parse(string[] args)
    {
        string? address = null, file = null;
        int year = 2026;
        bool allowStep = false, certificationMode = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--server":
                    if (++i >= args.Length) throw new ArgumentException("Missing --server value.");
                    address = args[i];
                    break;
                case "--server-year":
                    if (++i >= args.Length || !int.TryParse(args[i], out year))
                        throw new ArgumentException("Invalid --server-year value.");
                    break;
                case "--status-file":
                    if (++i >= args.Length) throw new ArgumentException("Missing --status-file path.");
                    file = args[i];
                    break;
                case "--allow-step": allowStep = true; break;
                case "--certification-mode": certificationMode = true; break;
                default: throw new ArgumentException($"Unknown argument {args[i]}");
            }
        }
        if (!IPAddress.TryParse(address, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
            throw new ArgumentException("Specify --server followed by an IPv4 address.");
        if (year < 2020 || year > 2090) throw new ArgumentException("Server year must be 2020..2090.");
        if (allowStep && !certificationMode)
            throw new ArgumentException("--allow-step requires --certification-mode.");
        if (allowStep && !OperatingSystem.IsWindows())
            throw new ArgumentException("Clock setting is supported only on Windows.");
        file ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "ARFastSNTP", "status.json");
        return new SyncOptions(ip, allowStep, year, Path.GetFullPath(file));
    }
}

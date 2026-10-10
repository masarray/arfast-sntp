using System.Net;
using System.Net.Sockets;

namespace ARFastSNTP.Setup;

internal sealed record SetupArguments(
    IPAddress Server, int ServerYear, bool Lab, bool Remove, bool VerifyPackage,
    bool AutoApply = false)
{
    internal static SetupArguments Parse(string[] args)
    {
        string ip = "192.16.1.175";
        int year = 2026;
        bool sync = true, remove = false, verify = false, autoApply = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--server":
                    if (++i >= args.Length) throw new ArgumentException("Missing --server IPv4 value.");
                    ip = args[i];
                    break;
                case "--year":
                    if (++i >= args.Length || !int.TryParse(args[i], out year))
                        throw new ArgumentException("Invalid --year argument.");
                    break;
                case "--lab": sync = true; break;
                case "--monitor-only": sync = false; break;
                case "--auto-apply": autoApply = true; break;
                case "--remove": remove = true; break;
                case "--verify-package": verify = true; break;
                default: throw new ArgumentException($"Unsupported setup option: {args[i]}");
            }
        }
        if (!IPAddress.TryParse(ip, out var server) ||
            server.AddressFamily != AddressFamily.InterNetwork)
            throw new ArgumentException("Please supply a valid IPv4 SNTP server address.");
        if (year < 2020 || year > 2090)
            throw new ArgumentException("Server reference year must be 2020..2090.");
        return new SetupArguments(server, year, sync, remove, verify, autoApply);
    }

    internal string[] CommandLine(bool remove = false) =>
        new[] { "--server", Server.ToString(), "--year", ServerYear.ToString() }
            .Concat(Lab ? new[] { "--lab" } : new[] { "--monitor-only" })
            .Concat(remove ? new[] { "--remove" } : Array.Empty<string>())
            .Concat(AutoApply ? new[] { "--auto-apply" } : Array.Empty<string>())
            .ToArray();
}

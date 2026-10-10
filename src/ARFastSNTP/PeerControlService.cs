using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ARFastSNTP;

/// <summary>Service-hosted peer mutations, authenticated by the local
/// Windows named-pipe DACL. GUI never edits the service-owned config file.</summary>
internal sealed class PeerControlService : BackgroundService
{
    private readonly PeerCatalog _catalog;
    private readonly ILogger<PeerControlService> _logger;

    internal PeerControlService(PeerCatalog catalog, ILogger<PeerControlService> logger)
    {
        _catalog = catalog;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!OperatingSystem.IsWindows()) return;
        var acl = new PipeSecurity();
        acl.SetAccessRuleProtection(true, false);
        acl.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        acl.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        acl.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.InteractiveSid, null),
            PipeAccessRights.ReadWrite, AccessControlType.Allow));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var pipe = NamedPipeServerStreamAcl.Create(
                    PeerControlProtocol.PipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 1024, 1024, acl);
                await pipe.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                PeerControlProtocol.Response result;
                try
                {
                    var command = await PeerControlProtocol.ReadAsync<PeerControlProtocol.Request>(
                        pipe, timeout.Token).ConfigureAwait(false);
                    if (!PeerControlProtocol.IsAction(command.Action))
                        throw new ArgumentException("Unsupported NTP server action.");
                    NtpPeer[] peers = command.Action == "list"
                        ? _catalog.Snapshot().Peers
                        : _catalog.Mutate(command.Action, command.Address);
                    result = new PeerControlProtocol.Response(true, null, peers);
                    if (command.Action != "list")
                        _logger.LogInformation("NTP server list updated: {Operation} {Address}",
                            command.Action, command.Address);
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
                    or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
                {
                    result = new PeerControlProtocol.Response(false, ex.Message,
                        _catalog.Snapshot().Peers);
                }
                await pipe.WriteAsync(PeerControlProtocol.Encode(result), timeout.Token)
                    .ConfigureAwait(false);
                await pipe.FlushAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or
                InvalidOperationException or UnauthorizedAccessException or
                System.Security.SecurityException)
            {
                _logger.LogWarning("Peer control request skipped: {Error}", ex.Message);
                try { await Task.Delay(150, stoppingToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
}

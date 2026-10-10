using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ARFastSNTP;

/// <summary>
/// Local-only, ACL-protected service control. The operating system authenticates
/// interactive session membership before any command is read. Requests contain
/// exactly two bytes; responses are exactly three. No generic command execution.
/// </summary>
internal sealed class SourceControlPipeService : BackgroundService
{
    private readonly SourceControlState _state;
    private readonly ILogger<SourceControlPipeService> _logger;

    internal SourceControlPipeService(SourceControlState state,
        ILogger<SourceControlPipeService> logger)
    {
        _state = state;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!OperatingSystem.IsWindows())
            return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // ACL is protected: no inherited Everyone/Network/Anonymous ACE.
                var security = new PipeSecurity();
                security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
                security.AddAccessRule(new PipeAccessRule(
                    new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                    PipeAccessRights.FullControl, AccessControlType.Allow));
                security.AddAccessRule(new PipeAccessRule(
                    new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                    PipeAccessRights.FullControl, AccessControlType.Allow));
                security.AddAccessRule(new PipeAccessRule(
                    new SecurityIdentifier(WellKnownSidType.InteractiveSid, null),
                    PipeAccessRights.ReadWrite, AccessControlType.Allow));

                using var pipe = NamedPipeServerStreamAcl.Create(
                    SourceControlProtocol.PipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 64, 64, security);
                await pipe.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);
                // A stalled/malicious local client must not pin the control
                // server indefinitely. Requests cannot allocate unbounded input.
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                deadline.CancelAfter(TimeSpan.FromSeconds(2));
                var request = new byte[2];
                await pipe.ReadExactlyAsync(request, deadline.Token).ConfigureAwait(false);

                byte[] response;
                if (SourceControlProtocol.TryDecode(request, out byte command))
                {
                    try
                    {
                        bool enabled = command switch
                        {
                            SourceControlProtocol.Enable => _state.SetEnabled(true),
                            SourceControlProtocol.Disable => _state.SetEnabled(false),
                            _ => _state.Enabled
                        };
                        response = SourceControlProtocol.Reply(enabled);
                        if (command != SourceControlProtocol.Query)
                            _logger.LogInformation("NTP source changed: enabled={Enabled}", enabled);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                        or System.Security.SecurityException)
                    {
                        _logger.LogWarning("NTP source change refused: {Error}", ex.Message);
                        response = SourceControlProtocol.Reply(_state.Enabled, false);
                    }
                }
                else
                    response = SourceControlProtocol.Reply(_state.Enabled, false);
                await pipe.WriteAsync(response, deadline.Token).ConfigureAwait(false);
                await pipe.FlushAsync(deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                // Client timed out; the next command gets a new pipe instance.
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or InvalidOperationException or System.Security.SecurityException)
            {
                _logger.LogWarning("NTP local control unavailable: {Error}", ex.Message);
                // Prevent a failing ACL/pipe from turning into a busy loop.
                try { await Task.Delay(1000, stoppingToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
}

using System.IO.Pipes;

namespace ARFastSNTP;

/// <summary>
/// Fixed-width local control protocol. Deliberately no arbitrary command,
/// path, IP address or executable content can cross this privilege boundary.
/// The worker's Windows pipe ACL authorizes only local INTERACTIVE users.
/// </summary>
internal static class SourceControlProtocol
{
    internal const string PipeName = "ARFastSNTP.SourceControl.v1";
    internal const string SettingsKey = @"SOFTWARE\ARFastSNTP";
    internal const string SettingName = "SourceEnabled";
    internal const byte Version = 1;
    internal const byte Disable = 0;
    internal const byte Enable = 1;
    internal const byte Query = 2;
    internal const byte Ok = 0;
    internal const byte Rejected = 1;

    internal static bool IsCommand(byte command) =>
        command is Disable or Enable or Query;

    internal static bool TryDecode(ReadOnlySpan<byte> request, out byte command)
    {
        command = 255;
        if (request.Length != 2 || request[0] != Version ||
            !IsCommand(request[1])) return false;
        command = request[1];
        return true;
    }

    internal static byte[] Reply(bool enabled, bool ok = true) =>
        new byte[] { Version, ok ? Ok : Rejected, enabled ? (byte)1 : (byte)0 };

    internal static bool TryDecodeReply(ReadOnlySpan<byte> reply, out bool enabled)
    {
        enabled = false;
        if (reply.Length != 3 || reply[0] != Version ||
            reply[1] != Ok || reply[2] > 1) return false;
        enabled = reply[2] == 1;
        return true;
    }

    internal static async Task<bool> SendAsync(byte command, CancellationToken cancellationToken)
    {
        if (!IsCommand(command))
            throw new ArgumentOutOfRangeException(nameof(command));
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(TimeSpan.FromSeconds(2));
        using var client = new NamedPipeClientStream(".", PipeName,
            PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(1100, limit.Token).ConfigureAwait(false);
        await client.WriteAsync(new byte[] { Version, command }, limit.Token).ConfigureAwait(false);
        await client.FlushAsync(limit.Token).ConfigureAwait(false);
        byte[] response = new byte[3];
        await client.ReadExactlyAsync(response, limit.Token).ConfigureAwait(false);
        if (!TryDecodeReply(response, out bool enabled))
            throw new InvalidDataException("Windows service rejected NTP source control.");
        return enabled;
    }
}

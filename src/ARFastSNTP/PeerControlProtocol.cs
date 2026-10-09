using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text.Json;

namespace ARFastSNTP;

/// <summary>
/// Local-only peer management protocol. Length-prefixed, bounded, and
/// intentionally restricted to list/add/delete/enable/disable IPv4 peers.
/// </summary>
internal static class PeerControlProtocol
{
    internal const string PipeName = "ARFastSNTP.PeerControl.v1";
    internal const int MaxMessage = 8192;

    internal sealed record Request(string Action, string? Address = null);
    internal sealed record Response(bool Ok, string? Error, NtpPeer[] Peers);

    internal static bool IsAction(string? action) =>
        action is "list" or "add" or "delete" or "enable" or "disable";

    internal static byte[] Encode<T>(T message)
    {
        byte[] content = JsonSerializer.SerializeToUtf8Bytes(message);
        if (content.Length == 0 || content.Length > MaxMessage)
            throw new InvalidDataException("NTP control message exceeds limit.");
        byte[] framed = new byte[content.Length + 2];
        BinaryPrimitives.WriteUInt16LittleEndian(framed.AsSpan(0, 2),
            checked((ushort)content.Length));
        content.CopyTo(framed.AsSpan(2));
        return framed;
    }

    internal static async Task<T> ReadAsync<T>(Stream stream, CancellationToken token)
    {
        byte[] len = new byte[2];
        await stream.ReadExactlyAsync(len, token).ConfigureAwait(false);
        int size = BinaryPrimitives.ReadUInt16LittleEndian(len);
        if (size is < 2 or > MaxMessage)
            throw new InvalidDataException("Invalid NTP control message length.");
        byte[] data = new byte[size];
        await stream.ReadExactlyAsync(data, token).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(data) ??
            throw new InvalidDataException("Invalid NTP control message JSON.");
    }

    internal static async Task<Response> SendAsync(
        string action, string? address, CancellationToken token)
    {
        if (!IsAction(action)) throw new ArgumentException("Invalid NTP operation.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        using var client = new NamedPipeClientStream(".", PipeName,
            PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(1500, timeout.Token).ConfigureAwait(false);
        await client.WriteAsync(Encode(new Request(action, address)), timeout.Token)
            .ConfigureAwait(false);
        await client.FlushAsync(timeout.Token).ConfigureAwait(false);
        return await ReadAsync<Response>(client, timeout.Token).ConfigureAwait(false);
    }
}

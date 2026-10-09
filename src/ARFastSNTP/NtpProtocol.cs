using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace ARFastSNTP;

internal readonly record struct SntpSample(TimeSpan Offset, double RttMs, byte Stratum);

internal static class NtpProtocol
{
    private static readonly DateTimeOffset Epoch = new(1900, 1, 1, 0, 0, 0, TimeSpan.Zero);

    internal static void WriteTimestamp(Span<byte> bytes, DateTimeOffset instant)
    {
        long ticks = (instant - Epoch).Ticks;
        BinaryPrimitives.WriteUInt32BigEndian(bytes[..4],
            unchecked((uint)(ticks / TimeSpan.TicksPerSecond)));
        BinaryPrimitives.WriteUInt32BigEndian(bytes[4..],
            (uint)(((ulong)(ticks % TimeSpan.TicksPerSecond) << 32) / TimeSpan.TicksPerSecond));
    }

    internal static SntpSample Parse(ReadOnlySpan<byte> request, ReadOnlySpan<byte> bytes,
        DateTimeOffset t1, DateTimeOffset t4, int serverYear)
    {
        if (bytes.Length < 48 || request.Length < 48)
            throw new InvalidOperationException("Truncated NTP message.");
        int mode = bytes[0] & 7, version = (bytes[0] >> 3) & 7, leap = bytes[0] >> 6;
        if (mode != 4 || version is < 3 or > 4 || leap == 3)
            throw new InvalidOperationException("Peer is not a synchronized unicast NTP server.");
        if (bytes[1] == 0)
            throw new InvalidOperationException("NTP Kiss-o'-Death / server unavailable (backoff required).");
        if (bytes[1] > 15) throw new InvalidOperationException("Invalid server stratum.");
        if (!bytes.Slice(24, 8).SequenceEqual(request.Slice(40, 8)))
            throw new InvalidOperationException("Unmatched NTP originate timestamp.");
        var reference = new DateTimeOffset(serverYear, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var t2 = Read(bytes.Slice(32, 8), reference);
        var t3 = Read(bytes.Slice(40, 8), reference);
        // The reference is operator configured, NOT the potentially corrupted PC clock.
        if ((t2 - reference).Duration() > TimeSpan.FromDays(365 * 15) ||
            (t3 - reference).Duration() > TimeSpan.FromDays(365 * 15))
            throw new InvalidOperationException("Server timestamp outside configured trusted year window.");
        if (t3 < t2 || t3 - t2 > TimeSpan.FromSeconds(2))
            throw new InvalidOperationException("Invalid NTP server processing interval.");
        var networkDelay = (t4 - t1) - (t3 - t2);
        if (networkDelay < TimeSpan.FromMilliseconds(-2) ||
            networkDelay > TimeSpan.FromMilliseconds(250))
            throw new InvalidOperationException("Unacceptable NTP network delay.");
        return new SntpSample(TimeSpan.FromTicks(((t2 - t1).Ticks + (t3 - t4).Ticks) / 2),
            Math.Max(0, networkDelay.TotalMilliseconds), bytes[1]);
    }

    private static DateTimeOffset Read(ReadOnlySpan<byte> bytes, DateTimeOffset reference)
    {
        uint seconds = BinaryPrimitives.ReadUInt32BigEndian(bytes[..4]);
        uint fraction = BinaryPrimitives.ReadUInt32BigEndian(bytes[4..]);
        if (seconds == 0 && fraction == 0)
            throw new InvalidOperationException("Zero NTP server timestamp.");
        return NtpEra.Resolve(seconds, fraction, reference);
    }

    internal static async Task<SntpSample> QueryAsync(IPAddress server, int serverYear,
        CancellationToken cancellationToken)
    {
        using var socket = new UdpClient(AddressFamily.InterNetwork);
        socket.Connect(new IPEndPoint(server, 123));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        byte[] request = new byte[48];
        request[0] = 0x23; // LI 0, version 4, client mode 3.
        DateTimeOffset t1 = DateTimeOffset.UtcNow;
        WriteTimestamp(request.AsSpan(40, 8), t1);
        var timer = Stopwatch.StartNew();
        await socket.SendAsync(request, timeout.Token);
        UdpReceiveResult result = await socket.ReceiveAsync(timeout.Token);
        timer.Stop();
        DateTimeOffset actualReceive = DateTimeOffset.UtcNow;
        if (ClockDiscontinuity.Detected(t1, actualReceive, timer.Elapsed))
            throw new InvalidOperationException("Windows clock jumped during request.");
        DateTimeOffset t4 = t1 + timer.Elapsed;
        if (!result.RemoteEndPoint.Address.Equals(server) || result.RemoteEndPoint.Port != 123)
            throw new InvalidOperationException("Unexpected NTP response source.");
        return Parse(request, result.Buffer, t1, t4, serverYear);
    }
}

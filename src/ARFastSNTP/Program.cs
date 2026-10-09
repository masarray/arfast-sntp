using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

if (args.Length == 0 || args.Contains("--help"))
{
    Console.WriteLine("AR Fast SNTP --server <IPv4> [--allow-step]");
    return;
}
var index = Array.IndexOf(args, "--server");
if (index < 0 || index + 1 >= args.Length ||
    !IPAddress.TryParse(args[index + 1], out var ip) ||
    ip.AddressFamily != AddressFamily.InterNetwork)
{
    Console.Error.WriteLine("Specify --server followed by an IPv4 address.");
    Environment.ExitCode = 2;
    return;
}
var allowStep = args.Contains("--allow-step");
if (allowStep && !OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("Clock correction requires Windows.");
    return;
}
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
Console.WriteLine($"Server {ip}:123; mode {(allowStep ? "CERTIFICATION STEP" : "MONITOR ONLY")}");
while (!cts.IsCancellationRequested)
{
    try
    {
        var samples = new List<(TimeSpan offset, double rtt)>();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            samples.Add(await QueryAsync(ip, cts.Token));
            if (attempt < 2) await Task.Delay(150, cts.Token);
        }
        samples.Sort((a,b) => a.offset.CompareTo(b.offset));
        var median = samples[1];
        if ((samples[2].offset - samples[0].offset).Duration() > TimeSpan.FromMilliseconds(250))
            throw new InvalidOperationException("NTP samples disagree");
        Console.WriteLine($"{DateTimeOffset.Now:O} offset={median.offset.TotalMilliseconds:+0.000;-0.000;0}ms RTT={median.rtt:F2}ms");
        if (allowStep && median.offset.Duration() >= TimeSpan.FromMilliseconds(100))
        {
            if (!SetUtc(DateTimeOffset.UtcNow + median.offset))
                Console.Error.WriteLine($"SetSystemTime failed, Win32 error {Marshal.GetLastWin32Error()}");
            else Console.WriteLine("Clock stepped; verification follows.");
        }
        await Task.Delay(1000, cts.Token);
    }
    catch (OperationCanceledException) when (cts.IsCancellationRequested) { break; }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Rejected sample: {ex.Message}");
        try { await Task.Delay(1000, cts.Token); }
        catch (OperationCanceledException) { break; }
    }
}

static async Task<(TimeSpan offset, double rtt)> QueryAsync(IPAddress ip, CancellationToken token)
{
    using var socket = new UdpClient(AddressFamily.InterNetwork);
    socket.Connect(new IPEndPoint(ip,123));
    var request = new byte[48];
    request[0] = 0x23;
    var t1 = DateTimeOffset.UtcNow;
    WriteTimestamp(request.AsSpan(40,8), t1);
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
    timeout.CancelAfter(TimeSpan.FromSeconds(2));
    var clock = Stopwatch.StartNew();
    await socket.SendAsync(request, timeout.Token);
    var response = await socket.ReceiveAsync(timeout.Token);
    clock.Stop();
    var actualReceiveTime = DateTimeOffset.UtcNow;
    var t4 = t1 + clock.Elapsed;
    // A Windows clock step during the UDP exchange invalidates this sample.
    // Monotonic elapsed time must agree with wall-clock elapsed time.
    if (ARFastSNTP.ClockDiscontinuity.Detected(t1, actualReceiveTime, clock.Elapsed))
        throw new InvalidOperationException("Windows clock changed during NTP exchange");
    var bytes = response.Buffer;
    if (bytes.Length < 48 || !response.RemoteEndPoint.Address.Equals(ip))
        throw new InvalidOperationException("Invalid NTP source");
    var mode = bytes[0] & 7;
    var version = (bytes[0] >> 3) & 7;
    if ((bytes[0] >> 6) == 3 || mode != 4 || version is < 3 or > 4 || bytes[1] is < 1 or > 15)
        throw new InvalidOperationException("Unsynchronized NTP peer");
    if (!bytes.AsSpan(24,8).SequenceEqual(request.AsSpan(40,8)))
        throw new InvalidOperationException("Originate timestamp mismatch");
    var t2 = ReadTimestamp(bytes.AsSpan(32,8),t1);
    var t3 = ReadTimestamp(bytes.AsSpan(40,8),t1);
    if (t3 < t2) throw new InvalidOperationException("Invalid transmit time");
    var delay = (t4 - t1) - (t3 - t2);
    if (delay < TimeSpan.Zero || delay > TimeSpan.FromMilliseconds(250))
        throw new InvalidOperationException("Excessive network delay");
    return (TimeSpan.FromTicks(((t2-t1).Ticks + (t3-t4).Ticks)/2),delay.TotalMilliseconds);
}

static void WriteTimestamp(Span<byte> bytes, DateTimeOffset instant)
{
    var ticks = (instant - new DateTimeOffset(1900,1,1,0,0,0,TimeSpan.Zero)).Ticks;
    BinaryPrimitives.WriteUInt32BigEndian(bytes[..4],unchecked((uint)(ticks / TimeSpan.TicksPerSecond)));
    BinaryPrimitives.WriteUInt32BigEndian(bytes[4..],(uint)(((ulong)(ticks % TimeSpan.TicksPerSecond) << 32) / TimeSpan.TicksPerSecond));
}
static DateTimeOffset ReadTimestamp(ReadOnlySpan<byte> bytes, DateTimeOffset anchor)
{
    var sec = BinaryPrimitives.ReadUInt32BigEndian(bytes[..4]);
    var frac = BinaryPrimitives.ReadUInt32BigEndian(bytes[4..]);
    if (sec == 0 && frac == 0) throw new InvalidOperationException("Zero timestamp");
    // A validated NTP era reference is required before the decoder can be
    // trusted across an arbitrary multi-decade local clock jump.
    return ARFastSNTP.NtpEra.Resolve(sec, frac, anchor);
}

static bool SetUtc(DateTimeOffset target)
{
    var utc = target.UtcDateTime;
    var value = new SystemTime {
        Year=(ushort)utc.Year,Month=(ushort)utc.Month,DayOfWeek=(ushort)utc.DayOfWeek,
        Day=(ushort)utc.Day,Hour=(ushort)utc.Hour,Minute=(ushort)utc.Minute,
        Second=(ushort)utc.Second,Milliseconds=(ushort)utc.Millisecond
    };
    return SetSystemTime(ref value);
}
[DllImport("kernel32.dll", SetLastError=true)]
[return: MarshalAs(UnmanagedType.Bool)]
static extern bool SetSystemTime(ref SystemTime time);
[StructLayout(LayoutKind.Sequential)]
struct SystemTime
{
    public ushort Year,Month,DayOfWeek,Day,Hour,Minute,Second,Milliseconds;
}

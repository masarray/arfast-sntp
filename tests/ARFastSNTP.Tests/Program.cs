using ARFastSNTP;

var failures = 0;
void Check(string label, bool condition)
{
    Console.WriteLine($"{(condition ? "PASS" : "FAIL")} {label}");
    if (!condition) failures++;
}

static (uint seconds, uint fraction) Encode(DateTimeOffset instant)
{
    var ticks = (instant - new DateTimeOffset(1900, 1, 1, 0, 0, 0, TimeSpan.Zero)).Ticks;
    return (unchecked((uint)(ticks / TimeSpan.TicksPerSecond)),
        (uint)(((ulong)(ticks % TimeSpan.TicksPerSecond) << 32) / TimeSpan.TicksPerSecond));
}
void RoundTrip(string label, DateTimeOffset target, DateTimeOffset anchor)
{
    var (seconds, fraction) = Encode(target);
    var decoded = NtpEra.Resolve(seconds, fraction, anchor);
    Check(label, (decoded - target).Duration() < TimeSpan.FromTicks(2));
}
RoundTrip("NTP 2026 with local clock shifted to 2023",
    new DateTimeOffset(2026, 10, 9, 0, 0, 0, TimeSpan.Zero),
    new DateTimeOffset(2023, 9, 17, 0, 0, 0, TimeSpan.Zero));
RoundTrip("2036 NTP era rollover",
    new DateTimeOffset(2036, 2, 8, 0, 0, 0, TimeSpan.Zero),
    new DateTimeOffset(2036, 2, 8, 0, 0, 0, TimeSpan.Zero));
RoundTrip("2036 NTP era before rollover",
    new DateTimeOffset(2036, 2, 7, 6, 28, 15, TimeSpan.Zero),
    new DateTimeOffset(2036, 2, 7, 6, 28, 15, TimeSpan.Zero));
RoundTrip("2040 timestamp",
    new DateTimeOffset(2040, 5, 1, 12, 0, 0, TimeSpan.Zero),
    new DateTimeOffset(2040, 5, 1, 12, 0, 0, TimeSpan.Zero));
RoundTrip("fractional milliseconds",
    new DateTimeOffset(2026, 10, 9, 12, 30, 0, TimeSpan.Zero).AddMilliseconds(123.456),
    new DateTimeOffset(2026, 10, 9, 12, 30, 0, TimeSpan.Zero));
Check("zero fraction at rollover", NtpEra.Resolve(0, 0,
    new DateTimeOffset(2036, 2, 8, 0, 0, 0, TimeSpan.Zero)).Year == 2036);
Check("clock step during request rejected", ClockDiscontinuity.Detected(
    DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddHours(1), TimeSpan.FromMilliseconds(15)));
Check("normal packet delay accepted", !ClockDiscontinuity.Detected(
    DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMilliseconds(21), TimeSpan.FromMilliseconds(20)));
using (var gate = new CoalescingGate())
{
    Check("first refresh enters", await gate.TryEnterAsync(CancellationToken.None));
    Check("duplicate refresh coalesced", !await gate.TryEnterAsync(CancellationToken.None));
    gate.Exit();
    Check("gate released", await gate.TryEnterAsync(CancellationToken.None));
    gate.Exit();
}
Check("status snapshot serializes", StatusSnapshot.Serialize(new StatusSnapshot(
    DateTimeOffset.UtcNow, "192.16.1.175", "Monitor", null, null, null, null))
    .Contains("192.16.1.175", StringComparison.Ordinal));
Console.WriteLine(failures == 0 ? "All era tests passed." : $"{failures} era tests failed.");
Environment.ExitCode = failures == 0 ? 0 : 1;

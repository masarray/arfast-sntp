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
Check("canonical service name matches Windows SCM key", ServiceIdentity.Name == "ARFastSNTP");
Check("status snapshot serializes", StatusSnapshot.Serialize(new StatusSnapshot(
    DateTimeOffset.UtcNow, "192.16.1.175", "Monitor", null, null, null, null))
    .Contains("192.16.1.175", StringComparison.Ordinal));

var referenceTime = new DateTimeOffset(2026, 10, 9, 8, 0, 0, TimeSpan.Zero);
var requestBytes = new byte[48];
requestBytes[0] = 0x23;
NtpProtocol.WriteTimestamp(requestBytes.AsSpan(40, 8), referenceTime);
var replyBytes = new byte[48];
replyBytes[0] = 0x24;
replyBytes[1] = 1;
requestBytes.AsSpan(40, 8).CopyTo(replyBytes.AsSpan(24, 8));
NtpProtocol.WriteTimestamp(replyBytes.AsSpan(32, 8), referenceTime.AddMilliseconds(15));
NtpProtocol.WriteTimestamp(replyBytes.AsSpan(40, 8), referenceTime.AddMilliseconds(16));
var measured = NtpProtocol.Parse(requestBytes, replyBytes, referenceTime,
    referenceTime.AddMilliseconds(30), 2026);
Check("valid synthetic NTP response", measured.RttMs >= 28 && measured.RttMs <= 30);
bool wrongOriginateRejected = false;
replyBytes[24] ^= 1;
try { NtpProtocol.Parse(requestBytes, replyBytes, referenceTime, referenceTime.AddMilliseconds(30), 2026); }
catch (InvalidOperationException) { wrongOriginateRejected = true; }
Check("originate mismatch rejected", wrongOriginateRejected);
replyBytes[24] ^= 1;
replyBytes[0] = 0xE4;
bool unsyncedRejected = false;
try { NtpProtocol.Parse(requestBytes, replyBytes, referenceTime, referenceTime.AddMilliseconds(30), 2026); }
catch (InvalidOperationException) { unsyncedRejected = true; }
Check("LI=3 rejected", unsyncedRejected);
Check("step without certification rejected", RejectStepWithoutCertification());
static bool RejectStepWithoutCertification()
{
    try { SyncOptions.Parse(new[] { "--server", "192.16.1.175", "--allow-step" }); return false; }
    catch (ArgumentException) { return true; }
}

Check("initial sample is burst verified", PollPolicy.RequiresBurst(false, measured));
Check("stable sample uses single exchange", !PollPolicy.RequiresBurst(true, measured));
Check("large drift triggers burst", PollPolicy.RequiresBurst(true,
    new SntpSample(TimeSpan.FromDays(365), 12, 1)));
Check("stable interval bounded to 3s",
    PollPolicy.NextInterval(true, true) == TimeSpan.FromSeconds(3));
Check("monitor large offset backs off to 5s",
    PollPolicy.NextInterval(false, false) == TimeSpan.FromSeconds(5));
Check("correction cycle fast 1s",
    PollPolicy.NextInterval(false, true) == TimeSpan.FromSeconds(1));
Check("valid coherent burst median", PollPolicy.MedianCoherent(
    new SntpSample(TimeSpan.FromMilliseconds(8), 15, 1),
    new SntpSample(TimeSpan.FromMilliseconds(7), 12, 1),
    new SntpSample(TimeSpan.FromMilliseconds(9), 14, 1)).Offset ==
        TimeSpan.FromMilliseconds(8));
bool spreadRejected = false;
try
{
    PollPolicy.MedianCoherent(
        new SntpSample(TimeSpan.FromMilliseconds(0), 12, 1),
        new SntpSample(TimeSpan.FromMilliseconds(1), 12, 1),
        new SntpSample(TimeSpan.FromMilliseconds(800), 12, 1));
}
catch (InvalidOperationException) { spreadRejected = true; }
Check("disagreeing burst rejected", spreadRejected);
replyBytes[0] = 0x24;
replyBytes[1] = 0; // Server KoD RATE must never be treated as valid time.
"RATE"u8.CopyTo(replyBytes.AsSpan(12, 4));
bool rateKoD = false;
try { NtpProtocol.Parse(requestBytes, replyBytes, referenceTime,
    referenceTime.AddMilliseconds(30), 2026); }
catch (NtpKissOfDeathException ex) { rateKoD = ex.Code == "RATE"; }
Check("NTP RATE Kiss-o'-Death recognized", rateKoD);
replyBytes[0] = 0x24;
replyBytes[1] = 1;
// Server 2026, local Windows manually shifted to 2023: offset is large but computable.
var localT1 = new DateTimeOffset(2023, 10, 9, 8, 0, 0, TimeSpan.Zero);
NtpProtocol.WriteTimestamp(requestBytes.AsSpan(40, 8), localT1);
requestBytes.AsSpan(40, 8).CopyTo(replyBytes.AsSpan(24, 8));
NtpProtocol.WriteTimestamp(replyBytes.AsSpan(32, 8), referenceTime.AddMilliseconds(10));
NtpProtocol.WriteTimestamp(replyBytes.AsSpan(40, 8), referenceTime.AddMilliseconds(11));
var shiftedSample = NtpProtocol.Parse(requestBytes, replyBytes, localT1,
    localT1.AddMilliseconds(22), 2026);
Check("multi-year Windows clock offset survives NTP parsing",
    shiftedSample.Offset > TimeSpan.FromDays(1000));

var testUtc = new DateTimeOffset(2023, 7, 12, 15, 58, 29, TimeSpan.Zero);
string oldWorkerStatus = System.Text.Json.JsonSerializer.Serialize(new
{
    Server = "192.16.1.175",
    UpdatedUtc = testUtc,
    State = "Offset detected",
    OffsetMilliseconds = 81000000000.0,
    EstimatedServerUtc = testUtc.AddYears(3),
    RequestCount = 3L
    // Intentionally lacks StepEnabled, AcceptedCount, RejectedCount, LastEvent.
});
bool legacyParsed = ARFastSNTP.Dashboard.DashboardStatus.TryParse(
    oldWorkerStatus, out var legacy, out _);
Check("dashboard tolerates older status missing StepEnabled", legacyParsed && legacy is not null &&
    !legacy.StepEnabled && legacy.RequestCount == 3 && legacy.AcceptedCount == 0);
Check("legacy dashboard status is not mislabeled synchronized", legacy is not null &&
    !legacy.IsSynchronized(testUtc.AddSeconds(2)) &&
    legacy.HasValidReply(testUtc.AddSeconds(2)));
Check("malformed JSON rejected without exception",
    !ARFastSNTP.Dashboard.DashboardStatus.TryParse("{ invalid",
        out _, out _));
Check("empty status rejected without exception",
    !ARFastSNTP.Dashboard.DashboardStatus.TryParse("{}", out _, out _));
Check("stale status not shown live after clock jump",
    legacy is not null && !legacy.HasValidReply(testUtc.AddHours(1)));
var normalizedStatus = StatusSnapshot.Serialize(new StatusSnapshot(
    testUtc, "192.16.1.175", "Synchronized", 12.5, 3.2,
    testUtc.AddMilliseconds(12.5), null)
{ StepEnabled = false, RequestCount = 8, AcceptedCount = 7, RejectedCount = 1 });
using var statusDocument = System.Text.Json.JsonDocument.Parse(normalizedStatus);
var statusFields = statusDocument.RootElement;
Check("worker JSON includes counters and StepEnabled",
    statusFields.TryGetProperty("AcceptedCount", out var acceptedField) &&
    acceptedField.GetInt64() == 7 &&
    statusFields.TryGetProperty("RequestCount", out var requestsField) &&
    requestsField.GetInt64() == 8 &&
    statusFields.TryGetProperty("StepEnabled", out var allowField) &&
    allowField.ValueKind == System.Text.Json.JsonValueKind.False);
bool currentParsed = ARFastSNTP.Dashboard.DashboardStatus.TryParse(
    normalizedStatus, out var current, out _);
Check("current worker status parsed and lamps valid", currentParsed && current is not null &&
    current.HasValidReply(testUtc.AddSeconds(2)) &&
    current.IsSynchronized(testUtc.AddSeconds(2)) && current.AcceptedCount == 7);
// Pure Studio rendering rules: enabling a Windows Service never proves sync.
var unreachable = new ARFastSNTP.Dashboard.DashboardStatus
{
    Server = "192.16.1.175",
    UpdatedUtc = testUtc,
    State = "Offline / rejected",
    LastEvent = "The operation timed out.",
    RequestCount = 12,
    RejectedCount = 12
};
var noReply = ARFastSNTP.Studio.StudioStatusPresenter.Create(unreachable);
Check("Studio reports no verified sync for failed NTP request",
    noReply.PcSync == "Not synced" && !noReply.ValidReply && !noReply.Synchronized);
Check("Studio shows actionable timeout rather than repeated No reply",
    noReply.ServerHealth == "No reply" &&
    noReply.Footer.Contains("UDP 123", StringComparison.Ordinal) &&
    !noReply.Footer.Contains("valid replies", StringComparison.Ordinal));
// Counters and updated timestamps change every second; the steady-state
// main-view model must stay equal when these diagnostics change.
var stillNoReply = unreachable with
{
    RequestCount = 500, RejectedCount = 500,
    UpdatedUtc = testUtc.AddMinutes(5)
};
Check("Studio status stable despite continuous failed request counters",
    ARFastSNTP.Studio.StudioStatusPresenter.Create(stillNoReply) == noReply);
var aligned = unreachable with
{
    State = "Synchronized", LastEvent = null, OffsetMilliseconds = 0.4,
    EstimatedServerUtc = testUtc.AddMilliseconds(0.4), Stratum = 1,
    AcceptedCount = 11, RejectedCount = 1
};
var good = ARFastSNTP.Studio.StudioStatusPresenter.Create(aligned);
Check("Studio sync label requires valid time from peer",
    good.Synchronized && good.PcSync == "Synced" &&
    good.ServerHealth == "Online" && good.Stratum == "1");
var newerAligned = aligned with
{
    RequestCount = 12345, AcceptedCount = 12000,
    UpdatedUtc = testUtc.AddSeconds(20), RoundTripMilliseconds = 8.7
};
Check("Studio keeps steady valid status when counters and RTT vary",
    ARFastSNTP.Studio.StudioStatusPresenter.Create(newerAligned) == good);
var spoofed = aligned with { OffsetMilliseconds = null, EstimatedServerUtc = null };
Check("Studio never equates synchronized text with a missing timestamp",
    !ARFastSNTP.Studio.StudioStatusPresenter.Create(spoofed).Synchronized);
var drifting = aligned with { State = "Offset detected", OffsetMilliseconds = 110_000.0 };
var driftView = ARFastSNTP.Studio.StudioStatusPresenter.Create(drifting);
Check("Studio formats large offset readably",
    !driftView.Synchronized && driftView.ServerOffset.Contains("min",
        StringComparison.Ordinal));
Console.WriteLine(failures == 0 ? "All era tests passed." : $"{failures} era tests failed.");
Environment.ExitCode = failures == 0 ? 0 : 1;

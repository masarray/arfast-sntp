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
// ON/OFF is a source-use flag, NOT an SCM install/stop/restart command.
// The privileged worker owns the toggle and persists it under HKLM.
Check("control protocol allows only fixed ON/OFF/QUERY commands",
    SourceControlProtocol.IsCommand(SourceControlProtocol.Disable) &&
    SourceControlProtocol.IsCommand(SourceControlProtocol.Enable) &&
    SourceControlProtocol.IsCommand(SourceControlProtocol.Query) &&
    !SourceControlProtocol.IsCommand(3));
Check("invalid control message version and length rejected",
    !SourceControlProtocol.TryDecode(new byte[] { 2, 1 }, out _) &&
    !SourceControlProtocol.TryDecode(new byte[] { 1, 1, 0 }, out _) &&
    !SourceControlProtocol.TryDecode(new byte[] { 1, 100 }, out _));
Check("valid control message decoded",
    SourceControlProtocol.TryDecode(new byte[] { 1, SourceControlProtocol.Disable },
        out var decodedControl) && decodedControl == SourceControlProtocol.Disable);
Check("control ACK contains verified enabled state",
    SourceControlProtocol.TryDecodeReply(
        SourceControlProtocol.Reply(enabled: false), out var sourceOff) && !sourceOff &&
    !SourceControlProtocol.TryDecodeReply(
        SourceControlProtocol.Reply(enabled: true, ok: false), out _));
int stateWrites = 0;
bool persistedState = true;
using (var controller = new SourceControlState(true, enabled =>
{
    persistedState = enabled;
    stateWrites++;
}))
{
    Check("worker source starts enabled", controller.Enabled);
    Check("OFF persists state without service shutdown",
        !controller.SetEnabled(false) && !controller.Enabled &&
        !persistedState && stateWrites == 1);
    Check("OFF prevents any Windows time correction",
        !controller.TrySetTime(testUtc, out _));
    Check("source state wake-up is single/coalesced",
        await controller.WaitAsync(TimeSpan.Zero, CancellationToken.None) &&
        !await controller.WaitAsync(TimeSpan.Zero, CancellationToken.None));
    Check("repeated OFF does not write registry or wake worker",
        !controller.SetEnabled(false) && stateWrites == 1 &&
        !await controller.WaitAsync(TimeSpan.Zero, CancellationToken.None));
    Check("ON resumes worker without SCM action",
        controller.SetEnabled(true) && controller.Enabled &&
        persistedState && stateWrites == 2 &&
        await controller.WaitAsync(TimeSpan.Zero, CancellationToken.None));
}
var pausedSnapshot = unreachable with
{
    State = "Disabled", SourceEnabled = false, LastEvent = "NTP source paused."
};
var paused = ARFastSNTP.Studio.StudioStatusPresenter.Create(pausedSnapshot);
Check("paused source is OFF, not falsely shown as No reply",
    paused.PcSync == "NTP off" && paused.ServerHealth == "Off" &&
    !paused.ValidReply && paused.Footer.Length == 0);
Check("status SourceEnabled preserves backwards compatible default",
    legacy is not null && legacy.SourceEnabled &&
    StatusSnapshot.Serialize(new StatusSnapshot(testUtc,
        "192.16.1.175", "Disabled", null, null, null, null)
    { SourceEnabled = false }).Contains("\"SourceEnabled\":false", StringComparison.Ordinal));

// Deterministic NTP source selection and persisted live configuration.
var c1 = new PeerElection.Candidate("192.16.1.175", 2, 8, 1);
var c2 = new PeerElection.Candidate("192.16.1.176", 4, 12, 2);
var bad = new PeerElection.Candidate("192.16.1.177", 8000, 1, 1);
Check("one trusted server needs repeated verification",
    PeerElection.Choose(new[] {c1}, null, false).Selected is null &&
    PeerElection.Choose(new[] {c1}, null, true).Selected == c1.Address);
Check("two agreeing NTP servers elect lowest RTT",
    PeerElection.Choose(new[] {c1,c2}, null, false).Selected == c1.Address);
Check("previous valid NTP source retained (hysteresis)",
    PeerElection.Choose(new[] {c1,c2}, c2.Address, false).Selected == c2.Address);
Check("two disagreeing sources never step",
    PeerElection.Choose(new[] {c1,bad}, null, true).Selected is null);
var majority = PeerElection.Choose(new[] {c1,c2,bad}, null, false);
Check("three sources exclude outlier even if lower RTT",
    majority.Selected == c1.Address && majority.Agreeing == 2);
var chainA = new PeerElection.Candidate("192.16.1.180", 0, 5, 1);
var chainB = new PeerElection.Candidate("192.16.1.181", 200, 5, 1);
var chainC = new PeerElection.Candidate("192.16.1.182", 400, 5, 1);
Check("consensus is a bounded interval, not a chained 400ms span",
    PeerElection.Choose(new[] { chainA, chainB, chainC }, null, false).Agreeing == 2);
Check("no valid sources means no clock selection",
    PeerElection.Choose(new[] {bad with { RttMilliseconds = 999 }}, null, true).Selected is null);
Check("NTP addresses use canonical unicast IPv4 only",
    PeerCatalog.TryNormalize("192.016.1.175", out _) == false &&
    PeerCatalog.TryNormalize("192.16.1.175", out var cleanIp) &&
    cleanIp == "192.16.1.175" &&
    !PeerCatalog.TryNormalize("224.0.0.1", out _) &&
    PeerCatalog.TryNormalize("127.0.0.1", out _));

string peerPath = Path.Combine(Path.GetTempPath(),
    "arfastsntp-tests-" + Guid.NewGuid().ToString("N"), "peers.json");
try
{
    int writes = 0;
    using (var master = new SourceControlState(true, _ => writes++))
    using (var peers = PeerCatalog.Load(peerPath,
        System.Net.IPAddress.Parse("192.16.1.175"), master))
    {
        Check("legacy one-IP configuration migrated without installer",
            peers.Snapshot().Peers is [{ Address: "192.16.1.175", Enabled: true }]);
        var added = peers.Mutate("add", "192.16.1.176");
        Check("adding server modifies live list without SCM restart",
            added.Length == 2 && added[1].Enabled);
        bool duplicateRefused = false;
        try { peers.Mutate("add", "192.16.1.176"); }
        catch (InvalidOperationException) { duplicateRefused = true; }
        Check("duplicate IP refused", duplicateRefused);
        var beforeDisable = peers.Snapshot().Generation;
        peers.Mutate("disable", "192.16.1.175");
        Check("disabled source immediately invalidates prior election",
            !peers.IsCurrent("192.16.1.175", beforeDisable));
        peers.Mutate("disable", "192.16.1.176");
        Check("all OFF prevents any Windows clock step",
            !master.Enabled &&
            !peers.TryStep("192.16.1.175", peers.Snapshot().Generation, testUtc, out _));
        peers.Mutate("enable", "192.16.1.176");
        Check("one server ON wakes canonical worker", master.Enabled &&
            peers.Snapshot().Peers.Count(p => p.Enabled) == 1);
        peers.Mutate("delete", "192.16.1.175");
        Check("deleting a server removes it without service uninstall",
            peers.Snapshot().Peers is [{ Address: "192.16.1.176", Enabled: true }]);
        Check("peer catalog signals changes with bounded coalescing",
            await peers.WaitAsync(TimeSpan.Zero, CancellationToken.None) &&
            !await peers.WaitAsync(TimeSpan.Zero, CancellationToken.None));
    }
    using var master2 = new SourceControlState(true, _ => { });
    using var restored = PeerCatalog.Load(peerPath,
        System.Net.IPAddress.Parse("192.16.1.175"), master2);
    Check("NTP server list survives worker restart",
        restored.Snapshot().Peers is [{ Address: "192.16.1.176", Enabled: true }]);
}
finally
{
    Directory.Delete(Path.GetDirectoryName(peerPath)!, recursive: true);
}
Check("peer control accepts only constrained operations",
    PeerControlProtocol.IsAction("add") &&
    PeerControlProtocol.IsAction("delete") &&
    PeerControlProtocol.IsAction("enable") &&
    !PeerControlProtocol.IsAction("restart-service") &&
    !PeerControlProtocol.IsAction("set-clock"));
using (var stream = new MemoryStream(PeerControlProtocol.Encode(
    new PeerControlProtocol.Request("add", "192.16.1.176"))))
{
    var request = await PeerControlProtocol.ReadAsync<PeerControlProtocol.Request>(
        stream, CancellationToken.None);
    Check("bounded local IPC round-trip",
        request.Action == "add" && request.Address == "192.16.1.176");
}
var snapshotMulti = new StatusSnapshot(testUtc, "192.16.1.175",
    "Synchronized", 2.0, 8, testUtc.AddMilliseconds(2), null)
{
    SelectedServer = "192.16.1.175",
    Peers = new[] { new PeerReading("192.16.1.175", true, "Online",
        2.0, 8, 1, testUtc, null, true, 4) }
};
Check("per-peer multi status parses in Studio and legacy dashboard",
    ARFastSNTP.Dashboard.DashboardStatus.TryParse(
        StatusSnapshot.Serialize(snapshotMulti), out var ui, out _) &&
    ui is not null && ui.Peers.Length == 1 &&
    ui.Peers[0].Selected && ui.SelectedServer == "192.16.1.175");

// ARSAS advertises SNTPv4 Mode 4, stratum 2, LOCL reference ID.
// Once the reply reaches a Windows UDP socket these values alone must
// not be treated as invalid or incompatible by the client parser.
var arsasCompatible = (byte[])replyBytes.Clone();
arsasCompatible[0] = 0x24;
arsasCompatible[1] = 2;
arsasCompatible[12] = (byte)'L';
arsasCompatible[13] = (byte)'O';
arsasCompatible[14] = (byte)'C';
arsasCompatible[15] = (byte)'L';
var arsasSample = NtpProtocol.Parse(requestBytes, arsasCompatible,
    referenceTime, referenceTime.AddMilliseconds(30), 2026);
Check("ARSAS Mode 4 stratum 2 LOCL packet is protocol-compatible",
    arsasSample.Stratum == 2);
var invalidLeap = (byte[])arsasCompatible.Clone();
invalidLeap[0] = (byte)(invalidLeap[0] | 0xC0); // LI=3, unsynchronized
bool invalidClockRejected = false;
try { _ = NtpProtocol.Parse(requestBytes, invalidLeap,
    referenceTime, referenceTime.AddMilliseconds(30), 2026); }
catch (InvalidOperationException) { invalidClockRejected = true; }
Check("ARSAS unsynchronized LI=3 packet must not set Windows clock",
    invalidClockRejected);


 
// Real UDP loopback transport fixture. No privileged port, external network,
// Windows clock mutation, installer or service is involved. The response
// follows the exact ARSAS Mode 4/stratum 2/LOCL transaction shape.
static async Task<(SntpSample? Sample, Exception? Error)> ProbeLoopbackAsync(
    bool badOriginate, bool withReply)
{
    using var server = new System.Net.Sockets.UdpClient(
        new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
    int port = ((System.Net.IPEndPoint)server.Client.LocalEndPoint!).Port;
    Task responder = Task.Run(async () =>
    {
        var received = await server.ReceiveAsync();
        if (!withReply) return;
        var bytes = new byte[48];
        bytes[0] = 0x24; // v4 server mode 4, synchronized LI=0
        bytes[1] = 2; // ARSAS commissioning stratum
        "LOCL"u8.CopyTo(bytes.AsSpan(12, 4));
        received.Buffer.AsSpan(40, 8).CopyTo(bytes.AsSpan(24, 8));
        if (badOriginate) bytes[24] ^= 1;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        NtpProtocol.WriteTimestamp(bytes.AsSpan(32, 8), now);
        NtpProtocol.WriteTimestamp(bytes.AsSpan(40, 8), now);
        await server.SendAsync(bytes, received.RemoteEndPoint);
    });
    SntpSample? sample = null;
    Exception? error = null;
    try
    {
        sample = await NtpProtocol.QueryAsync(
            System.Net.IPAddress.Loopback, DateTimeOffset.UtcNow.Year,
            CancellationToken.None, port);
    }
    catch (Exception ex) { error = ex; }
    await responder;
    return (sample, error);
}
var receivedFixture = await ProbeLoopbackAsync(badOriginate: false, withReply: true);
Check("live UDP loopback Mode 4 from correct socket validated",
    receivedFixture.Sample is { Stratum: 2 } && receivedFixture.Error is null);
var rejectedFixture = await ProbeLoopbackAsync(badOriginate: true, withReply: true);
Check("UDP socket receive with invalid Originate is NOT No reply",
    rejectedFixture.Error is NtpInvalidResponseException &&
    rejectedFixture.Error.Message.Contains("originate", StringComparison.OrdinalIgnoreCase) &&
    rejectedFixture.Sample is null);
var silentFixture = await ProbeLoopbackAsync(badOriginate: false, withReply: false);
Check("genuine UDP socket silence is a typed receive timeout with Tx correlation",
    silentFixture.Error is NtpResponseTimeoutException noRx &&
    noRx.LocalEndpoint.StartsWith("127.0.0.1:", StringComparison.Ordinal) &&
    noRx.RemoteEndpoint.StartsWith("127.0.0.1:", StringComparison.Ordinal) &&
    noRx.RequestTransmitHex.Length == 16 &&
    noRx.IsSelfTarget &&
    NtpFailureClassifier.State(noRx) == "Local target");

var remoteTimeout = new NtpResponseTimeoutException(
    new System.Net.IPEndPoint(System.Net.IPAddress.Parse("192.16.1.175"), 54000),
    new System.Net.IPEndPoint(System.Net.IPAddress.Parse("192.16.1.33"), 123),
    new byte[8], new TimeoutException());
Check("remote UDP timeout remains No reply and is not treated as self-target",
    !remoteTimeout.IsSelfTarget &&
    NtpFailureClassifier.State(remoteTimeout) == "No reply");

var capturedSelfTarget = new NtpResponseTimeoutException(
    new System.Net.IPEndPoint(System.Net.IPAddress.Parse("192.16.1.33"), 58642),
    new System.Net.IPEndPoint(System.Net.IPAddress.Parse("192.16.1.33"), 123),
    Convert.FromHexString("EE7429C890E71624"), new TimeoutException());
Check("pcap .33 to .33 request is diagnosed as local target",
    capturedSelfTarget.IsSelfTarget &&
    NtpFailureClassifier.State(capturedSelfTarget) == "Local target" &&
    capturedSelfTarget.Message.Contains("Npcap RAW", StringComparison.Ordinal) &&
    capturedSelfTarget.RequestTransmitHex == "EE7429C890E71624");

Check("received-but-invalid NTP reply is separately classified",
    rejectedFixture.Error is not null &&
    NtpFailureClassifier.State(rejectedFixture.Error) == "Invalid reply");

// An initial single verified sample is not a server conflict.
var notYetTrusted = PeerElection.Choose(new[] { c1 }, null, false);
Check("first responding source is verifying, not conflicting",
    PeerStatusClassifier.Pending(1, notYetTrusted, Array.Empty<string>()) == "Verifying");
Check("two disagreeing sources show conflict",
    PeerStatusClassifier.Pending(2, PeerElection.Choose(new[] { c1, bad }, null, false),
        Array.Empty<string>()) == "Source conflict");
Check("no validated samples with rate limited peer is distinguishable",
    PeerStatusClassifier.Pending(0,
        PeerElection.Choose(Array.Empty<PeerElection.Candidate>(), null, false),
        new[] { "Rate limited" }) == "Rate limited");
var diag = new NtpDecisionTrace(DateTimeOffset.UtcNow, "Verifying", null,
    "Single responding source; consensus unavailable", null,
    new[] { new NtpDecisionPeer(c1.Address, true, "Online", 2, 8, 3, 100, 1, 0, null) },
    4, 3, 1);
using var diagJson = System.Text.Json.JsonDocument.Parse(StatusSnapshot.Serialize(
    snapshotMulti with { RecentDecisions = new[] { diag } }));
Check("worker snapshot contains structured bounded decision evidence",
    diagJson.RootElement.GetProperty("RecentDecisions")[0]
        .GetProperty("Peers")[0].GetProperty("Address").GetString() == c1.Address);

Console.WriteLine(failures == 0 ? "All regression tests passed." : $"{failures} regression tests failed.");
Environment.ExitCode = failures == 0 ? 0 : 1;

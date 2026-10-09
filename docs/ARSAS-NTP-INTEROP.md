# AR Fast SNTP ↔ ARSAS SNTP interop diagnosis

## Observed evidence (user screenshots, 2026-10-10)

- AR Fast SNTP displays **No reply** and a UDP/123 timeout.
- ARSAS displays **SNTP Server Active: 192.16.1.33**.
- ARSAS logs **UDP/123 unavailable (AccessDenied)** and switched to
  **Npcap RAW SNTP** (on the KM_Test/station-bus interface).
- ARSAS logs a client request from `192.16.1.175` and a Mode 4 RAW reply
  sent to that address, but that alone does not establish delivery to the
  Windows UDP socket used by AR Fast SNTP.
- `192.16.1.255` in ARSAS is its directed Mode 5 broadcast target on a /24,
  **not** the unicast NTP server address.

## Grounded implementation check

ARSAS `Services/SntpRawNpcapTransport.cs` sends packet bytes through
`NpcapProcessBusDuplexTransport.SendAsync`, which is link-layer injection,
not a Windows UDP socket. `Services/SntpEthernetFrameCodec.cs` builds a Mode 4
response with UDP source port 123 and destination equal to the **captured
request's ephemeral UDP source port**. AR Fast SNTP `NtpProtocol.QueryAsync`
connects a standard UDP socket to `SERVER_IP:123` and waits for the socket
to receive a reply. It validates the server IP, server UDP port, NTP mode,
stratum and originate timestamp after receipt.

A RAW transmit success in Npcap **does not prove** that an RX datagram was
injected into the *same Windows host's* UDP/IP receive path, particularly
when ARSAS and AR Fast SNTP are running on the same machine. This is a likely
test-topology/transport issue, not evidence for relaxing SNTP validation.

## Reproduce without changing security or Windows Time

1. Record the IP shown in the ARSAS SNTP Server banner (e.g. `192.16.1.33`).
   It must be configured as a **unicast server** in AR Fast SNTP.
   Do not put the Mode 5 directed-broadcast IP (e.g. `192.16.1.255`) into
   the unicast peer list.
2. Hover the AR Fast SNTP row **Status** when it says No reply. Its detailed
   diagnostic now identifies the Windows local IP:ephemeral UDP port that
   sent the request and the expected server IP:123.
3. In ARSAS Diagnostics confirm **UdpSocket** vs **NpcapRaw**, and compare
   both applications' timestamps. A request from a third relay/IED is not
   necessarily the AR Fast SNTP request.
4. Wireshark/Npcap capture on the actual NIC, filter
   `udp.port == 123 && ip.addr == 192.16.1.33`, then match the exact
   request's source IP/port and NTP transmit timestamp with the reply's
   destination IP/port and originate timestamp.
5. Determine whether the reply reaches the **receive** side of the Windows
   IP stack, not merely the capture/injection send path. Inspect source IP
   `192.16.1.33`, UDP source 123, expected local destination ephemeral
   port, nonzero valid IP/UDP checksums, and NTP LI/stratum. Do not assume
   that a RAW-injected Tx frame is delivered to local sockets on same host.
6. Most decisive A/B test: run ARSAS server on a **separate machine on the
   same isolated station-bus segment**; run AR Fast SNTP client on the
   Windows certification PC. Alternatively use a genuine UDP-socket NTP
   server bound to the correct NIC when port 123 is available.
7. If packets reach the client's UDP socket but still fail, Status/History
   will show parser validation problems (LI=3, NTP originate mismatch,
   unsupported version/stratum, invalid time), rather than a receive timeout.

**Never disable antivirus/firewall broadly, change NTP trust boundaries,
or forcibly stop Windows Time just to suppress No reply.** Identify whether
the failure is on the return path, local injection, socket delivery, or NTP
validation and address that layer explicitly.

## UI/worker ownership

- This milestone does **not** bypass `UdpClient` with a promiscuous RAW
  receiver, relax endpoint/originate checks, or infer broadcast from Mode 4.
- A new bounded, typed `NtpResponseTimeoutException` records the selected
  local IP:port and expected peer IP:123 for row tooltip. Status History
  remains compact and logs only peer transitions.
- ARSAS RAW backend has its own acceptance criteria and should be changed
  in `masarray/arsas` only after packet direction and topology evidence.

# SNTP raw-transport interoperability

This engineering note explains a common source of confusion when a time-server test harness sends a raw Ethernet/IPv4/UDP packet instead of writing an ordinary UDP datagram through the operating system.

## What a client actually checks

A sent or captured Ethernet frame is not proof that a standard UDP socket received its payload. AR Fast SNTP sends a unicast NTP Mode 3 request to the configured server IP on UDP port 123, then validates the corresponding Mode 4 reply. The reply must reach the client UDP socket with matching endpoint, NTP originate timestamp, leap status, stratum and version.

## Check the route and response

1. Configure the actual unicast NTP server IPv4 address, not a subnet broadcast address.
2. Inspect the AR Fast SNTP status and the local/remote IP:port diagnostics.
3. Capture UDP port 123 with a packet analyzer on the relevant NIC; correlate the client's transmit timestamp and source port with the response's originate timestamp and destination port.
4. Confirm delivery through the receive path of the operating system, not only success of link-layer injection.
5. Prefer two separate test computers or a regular UDP-based server to distinguish local raw packet injection from a real server response.
6. Treat parser rejections separately from no UDP reply. Do not weaken NTP packet validation to work around a test-network problem.

This note describes test topology and SNTP socket behavior. It does not require any specific vendor or test equipment.

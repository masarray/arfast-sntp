namespace ARFastSNTP;

/// <summary>Persisted, service-owned NTP configuration entry.</summary>
internal sealed record NtpPeer(string Address, bool Enabled);

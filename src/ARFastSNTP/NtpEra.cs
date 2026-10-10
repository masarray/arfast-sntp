namespace ARFastSNTP;

/// <summary>Resolves 32-bit NTP seconds without trusting the possibly incorrect Windows clock.</summary>
internal static class NtpEra
{
    private static readonly DateTimeOffset Epoch = new(1900, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private const long EraSeconds = 1L << 32;

    internal static DateTimeOffset Resolve(uint seconds, uint fraction, DateTimeOffset trustedReference)
    {
        var approximate = (long)(trustedReference - Epoch).TotalSeconds;
        var era = (long)Math.Round((approximate - (long)seconds) / (double)EraSeconds);
        return Epoch.AddSeconds(checked(era * EraSeconds + seconds))
            .AddTicks((long)(((ulong)fraction * TimeSpan.TicksPerSecond) >> 32));
    }

    internal static DateTimeOffset ResolveGpsEra(uint seconds, uint fraction) =>
        Resolve(seconds, fraction, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
}

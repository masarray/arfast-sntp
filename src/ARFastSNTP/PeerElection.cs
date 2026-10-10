namespace ARFastSNTP;

/// <summary>
/// Small, deterministic source election for an authorized trusted lab.
/// No clock step is allowed when currently reachable sources disagree.
/// RFC 5905/chrony inspire the majority, consistency and stickiness policy;
/// this is NOT a complete NTPv4 clock-filter/discipline implementation.
/// </summary>
internal static class PeerElection
{
    internal readonly record struct Candidate(
        string Address, double OffsetMilliseconds, double RttMilliseconds, byte Stratum);

    internal readonly record struct Decision(
        string? Selected, string Reason, bool Degraded, int Agreeing);

    internal const double AgreementMilliseconds = 250;

    internal static Decision Choose(IReadOnlyList<Candidate> candidates,
        string? previous, bool singleSourceConfirmed)
    {
        var clean = candidates.Where(c =>
            double.IsFinite(c.OffsetMilliseconds) &&
            double.IsFinite(c.RttMilliseconds) &&
            c.RttMilliseconds is >= 0 and <= 250 &&
            c.Stratum is >= 1 and <= 15).ToArray();
        if (clean.Length == 0)
            return new Decision(null, "No validated NTP sources", true, 0);
        if (clean.Length == 1)
        {
            // One trusted GPS on a deliberately isolated test PC is supported.
            // When multiple sources are configured, the worker must confirm
            // the sole responder on consecutive rounds before correcting.
            return new Decision(singleSourceConfirmed ? clean[0].Address : null,
                "Single responding source; consensus unavailable", true, 1);
        }

        // A clique must have an overall spread <= threshold; testing
        // distances only to a central seed could accidentally admit endpoints
        // separated by twice the threshold.
        var sorted = clean.OrderBy(c => c.OffsetMilliseconds).ToArray();
        var groups = (from lo in Enumerable.Range(0, sorted.Length)
                      from hi in Enumerable.Range(lo, sorted.Length - lo)
                      where sorted[hi].OffsetMilliseconds -
                            sorted[lo].OffsetMilliseconds <= AgreementMilliseconds
                      select sorted.Skip(lo).Take(hi - lo + 1).ToArray()).ToArray();
        var best = groups.OrderByDescending(g => g.Length)
            .ThenBy(g => g.Average(c => c.RttMilliseconds)).First();

        if (best.Length <= clean.Length / 2)
            return new Decision(null, "Time sources disagree; correction held", false, best.Length);

        // Avoid unnecessary source switching. Reuse the previous reference if
        // it still belongs to the agreeing clique; RTT is only a tie-breaker.
        var retained = best.FirstOrDefault(c => c.Address == previous);
        var elected = retained.Address is not null ? retained :
            best.OrderBy(c => c.RttMilliseconds)
                .ThenBy(c => c.Stratum).First();
        return new Decision(elected.Address,
            best.Length < clean.Length ? "Outlier excluded" : "Sources agree",
            false, best.Length);
    }
}

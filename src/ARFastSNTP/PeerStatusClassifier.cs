namespace ARFastSNTP;

/// <summary>
/// Distinguish insufficient evidence from genuine peer disagreement.
/// This is a pure presentation decision and never authorizes a clock step.
/// </summary>
internal static class PeerStatusClassifier
{
    internal static string Pending(int eligibleCount, PeerElection.Decision decision,
        IReadOnlyCollection<string> failedStates)
    {
        if (decision.Selected is not null) return "Verifying";
        if (eligibleCount == 1) return "Verifying"; // one unconfirmed source != conflict
        if (eligibleCount > 1)
            return decision.Reason == "Time sources disagree; correction held"
                ? "Source conflict" : "Verifying";
        if (failedStates.Contains("Local target")) return "Local target";
        if (failedStates.Contains("Invalid reply")) return "Invalid reply";
        if (failedStates.Contains("Denied")) return "Denied";
        if (failedStates.Contains("Rate limited")) return "Rate limited";
        if (failedStates.Contains("Network error")) return "Network error";
        if (failedStates.Contains("Invalid sample")) return "Invalid sample";
        return "No reply";
    }
}

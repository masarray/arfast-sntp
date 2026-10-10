using System.Globalization;

namespace ARFastSNTP.Studio;

/// <summary>
/// Preserve original worker events in their tooltip but display compact,
/// local operator time without ISO-8601 fractional-second noise.
/// </summary>
internal static class EventDisplay
{
    internal static (string Time, string Message) Format(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return ("", "");
        int separator = raw.IndexOf(' ');
        if (separator <= 0) return ("", raw);
        string stamp = raw[..separator];
        if (!DateTimeOffset.TryParseExact(stamp, "O", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var utc))
            return ("", raw);
        return (utc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            raw[(separator + 1)..]);
    }
}

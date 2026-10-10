namespace ARFastSNTP;

internal sealed class NtpKissOfDeathException : InvalidOperationException
{
    internal string Code { get; }
    internal NtpKissOfDeathException(string code)
        : base($"NTP server Kiss-o'-Death: {code}")
    {
        Code = code;
    }
}

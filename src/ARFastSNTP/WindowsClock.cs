using System.Runtime.InteropServices;

namespace ARFastSNTP;

internal static class WindowsClock
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SystemTime
    {
        public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetSystemTime(ref SystemTime systemTime);

    internal static (bool Success, int Error) SetUtc(DateTimeOffset target)
    {
        if (!OperatingSystem.IsWindows()) return (false, -1);
        DateTime utc = target.UtcDateTime;
        var value = new SystemTime
        {
            Year = (ushort)utc.Year, Month = (ushort)utc.Month,
            DayOfWeek = (ushort)utc.DayOfWeek, Day = (ushort)utc.Day,
            Hour = (ushort)utc.Hour, Minute = (ushort)utc.Minute,
            Second = (ushort)utc.Second, Milliseconds = (ushort)utc.Millisecond
        };
        bool success = SetSystemTime(ref value);
        return (success, success ? 0 : Marshal.GetLastWin32Error());
    }
}

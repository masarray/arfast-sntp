using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Media;
using ARFastSNTP.Dashboard;

namespace ARFastSNTP.Studio;

/// <summary>A small, observable row; unchanged fields never trigger repaint.</summary>
public sealed class PeerRowModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Address { get; }
    private string _number;
    public string Number { get => _number; private set => Set(ref _number, value); }
    private bool _enabled;
    private bool _canChange;
    private string _state = "Waiting";
    private string _stratum = "—";
    private string _offset = "—";
    private string _serverTime = "—";
    private string _rtt = "—";
    private bool _isZebra;
    private IBrush _iconBrush;
    private IBrush _trackBrush;
    private IBrush _thumbBrush;
    private IBrush _statusBrush;
    private string? _lastError;
    private DateTimeOffset? _utcBase;
    private long _baseTick;
    private long _updatedTick;

    private readonly IBrush _green, _gray, _trackOff, _thumb, _warning;

    internal PeerRowModel(string address, int number, bool enabled,
        IBrush green, IBrush gray, IBrush trackOff, IBrush thumb, IBrush warning)
    {
        Address = address;
        _number = number.ToString("00", CultureInfo.InvariantCulture);
        _isZebra = number % 2 == 0;
        _green = green; _gray = gray; _trackOff = trackOff;
        _thumb = thumb; _warning = warning;
        _iconBrush = gray; _trackBrush = trackOff;
        _thumbBrush = thumb; _statusBrush = gray;
        _enabled = !enabled; // force initial Off/On visual setup
        SetEnabled(enabled);
    }

    public bool Enabled { get => _enabled; private set => Set(ref _enabled, value); }
    public bool CanChange { get => _canChange; private set => Set(ref _canChange, value); }
    public string State { get => _state; private set => Set(ref _state, value); }
    public string Stratum { get => _stratum; private set => Set(ref _stratum, value); }
    public string Offset { get => _offset; private set => Set(ref _offset, value); }
    public string ServerTime { get => _serverTime; private set => Set(ref _serverTime, value); }
    public string Rtt { get => _rtt; private set => Set(ref _rtt, value); }
    public bool IsZebra { get => _isZebra; private set => Set(ref _isZebra, value); }
    public IBrush IconBrush { get => _iconBrush; private set => Set(ref _iconBrush, value); }
    public IBrush TrackBrush { get => _trackBrush; private set => Set(ref _trackBrush, value); }
    public IBrush ThumbBrush { get => _thumbBrush; private set => Set(ref _thumbBrush, value); }
    public IBrush StatusBrush { get => _statusBrush; private set => Set(ref _statusBrush, value); }
    public string? LastError { get => _lastError; private set => Set(ref _lastError, value); }
    public HorizontalAlignment ThumbAlignment =>
        Enabled ? HorizontalAlignment.Right : HorizontalAlignment.Left;

    internal void SetEnabled(bool enabled)
    {
        if (Enabled == enabled) return;
        Enabled = enabled;
        IconBrush = enabled ? _green : _gray;
        TrackBrush = enabled ? _green : _trackOff;
        ThumbBrush = _thumb;
        OnChanged(nameof(ThumbAlignment));
        if (!enabled)
        {
            State = "Off"; StatusBrush = _gray; Stratum = "—"; Offset = "—";
            ServerTime = "—"; Rtt = "—"; LastError = null; _utcBase = null;
        }
    }

    internal void SetCanChange(bool enabled) => CanChange = enabled;
    internal void SetIndex(int index)
    {
        Number = index.ToString("00", CultureInfo.InvariantCulture);
        IsZebra = index % 2 == 0;
    }

    internal void Apply(DashboardPeer data)
    {
        if (!Enabled)
        {
            State = "Off";
            return;
        }
        bool valid = data.OffsetMilliseconds.HasValue &&
            data.EstimatedServerUtc.HasValue;
        State = data.Selected && valid ? "● Selected" : data.State;
        StatusBrush = valid ? _green : _warning;
        Stratum = valid && data.Stratum is byte b
            ? b.ToString(CultureInfo.InvariantCulture) : "—";
        Offset = valid && data.OffsetMilliseconds is double ms
            ? StudioStatusPresenter.FormatOffset(ms) : "—";
        Rtt = valid && data.RttMilliseconds is double rtt && double.IsFinite(rtt) && rtt >= 0
            ? rtt.ToString("0.00", CultureInfo.InvariantCulture) + " ms" : "—";
        LastError = data.Error;
        if (valid && data.EstimatedServerUtc is DateTimeOffset utc)
        {
            DateTimeOffset now = _utcBase is DateTimeOffset previous &&
                _baseTick > 0
                ? previous + System.Diagnostics.Stopwatch.GetElapsedTime(_baseTick)
                : utc;
            if (_utcBase is null || Math.Abs((utc - now).TotalSeconds) > 1)
            {
                _utcBase = utc;
                _baseTick = System.Diagnostics.Stopwatch.GetTimestamp();
            }
            _updatedTick = System.Diagnostics.Stopwatch.GetTimestamp();
        }
        else
        {
            _utcBase = null;
            ServerTime = "—";
        }
    }

    internal void Tick()
    {
        if (!Enabled || _utcBase is null || _baseTick == 0) return;
        if (System.Diagnostics.Stopwatch.GetElapsedTime(_updatedTick) >
            TimeSpan.FromSeconds(12))
        {
            ServerTime = "—";
            return;
        }
        var now = (_utcBase.Value +
            System.Diagnostics.Stopwatch.GetElapsedTime(_baseTick)).ToLocalTime();
        ServerTime = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private void Set<T>(ref T field, T next, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, next)) return;
        field = next;
        OnChanged(property);
    }

    private void OnChanged(string? property) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}

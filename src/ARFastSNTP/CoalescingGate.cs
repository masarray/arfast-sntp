namespace ARFastSNTP;

/// <summary>Single-flight gate; redundant refresh requests are coalesced rather than queued.</summary>
internal sealed class CoalescingGate : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    internal async Task<bool> TryEnterAsync(CancellationToken cancellationToken)
        => await _semaphore.WaitAsync(0, cancellationToken);
    internal void Exit() => _semaphore.Release();
    public void Dispose() => _semaphore.Dispose();
}

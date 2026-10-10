using Avalonia.Controls;
using Avalonia.Media;

namespace ARFastSNTP.Studio.Design;

// Same centralized brush-resolution pattern as Arssyut's ArBrushResolver.
internal static class BrushResolver
{
    internal static IBrush Require(IResourceHost host, string key)
    {
        if (host.TryFindResource(key, out var value) && value is IBrush brush)
            return brush;
        throw new InvalidOperationException($"Missing required design brush: {key}");
    }
}

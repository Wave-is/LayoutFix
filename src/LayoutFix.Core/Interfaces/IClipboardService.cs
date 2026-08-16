using System.Threading;
using System.Threading.Tasks;

namespace LayoutFix.Core.Interfaces;

public interface IClipboardSnapshot : IDisposable
{
}

public interface IClipboardService : IDisposable
{
    Task<IClipboardSnapshot> CaptureAsync(CancellationToken cancellationToken = default);
    Task RestoreAsync(IClipboardSnapshot snapshot, CancellationToken cancellationToken = default);
    Task<string?> ReadTextAsync(CancellationToken cancellationToken = default);
    Task SetTextAsync(string text, CancellationToken cancellationToken = default);
    uint GetSequenceNumber();
}

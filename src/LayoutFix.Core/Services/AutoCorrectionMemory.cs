using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Models;

namespace LayoutFix.Core.Services;

public sealed class AutoCorrectionMemory : IAutoCorrectionMemory
{
    private static readonly TimeSpan UndoLifetime = TimeSpan.FromSeconds(15);
    private readonly object _sync = new();
    private Entry? _last;
    private long _generation;

    public void Record(
        string originalText,
        string replacementText,
        string triggerText,
        ActiveWindowContext window)
    {
        if (string.IsNullOrEmpty(originalText) || string.IsNullOrEmpty(replacementText))
            return;

        lock (_sync)
        {
            _last = new Entry(
                ++_generation,
                originalText,
                replacementText,
                triggerText,
                window,
                DateTimeOffset.UtcNow);
        }
    }

    public bool TryPrepareUndo(
        TextSelection selection,
        out AutoCorrectionUndoCandidate candidate)
    {
        lock (_sync)
        {
            var entry = _last;
            if (entry == null ||
                DateTimeOffset.UtcNow - entry.CreatedAt > UndoLifetime ||
                entry.Window != selection.Window)
            {
                _last = null;
                candidate = default;
                return false;
            }

            string restored;
            if (string.Equals(selection.Text, entry.ReplacementText, StringComparison.Ordinal))
            {
                restored = entry.OriginalText;
            }
            else if (string.Equals(
                         selection.Text,
                         entry.ReplacementText + entry.TriggerText,
                         StringComparison.Ordinal))
            {
                restored = entry.OriginalText + entry.TriggerText;
            }
            else
            {
                candidate = default;
                return false;
            }

            candidate = new AutoCorrectionUndoCandidate(
                entry.Generation,
                entry.OriginalText,
                restored);
            return true;
        }
    }

    public void CommitUndo(long generation)
    {
        lock (_sync)
        {
            if (_last?.Generation == generation)
                _last = null;
        }
    }

    private sealed record Entry(
        long Generation,
        string OriginalText,
        string ReplacementText,
        string TriggerText,
        ActiveWindowContext Window,
        DateTimeOffset CreatedAt);
}

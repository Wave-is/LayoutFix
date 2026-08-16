using LayoutFix.Core.Models;

namespace LayoutFix.Core.Interfaces;

public interface IAutoCorrectionMemory
{
    void Record(
        string originalText,
        string replacementText,
        string triggerText,
        ActiveWindowContext window);

    bool TryPrepareUndo(
        TextSelection selection,
        out AutoCorrectionUndoCandidate candidate);

    void CommitUndo(long generation);
}

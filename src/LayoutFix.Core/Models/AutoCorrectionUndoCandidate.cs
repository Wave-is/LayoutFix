namespace LayoutFix.Core.Models;

public readonly record struct AutoCorrectionUndoCandidate(
    long Generation,
    string OriginalText,
    string RestoredSelectionText);

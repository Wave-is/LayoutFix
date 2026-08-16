namespace LayoutFix.Core.Models;

public readonly record struct LayoutCorrectionSuggestion(
    string Replacement,
    string TargetLayoutCode,
    bool IsConfidentForAutomaticCorrection = true);

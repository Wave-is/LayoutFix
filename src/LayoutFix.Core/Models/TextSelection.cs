namespace LayoutFix.Core.Models;

public sealed record TextSelection(
    string Text,
    ActiveWindowContext Window,
    bool WasSelectedByFallback,
    long? KeyboardInputGeneration = null,
    long? MouseInputGeneration = null,
    string? DirectAdapterId = null,
    long? DiagnosticCaptureId = null,
    bool AllowTargetLayoutActivation = true);

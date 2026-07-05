namespace LayoutFix.Core.Models;

/// <summary>
/// Represents the possible actions triggered by a hotkey.
/// </summary>
public enum HotkeyAction
{
    /// <summary>
    /// Fixes the layout of the last typed word or selected text.
    /// </summary>
    FixLayout,
    FixLayoutSelected,

    /// <summary>
    /// Cycles the case of the selected text.
    /// </summary>
    ChangeCase,

    /// <summary>
    /// Transliterates the text.
    /// </summary>
    Transliterate,

    /// <summary>
    /// Converts numbers in the text to their word representation.
    /// </summary>
    NumberToText,

    /// <summary>
    /// Pastes clipboard content as plain text.
    /// </summary>
    PastePlain,

    /// <summary>
    /// Switches the active Windows keyboard layout.
    /// </summary>
    SwitchLayout,

    /// <summary>
    /// Converts text to a specific layout index (e.g. 1st, 2nd).
    /// </summary>
    ConvertToLayoutN,

    ConvertToEnglish,
    ConvertToRussian,
    ConvertToUkrainian,

    /// <summary>
    /// Switches Windows to a specific layout index.
    /// </summary>
    SwitchToLayoutN,

    /// <summary>
    /// Undoes the last operation.
    /// </summary>
    Undo,

    Translate1,
    Translate2,
    Translate3,
    OpenTranslator
}

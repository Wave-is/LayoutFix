using LayoutFix.Infrastructure.Services;
using System.Windows.Forms;

namespace LayoutFix.IntegrationTests;

public class AdobeInlineRenameTextAdapterTests
{
    [Theory]
    [InlineData("AfterFX", "AE_CApplication_25.0", "Edit", "after-effects-rename-v1")]
    [InlineData("afterfx", "AE_CApplication_25.3", "Edit", "after-effects-rename-v1")]
    [InlineData("Adobe Premiere Pro", "Premiere Pro", "Edit", "premiere-rename-v1")]
    [InlineData("Adobe Premiere Pro", "DroverLord - Window Class", "Edit", "premiere-rename-v1")]
    [InlineData("Photoshop", "#32770", "Edit", "photoshop-save-dialog-v1")]
    [InlineData("photoshop", "#32770", "Edit", "photoshop-save-dialog-v1")]
    public void ResolveAdapterId_AcceptsOnlyProvenAdobeApplicationProfiles(
        string processName,
        string mainClass,
        string focusedClass,
        string expectedAdapterId)
    {
        Assert.Equal(
            expectedAdapterId,
            AdobeInlineRenameTextAdapter.ResolveAdapterId(
                processName,
                mainClass,
                focusedClass));
    }

    [Theory]
    [InlineData("AfterFX", "AE_CApplication_25.0", "Button")]
    [InlineData("AfterFX", "ae_capplication_25.0", "Edit")]
    [InlineData("AfterFX-helper", "AE_CApplication_25.0", "Edit")]
    [InlineData("Adobe Premiere Pro", "Premiere Pro", "RichEdit20W")]
    [InlineData("Adobe Premiere Pro", "Premiere Pro 2025", "Edit")]
    [InlineData("Adobe Premiere Pro", "DroverLord", "Edit")]
    [InlineData("Premiere Pro", "Premiere Pro", "Edit")]
    [InlineData("Photoshop", "Premiere Pro", "Edit")]
    [InlineData("Photoshop", "#32770", "RichEdit20W")]
    [InlineData("Photoshop Helper", "#32770", "Edit")]
    public void ResolveAdapterId_RejectsLookalikeOrUnprovenProfiles(
        string processName,
        string mainClass,
        string focusedClass)
    {
        Assert.Null(AdobeInlineRenameTextAdapter.ResolveAdapterId(
            processName,
            mainClass,
            focusedClass));
    }

    [Theory]
    [InlineData("photoshop-save-dialog-v1", true)]
    [InlineData("premiere-rename-v1", false)]
    [InlineData("after-effects-rename-v1", false)]
    [InlineData("", false)]
    public void NativeDialogContract_IsRestrictedToExactWindowsDialogProfile(
        string adapterId,
        bool expected)
    {
        Assert.Equal(
            expected,
            AdobeInlineRenameTextAdapter.UsesNativeDialogContract(adapterId));
    }

    [Theory]
    [InlineData("UI_TextEdit", "TEST")]
    [InlineData("TEST", "TEST")]
    [InlineData("привет", "привет")]
    public void IsSupportedAccessibleName_AcceptsAdobeStableOrValueBackedName(
        string accessibleName,
        string currentValue)
    {
        Assert.True(AdobeInlineRenameTextAdapter.IsSupportedAccessibleName(
            accessibleName,
            currentValue));
    }

    [Fact]
    public void IsSupportedAccessibleName_AcceptsCapturedNameWhileAdobeValueUpdates()
    {
        Assert.True(AdobeInlineRenameTextAdapter.IsSupportedAccessibleName(
            "ghbdtn",
            "привет",
            expectedAccessibleName: "ghbdtn"));
    }

    [Theory]
    [InlineData("Other", "TEST")]
    [InlineData("test", "TEST")]
    [InlineData("", "TEST")]
    [InlineData("", "")]
    public void IsSupportedAccessibleName_RejectsAmbiguousOrMismatchedName(
        string accessibleName,
        string currentValue)
    {
        Assert.False(AdobeInlineRenameTextAdapter.IsSupportedAccessibleName(
            accessibleName,
            currentValue));
    }

    [Fact]
    public void IsSupportedAccessibleName_RejectsUnrelatedNameDuringReplacement()
    {
        Assert.False(AdobeInlineRenameTextAdapter.IsSupportedAccessibleName(
            "Other",
            "привет",
            expectedAccessibleName: "ghbdtn"));
    }

    [Fact]
    public void NativeEditReplacement_ChangesOnlySelectedTextWithoutClipboard()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var editor = new TextBox { Text = "before ghbdtn after" };
                _ = editor.Handle;
                editor.Select(7, 6);

                var replaced = AdobeInlineRenameTextAdapter.TryReplaceNativeSelection(
                    editor.Handle,
                    editor.Text,
                    "ghbdtn",
                    "привет",
                    out var expectedValue);

                Assert.True(replaced);
                Assert.Equal("before привет after", expectedValue);
                Assert.Equal(expectedValue, editor.Text);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        if (failure != null)
            throw failure;
    }

    [Fact]
    public void NativeEditCapture_ReadsOnlySelectedTextWithoutClipboard()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var editor = new TextBox { Text = "before ghbdtn after" };
                _ = editor.Handle;
                editor.Select(7, 6);

                var captured = AdobeInlineRenameTextAdapter.TryReadNativeEditSelection(
                    editor.Handle,
                    out var currentValue,
                    out var selectedText);

                Assert.True(captured);
                Assert.Equal(editor.Text, currentValue);
                Assert.Equal("ghbdtn", selectedText);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        if (failure != null)
            throw failure;
    }
}

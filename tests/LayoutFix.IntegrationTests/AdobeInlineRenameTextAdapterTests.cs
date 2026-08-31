using LayoutFix.Core.Interfaces;
using LayoutFix.Infrastructure.Services;
using System.Windows.Forms;

namespace LayoutFix.IntegrationTests;

public class AdobeInlineRenameTextAdapterTests
{
    [Theory]
    [InlineData("AfterFX", "AE_CApplication_25.0", "Edit", "after-effects-rename-paste-v2")]
    [InlineData("afterfx", "AE_CApplication_25.3", "Edit", "after-effects-rename-paste-v2")]
    [InlineData("Adobe Premiere Pro", "Premiere Pro", "Edit", "premiere-rename-paste-v2")]
    [InlineData("Adobe Premiere Pro", "DroverLord - Window Class", "Edit", "premiere-rename-paste-v2")]
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

    [Fact]
    public void AllAdobeAdaptersUseClipboardPasteAndRejectLegacyPremiereContract()
    {
        Assert.Equal(
            AdobeInlineRenameTextAdapter.ReplacementContract.ClipboardPaste,
            AdobeInlineRenameTextAdapter.ResolveReplacementContract(
                "premiere-rename-paste-v2"));
        Assert.Equal(
            AdobeInlineRenameTextAdapter.ReplacementContract.ClipboardPaste,
            AdobeInlineRenameTextAdapter.ResolveReplacementContract(
                "after-effects-rename-paste-v2"));
        Assert.Equal(
            AdobeInlineRenameTextAdapter.ReplacementContract.ClipboardPaste,
            AdobeInlineRenameTextAdapter.ResolveReplacementContract(
                "photoshop-save-dialog-v1"));
        Assert.Equal(
            AdobeInlineRenameTextAdapter.ReplacementContract.Rejected,
            AdobeInlineRenameTextAdapter.ResolveReplacementContract(
                "premiere-rename-v1"));
    }

    [Fact]
    public async Task ClipboardPasteTransaction_RevalidatesPastesAndRestoresClipboard()
    {
        var clipboard = new AdapterClipboard("user-rich-clipboard");
        var target = "ghbdtn";
        var input = new AdapterInput(() => target = clipboard.Value);

        var result = await AdobeInlineRenameTextAdapter.ExecuteClipboardPasteAsync(
            input,
            clipboard,
            "привет",
            _ => Task.FromResult(target == "ghbdtn" && clipboard.Value == "привет"),
            _ => Task.FromResult(target == "привет"),
            CancellationToken.None);

        Assert.True(result);
        Assert.Equal(1, input.PasteCount);
        Assert.Equal("привет", target);
        Assert.Equal("user-rich-clipboard", clipboard.Value);
        Assert.Equal(1, clipboard.RestoreCount);
    }

    [Fact]
    public async Task ClipboardPasteTransaction_RevalidationFailureRestoresWithoutInput()
    {
        var clipboard = new AdapterClipboard("user-rich-clipboard");
        var input = new AdapterInput(() => throw new InvalidOperationException());

        var result = await AdobeInlineRenameTextAdapter.ExecuteClipboardPasteAsync(
            input,
            clipboard,
            "привет",
            _ => Task.FromResult(false),
            _ => Task.FromResult(false),
            CancellationToken.None);

        Assert.False(result);
        Assert.Equal(0, input.PasteCount);
        Assert.Equal("user-rich-clipboard", clipboard.Value);
        Assert.Equal(1, clipboard.RestoreCount);
    }

    [Fact]
    public async Task ClipboardPasteTransaction_InputFailureStillRestoresClipboard()
    {
        var clipboard = new AdapterClipboard("user-rich-clipboard");
        var input = new AdapterInput(() => throw new InvalidOperationException("blocked"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AdobeInlineRenameTextAdapter.ExecuteClipboardPasteAsync(
                input,
                clipboard,
                "привет",
                _ => Task.FromResult(true),
                _ => Task.FromResult(false),
                CancellationToken.None));

        Assert.Equal(1, input.PasteCount);
        Assert.Equal("user-rich-clipboard", clipboard.Value);
        Assert.Equal(1, clipboard.RestoreCount);
    }

    private sealed class AdapterInput(Action paste) : IInputInjector
    {
        public int PasteCount { get; private set; }

        public Task SendKeyCombinationAsync(bool ctrl, bool alt, bool shift, string key)
        {
            Assert.True(ctrl);
            Assert.False(alt);
            Assert.False(shift);
            Assert.Equal("v", key);
            PasteCount++;
            paste();
            return Task.CompletedTask;
        }

        public Task SendBackspacesAsync(int count) => Task.CompletedTask;
        public Task SendTextAsync(string text) => Task.CompletedTask;
        public Task SelectWordLeftAsync() => Task.CompletedTask;
        public Task WaitForModifiersReleaseAsync(int timeoutMs = 2000) => Task.CompletedTask;
    }

    private sealed class AdapterClipboard(string value) : IClipboardService
    {
        public string Value { get; private set; } = value;
        public int RestoreCount { get; private set; }

        public Task<IClipboardSnapshot> CaptureAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IClipboardSnapshot>(new AdapterSnapshot(Value));

        public Task RestoreAsync(
            IClipboardSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            Value = Assert.IsType<AdapterSnapshot>(snapshot).Value;
            RestoreCount++;
            return Task.CompletedTask;
        }

        public Task<string?> ReadTextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(Value);

        public Task SetTextAsync(
            string text,
            CancellationToken cancellationToken = default)
        {
            Value = text;
            return Task.CompletedTask;
        }

        public uint GetSequenceNumber() => 0;
        public void Dispose() { }
    }

    private sealed record AdapterSnapshot(string Value) : IClipboardSnapshot
    {
        public void Dispose() { }
    }
}

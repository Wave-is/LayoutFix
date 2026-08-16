using System.Drawing;
using LayoutFix.Core.Interfaces;
using LayoutFix.UI.Controls;

namespace LayoutFix.Services;

public sealed class PopupService : IPopupService, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly Control _dispatcher;
    private bool _disposed;

    public PopupService(ISettingsService settings)
    {
        _settings = settings;
        _dispatcher = new Control();
        _ = _dispatcher.Handle;
    }

    public void ShowTranslationPopup(string text) => Dispatch(() =>
    {
        var popup = new TranslationPopupForm(
            text,
            Color.FromArgb(240, 240, 240),
            Color.Black);
        popup.Show();
    });

    public void ShowStatus(string message, bool isError = false)
    {
        if (!_settings.Current.NotificationsEnabled || string.IsNullOrWhiteSpace(message))
            return;

        Dispatch(() =>
        {
            var popup = new TranslationPopupForm(
                message,
                isError ? Color.FromArgb(96, 35, 35) : Color.FromArgb(45, 48, 54),
                Color.White,
                autoCloseMilliseconds: 2_500,
                copyOnClick: false);
            popup.Show();
        });
    }

    private void Dispatch(Action action)
    {
        if (_disposed || _dispatcher.IsDisposed) return;
        try
        {
            if (_dispatcher.InvokeRequired)
                _dispatcher.BeginInvoke(action);
            else
                action();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ObjectDisposedException)
        {
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _dispatcher.Dispose();
    }
}

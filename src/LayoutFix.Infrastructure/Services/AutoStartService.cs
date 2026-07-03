using System;
using System.Diagnostics;
using Microsoft.Win32;
using LayoutFix.Core.Interfaces;

namespace LayoutFix.Infrastructure.Services;

public class AutoStartService : IAutoStartService
{
    private const string RegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "LayoutFix";

    public bool IsAutoStartEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, false);
                var value = key?.GetValue(AppName) as string;
                return value == GetExecutablePath();
            }
            catch
            {
                return false;
            }
        }
        set
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, true);
                if (key == null) return;

                if (value)
                {
                    key.SetValue(AppName, GetExecutablePath());
                }
                else
                {
                    if (key.GetValue(AppName) != null)
                    {
                        key.DeleteValue(AppName, false);
                    }
                }
            }
            catch
            {
                // Ignore permissions/registry errors
            }
        }
    }

    private string GetExecutablePath()
    {
        using var process = Process.GetCurrentProcess();
        return process.MainModule?.FileName ?? string.Empty;
    }
}

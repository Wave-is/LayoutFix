using Microsoft.Extensions.DependencyInjection;
using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Services;
using LayoutFix.Infrastructure.Services;
using LayoutFix.UI;
using Microsoft.Win32;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace LayoutFix;

internal static class Program
{
    private static Mutex? _mutex;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length is 2 or 3 &&
            string.Equals(args[0], "--translation-worker", StringComparison.OrdinalIgnoreCase))
        {
            return LayoutFix.Services.TranslationWorkerLauncher
                .RunAsync(args[1], args.Length == 3 ? args[2] : null)
                .GetAwaiter()
                .GetResult();
        }

        if (args.Any(argument => string.Equals(argument, "--smoke-test", StringComparison.OrdinalIgnoreCase)))
            return RunSmokeTest();
        if (args.Length == 2 &&
            string.Equals(args[0], "--startup-lifecycle-test", StringComparison.OrdinalIgnoreCase))
        {
            return RunStartupLifecycleTest(
                args[1],
                injectFirstHookFailure: false,
                simulateSessionRecovery: false);
        }
        if (args.Length == 2 &&
            string.Equals(args[0], "--startup-recovery-test", StringComparison.OrdinalIgnoreCase))
        {
            return RunStartupLifecycleTest(
                args[1],
                injectFirstHookFailure: true,
                simulateSessionRecovery: false);
        }
        if (args.Length == 2 &&
            string.Equals(args[0], "--session-recovery-test", StringComparison.OrdinalIgnoreCase))
        {
            return RunStartupLifecycleTest(
                args[1],
                injectFirstHookFailure: false,
                simulateSessionRecovery: true);
        }

        const string appName = "LayoutFix_SingleInstance_Mutex";
        _mutex = new Mutex(true, appName, out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("LayoutFix is already running.", "LayoutFix", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _mutex.Dispose();
            _mutex = null;
            return 0;
        }

        IKeyboardHook? keyboardHook = null;
        IMouseHook? mouseHook = null;
        System.Windows.Forms.Timer? hookRecoveryTimer = null;
        try
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (_, eventArgs) => ReportUnexpectedError("UI thread error", eventArgs.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
                ReportUnexpectedError("Unhandled application error", eventArgs.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
            {
                ReportUnexpectedError("Unobserved background task error", eventArgs.Exception);
                eventArgs.SetObserved();
            };

            AppHost.Build();
            var services = AppHost.Services!;
            _ = services.GetRequiredService<TrayManager>();
            keyboardHook = services.GetRequiredService<IKeyboardHook>();
            mouseHook = services.GetRequiredService<IMouseHook>();
            var hookRecovery = services.GetRequiredService<HookRecoveryCoordinator>();
            var sessionRecovery = services.GetRequiredService<
                LayoutFix.Services.WindowsSessionRecoveryMonitor>();
            var coordinator = services.GetRequiredService<IHotkeyCoordinator>();
            var settings = services.GetRequiredService<ISettingsService>();
            var autoStart = services.GetRequiredService<IAutoStartService>();
            services.GetRequiredService<ILocalizationService>().SetCulture(settings.Current.UiLanguage);

            try
            {
                LayoutFix.Services.AutoStartSynchronizer.Synchronize(settings, autoStart);
            }
            catch (Exception ex)
            {
                services.GetRequiredService<ILoggerService>().LogError("Failed to synchronize autostart settings", ex);
            }

            coordinator.Initialize();
            _ = services.GetRequiredService<AutoConversionService>();
            var dictionaryAnalyzer = services.GetRequiredService<DictionaryAnalyzer>();
            var logger = services.GetRequiredService<ILoggerService>();
            LogApplicationIdentity(logger);
            try
            {
                // The first manual correction must not synchronously load several
                // dictionaries while the user is waiting on a global hotkey. Pay
                // this bounded cost during startup, before hooks become operational.
                dictionaryAnalyzer.WarmUp();
            }
            catch (Exception ex)
            {
                logger.LogError(
                    "Dictionary warm-up failed; on-demand loading remains available.",
                    ex);
            }

            hookRecoveryTimer = new System.Windows.Forms.Timer { Interval = 5_000 };
            hookRecoveryTimer.Tick += (_, _) => hookRecovery.RecoverIfRequested();
            hookRecoveryTimer.Start();
            sessionRecovery.Start();

            // A transient SetWindowsHookEx failure must not terminate the tray
            // process. The coordinator keeps the request pending and the UI
            // timer retries on the message-loop thread until both hooks start.
            hookRecovery.RequestRecovery();
            hookRecovery.RecoverIfRequested();

            Application.Run();
        }
        catch (Exception ex)
        {
            ReportUnexpectedError("LayoutFix could not start", ex);
            MessageBox.Show(
                "LayoutFix could not start. Check diagnostic logging or reinstall the application.",
                "LayoutFix",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            try
            {
                hookRecoveryTimer?.Stop();
                hookRecoveryTimer?.Dispose();
            }
            catch { }
            try { keyboardHook?.Stop(); } catch { }
            try { mouseHook?.Stop(); } catch { }
            AppHost.Shutdown();

            if (_mutex != null)
            {
                try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
                _mutex.Dispose();
                _mutex = null;
            }
        }

        return 0;
    }

    private static void LogApplicationIdentity(ILoggerService logger)
    {
        var assembly = typeof(Program).Assembly;
        var assemblyName = assembly.GetName();
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "unknown";
        var executableName = Path.GetFileName(Environment.ProcessPath) ?? "unknown";
        logger.LogInfo(
            "Application identity: " +
            $"Version={assemblyName.Version}; " +
            $"InformationalVersion={informationalVersion}; " +
            $"ExecutableName={executableName}; " +
            $"ProcessArchitecture={RuntimeInformation.ProcessArchitecture}; " +
            $"Runtime={Environment.Version}.");
    }

    private static int RunSmokeTest()
    {
        var smokeDirectory = Path.Combine(
            Path.GetTempPath(),
            $"LayoutFix.Smoke.{Guid.NewGuid():N}");
        var succeeded = false;
        try
        {
            SmokeStep("profile:isolated");
            SmokeStep("build:start");
            AppHost.Build(
                Path.Combine(smokeDirectory, "settings.json"),
                Path.Combine(smokeDirectory, "translation_history.json"),
                Path.Combine(smokeDirectory, "layoutfix.log"));
            SmokeStep("build:complete");
            var services = AppHost.Services!;
            services.GetRequiredService<ISettingsService>();
            SmokeStep("settings:resolved");
            services.GetRequiredService<IHotkeyCoordinator>();
            SmokeStep("hotkeys:resolved");
            services.GetRequiredService<ITextTransactionService>();
            SmokeStep("text-transaction:resolved");
            services.GetRequiredService<ITranslationCoordinator>();
            SmokeStep("translation:resolved");
            services.GetRequiredService<AutoConversionService>();
            SmokeStep("auto-conversion:resolved");
            services.GetRequiredService<IKeyboardLayoutManager>().LoadAll();
            SmokeStep("layouts:loaded");
            services.GetRequiredService<DictionaryAnalyzer>().WarmUp();
            SmokeStep("dictionaries:warmed");
            succeeded = true;
        }
        catch
        {
            SmokeStep("failed");
        }
        finally
        {
            try
            {
                AppHost.Shutdown();
                SmokeStep("shutdown:complete");
            }
            catch
            {
                SmokeStep("shutdown:failed");
                succeeded = false;
            }
            try
            {
                if (Directory.Exists(smokeDirectory))
                    Directory.Delete(smokeDirectory, recursive: true);
                SmokeStep("profile:cleaned");
            }
            catch
            {
                SmokeStep("profile-cleanup:failed");
                succeeded = false;
            }
        }

        return succeeded ? 0 : 1;
    }

    private static int RunStartupLifecycleTest(
        string resultPath,
        bool injectFirstHookFailure,
        bool simulateSessionRecovery)
    {
        var fullResultPath = Path.GetFullPath(resultPath);
        var tempRoot = Path.GetFullPath(Path.GetTempPath())
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullResultPath.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) ||
            File.Exists(fullResultPath))
        {
            return 2;
        }

        var profileDirectory = Path.Combine(
            Path.GetTempPath(),
            $"LayoutFix.Startup.{Guid.NewGuid():N}");
        var logFilePath = Path.Combine(profileDirectory, "layoutfix.log");
        IKeyboardHook? keyboardHook = null;
        IMouseHook? mouseHook = null;
        System.Windows.Forms.Timer? readinessTimer = null;
        System.Windows.Forms.Timer? hookRecoveryTimer = null;
        ApplicationContext? applicationContext = null;
        var result = "startup_lifecycle=fail reason=initialization";
        var exitCode = 1;

        try
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            AppHost.BuildForStartupLifecycle(
                Path.Combine(profileDirectory, "settings.json"),
                Path.Combine(profileDirectory, "translation_history.json"),
                logFilePath,
                injectFirstHookFailure ? 5 : 0);
            var services = AppHost.Services!;
            _ = services.GetRequiredService<TrayManager>();
            keyboardHook = services.GetRequiredService<IKeyboardHook>();
            mouseHook = services.GetRequiredService<IMouseHook>();
            var hookRecovery = services.GetRequiredService<HookRecoveryCoordinator>();
            var sessionRecovery = services.GetRequiredService<
                LayoutFix.Services.WindowsSessionRecoveryMonitor>();
            var coordinator = services.GetRequiredService<IHotkeyCoordinator>();
            var settings = services.GetRequiredService<ISettingsService>();
            settings.Current.LoggingEnabled = true;
            var lifecycleLogger = services.GetRequiredService<ILoggerService>();
            services.GetRequiredService<ILocalizationService>()
                .SetCulture(settings.Current.UiLanguage);

            coordinator.Initialize();
            _ = services.GetRequiredService<AutoConversionService>();
            var dictionaryWarmUp = Task.Run(
                services.GetRequiredService<DictionaryAnalyzer>().WarmUp);

            hookRecoveryTimer = new System.Windows.Forms.Timer { Interval = 5_000 };
            hookRecoveryTimer.Tick += (_, _) => hookRecovery.RecoverIfRequested();
            hookRecoveryTimer.Start();

            hookRecovery.RequestRecovery();
            var initialRecoverySucceeded = hookRecovery.RecoverIfRequested();
            if (injectFirstHookFailure && initialRecoverySucceeded)
                throw new InvalidOperationException("Startup hook failure was not injected.");
            if (simulateSessionRecovery)
            {
                if (sessionRecovery.HandlePowerModeChanged(PowerModes.Suspend) ||
                    sessionRecovery.HandleSessionSwitch(SessionSwitchReason.SessionLock) ||
                    !sessionRecovery.HandlePowerModeChanged(PowerModes.Resume) ||
                    !sessionRecovery.HandleSessionSwitch(SessionSwitchReason.SessionUnlock) ||
                    !sessionRecovery.HandleSessionSwitch(SessionSwitchReason.RemoteConnect) ||
                    !sessionRecovery.HandleSessionSwitch(SessionSwitchReason.ConsoleConnect))
                {
                    throw new InvalidOperationException("Windows session recovery mapping failed.");
                }
            }

            applicationContext = new ApplicationContext();
            var deadline = Stopwatch.StartNew();
            var expectedAttempts = injectFirstHookFailure ? 6 : simulateSessionRecovery ? 2 : 1;
            var expectedFailures = injectFirstHookFailure ? 5 : 0;
            var expectedFailureDiagnostics = injectFirstHookFailure ? 4 : 0;
            var expectedSuppressedDiagnostics = injectFirstHookFailure ? 1 : 0;
            var expectedRequests = simulateSessionRecovery ? 5 : 1;
            readinessTimer = new System.Windows.Forms.Timer { Interval = 25 };
            readinessTimer.Tick += (_, _) =>
            {
                if (dictionaryWarmUp.IsFaulted)
                {
                    result = "startup_lifecycle=fail reason=dictionary-warmup";
                    applicationContext.ExitThread();
                    return;
                }

                if (hookRecovery.IsOperational &&
                    !hookRecovery.HasPendingRecovery &&
                    hookRecovery.RecoveryAttemptCount == expectedAttempts &&
                    hookRecovery.RecoveryFailureCount == expectedFailures &&
                    hookRecovery.LastSuppressedFailureDiagnosticCount ==
                        expectedSuppressedDiagnostics &&
                    hookRecovery.RecoveryRequestCount == expectedRequests &&
                    dictionaryWarmUp.IsCompletedSuccessfully &&
                    Application.MessageLoop)
                {
                    if (lifecycleLogger is FileLoggerService fileLogger)
                        fileLogger.Flush();
                    var log = File.Exists(logFilePath)
                        ? File.ReadAllText(logFilePath)
                        : string.Empty;
                    var failureDiagnostics = log.Split(Environment.NewLine)
                        .Count(line => line.Contains(
                            "Global input hook recovery attempt",
                            StringComparison.Ordinal));
                    var recoveryDiagnostic = injectFirstHookFailure
                        ? "Global input hooks recovered on attempt 6 after " +
                          "5 consecutive failures over "
                        : simulateSessionRecovery
                            ? "Global input hooks installed on attempt 2."
                            : "Global input hooks installed on attempt 1.";
                    var sessionDiagnostics = log.Split(Environment.NewLine)
                        .Count(line => line.Contains(
                            "Global input hook recovery requested after Windows",
                            StringComparison.Ordinal));
                    var suppressionDiagnostic =
                        $"(suppressed failure diagnostics: {expectedSuppressedDiagnostics}).";
                    var durationValid = !injectFirstHookFailure ||
                        hookRecovery.LastDegradationDuration >= TimeSpan.FromSeconds(20);
                    if (failureDiagnostics != expectedFailureDiagnostics ||
                        sessionDiagnostics != (simulateSessionRecovery ? 4 : 0) ||
                        !log.Contains(recoveryDiagnostic, StringComparison.Ordinal) ||
                        (injectFirstHookFailure &&
                         !log.Contains(suppressionDiagnostic, StringComparison.Ordinal)) ||
                        !durationValid)
                    {
                        result = "startup_lifecycle=fail reason=recovery-diagnostics";
                        applicationContext.ExitThread();
                        return;
                    }

                    var recovery = hookRecovery.RecoveryAttemptCount == 1 ? "initial" : "retry";
                    var firstAttempt = initialRecoverySucceeded ? "succeeded" : "failed";
                    result =
                        "startup_lifecycle=pass tray=ready message_loop=active " +
                        "hooks=operational dictionaries=warmed profile=isolated " +
                        $"autostart=untouched recovery={recovery} " +
                        $"first_attempt={firstAttempt} " +
                        $"attempts={hookRecovery.RecoveryAttemptCount} " +
                        $"failures={hookRecovery.RecoveryFailureCount} " +
                        $"suppressed={hookRecovery.LastSuppressedFailureDiagnosticCount} " +
                        $"degradation_ms={Math.Max(0, (long)hookRecovery.LastDegradationDuration.TotalMilliseconds)} " +
                        $"requests={hookRecovery.RecoveryRequestCount} " +
                        $"session={(simulateSessionRecovery ? "coalesced" : "none")} " +
                        "diagnostics=verified";
                    exitCode = 0;
                    applicationContext.ExitThread();
                    return;
                }

                if (deadline.Elapsed >= TimeSpan.FromSeconds(35))
                {
                    result = "startup_lifecycle=fail reason=readiness-timeout";
                    applicationContext.ExitThread();
                }
            };
            readinessTimer.Start();
            Application.Run(applicationContext);
        }
        catch (Exception exception)
        {
            result = $"startup_lifecycle=fail reason={exception.GetType().Name}";
        }
        finally
        {
            try { readinessTimer?.Stop(); } catch { }
            try { readinessTimer?.Dispose(); } catch { }
            try { hookRecoveryTimer?.Stop(); } catch { }
            try { hookRecoveryTimer?.Dispose(); } catch { }
            try { applicationContext?.Dispose(); } catch { }
            try { keyboardHook?.Stop(); } catch { }
            try { mouseHook?.Stop(); } catch { }
            try { AppHost.Shutdown(); } catch { exitCode = 1; }
            try
            {
                if (Directory.Exists(profileDirectory))
                    Directory.Delete(profileDirectory, recursive: true);
            }
            catch
            {
                result = "startup_lifecycle=fail reason=profile-cleanup";
                exitCode = 1;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(fullResultPath)!);
                using var stream = new FileStream(
                    fullResultPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.Read);
                using var writer = new StreamWriter(stream);
                writer.Write(result);
            }
            catch
            {
                exitCode = 1;
            }
        }

        return exitCode;
    }

    private static void SmokeStep(string step)
    {
        var logPath = Environment.GetEnvironmentVariable("LAYOUTFIX_SMOKE_LOG");
        if (string.IsNullOrWhiteSpace(logPath)) return;

        try
        {
            File.AppendAllText(logPath, $"{DateTime.UtcNow:O} {step}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    private static void ReportUnexpectedError(string message, Exception? exception)
    {
        try
        {
            AppHost.Services?.GetService<ILoggerService>()?.LogError(message, exception);
        }
        catch
        {
            // Error reporting must never cause a second application failure.
        }
    }
}

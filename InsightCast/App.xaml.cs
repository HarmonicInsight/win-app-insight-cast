namespace InsightCast;

using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using InsightCast.Core;
using InsightCast.Services;
using InsightCast.Video;
using InsightCast.Views;
using InsightCast.VoiceVox;
using Syncfusion.SfSkinManager;

/// <summary>
/// Application entry point. Handles first-run setup, engine discovery,
/// FFmpeg initialisation, and main window creation.
/// </summary>
public partial class App : Application
{
    private VoiceVoxClient? _voiceVoxClient;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Syncfusion ライセンス設定（Essential Studio 32.x - Community License）
        Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(
            "Ngo9BigBOggjHTQxAR8/V1JGaF5cXGpCf1FpRmJGdld5fUVHYVZUTXxaS00DNHVRdkdlWX1ccXVXQ2ZYVUF2XkBWYEs=");

        // Syncfusion テーマはRibbonのみに適用（カスタムタイトルバーの上書き防止）
        SfSkinManager.ApplyStylesOnApplication = false;

        // Global unhandled exception handlers to prevent silent crashes
        DispatcherUnhandledException += (_, args) =>
        {
            CrashReporter.WriteCrashReport(args.Exception, "DispatcherUnhandledException");
            MessageBox.Show(
                LocalizationService.GetString("App.Error.Unexpected", SanitizeErrorMessage(args.Exception.Message)),
                LocalizationService.GetString("App.Error.Unexpected.Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                CrashReporter.WriteCrashReport(ex, "AppDomain.UnhandledException");
                MessageBox.Show(
                    LocalizationService.GetString("App.Error.Fatal", SanitizeErrorMessage(ex.Message)),
                    LocalizationService.GetString("App.Error.Fatal.Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            CrashReporter.WriteCrashReport(args.Exception, "UnobservedTaskException");
            args.SetObserved();
        };

        try
        {
            StartupCore(e);
        }
        catch (Exception ex)
        {
            CrashReporter.WriteCrashReport(ex, "StartupCore");
            MessageBox.Show(
                LocalizationService.GetString("App.Error.Startup", ex.ToString()),
                LocalizationService.GetString("App.Error.Startup.Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
        }
    }

    private void StartupCore(StartupEventArgs e)
    {
        // ── 1. Load configuration (fast) ──────────────────────────────────
        var config = new Config();

        // ── 1.5. Initialize localization (fast) ─────────────────────────────
        LocalizationService.Initialize(config.Language);

        if (config.LoadFailed)
        {
            MessageBox.Show(
                LocalizationService.GetString("App.Config.Corrupted"),
                LocalizationService.GetString("App.Config.Corrupted.Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        // ── 2. First-run setup wizard ──────────────────────────────
        VoiceVoxClient? wizardClient = null;

        if (config.IsFirstRun)
        {
            var wizard = new SetupWizard();
            var result = wizard.ShowDialog();

            if (result == true)
            {
                wizardClient = wizard.GetClient();
                var wizardSpeakerId = wizard.GetSpeakerId();

                config.BeginUpdate();
                if (wizardClient != null)
                    config.EngineUrl = wizardClient.BaseUrl;
                if (wizardSpeakerId >= 0)
                    config.DefaultSpeakerId = wizardSpeakerId;
                config.IsFirstRun = false;
                config.EndUpdate();
            }
            else
            {
                Shutdown();
                return;
            }
        }

        // ── 3. Create VOICEVOX client (no connection check yet) ────────────
        var client = wizardClient ?? new VoiceVoxClient(config.EngineUrl);
        _voiceVoxClient = client;

        // ── 4. FFmpeg wrapper (fast - just path lookup) ────────────────────
        FFmpegWrapper? ffmpeg = null;
        try
        {
            ffmpeg = new FFmpegWrapper();
        }
        catch (Exception ex)
        {
            // Defer error message to avoid blocking startup
            _ = Task.Run(() => Dispatcher.Invoke(() =>
                MessageBox.Show(
                    LocalizationService.GetString("App.FFmpeg.Error", ex.Message),
                    "Insight Training Studio",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning)));
        }

        // ── 5. Default speaker ID ──────────────────────────────────
        int speakerId = config.DefaultSpeakerId ?? 13;

        // ── 6. Show main window IMMEDIATELY ─────────────────────────────
        var mainWindow = new MainWindow(client, speakerId, ffmpeg, config);
        mainWindow.Show();

        // ── 7. Background: VOICEVOX connection check (non-blocking) ────────
        if (!config.IsFirstRun)
        {
            _ = Task.Run(async () =>
            {
                var version = await client.CheckConnectionAsync();
                if (version == null)
                {
                    var discovered = await client.DiscoverEngineAsync();
                    if (discovered != null)
                    {
                        config.BeginUpdate();
                        config.EngineUrl = discovered.BaseUrl;
                        config.EndUpdate();
                    }
                }
            });
        }

        // ── 8. Open project from command-line argument ─────────────────────
        if (e.Args.Length > 0 && !string.IsNullOrEmpty(e.Args[0]))
        {
            var filePath = e.Args[0];
            if (System.IO.File.Exists(filePath) &&
                filePath.EndsWith(".icproj", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var project = Models.Project.Load(filePath);
                    mainWindow.LoadProject(project);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        LocalizationService.GetString("App.Error.Startup", ex.Message),
                        "Insight Training Studio",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _voiceVoxClient?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Sanitize exception messages to hide internal paths (e.g. C:\Users\username\...).
    /// </summary>
    internal static string SanitizeErrorMessage(string message)
    {
        // Replace full Windows paths with just the filename
        return Regex.Replace(message, @"[A-Za-z]:\\[^\s""']+\\([^\s""'\\]+)", "$1");
    }
}

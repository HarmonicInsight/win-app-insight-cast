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

/// <summary>
/// Application entry point. Handles first-run setup, engine discovery,
/// FFmpeg initialisation, and main window creation.
/// </summary>
public partial class App : Application
{
    private VoiceVoxClient? _voiceVoxClient;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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

        try
        {
            await StartupAsync(e);
        }
        catch (Exception ex)
        {
            CrashReporter.WriteCrashReport(ex, "StartupAsync");
            MessageBox.Show(
                LocalizationService.GetString("App.Error.Startup", ex.ToString()),
                LocalizationService.GetString("App.Error.Startup.Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
        }
    }

    private async Task StartupAsync(StartupEventArgs e)
    {

        // ── 1. Load configuration ──────────────────────────────────
        var config = new Config();

        // ── 1.5. Initialize localization ─────────────────────────────
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
                // Persist the values chosen in the wizard.
                // The wizard creates its own VoiceVoxClient internally;
                // retrieve the connected client and speaker ID.
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
                // User cancelled -- nothing to do, shut down.
                Shutdown();
                return;
            }
        }

        // ── 3. Create VOICEVOX client ──────────────────────────────
        // Reuse the client from the wizard when available; otherwise create a new one.
        var client = wizardClient ?? new VoiceVoxClient(config.EngineUrl);
        _voiceVoxClient = client;

        if (!config.IsFirstRun)
        {
            // Quick connection check; try auto-discovery on failure.
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
        }

        // ── 4. FFmpeg wrapper ──────────────────────────────────────
        FFmpegWrapper? ffmpeg = null;
        try
        {
            ffmpeg = new FFmpegWrapper();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                LocalizationService.GetString("App.FFmpeg.Error", ex.Message),
                "InsightCast",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        // ── 5. Default speaker ID ──────────────────────────────────
        int speakerId = config.DefaultSpeakerId ?? 13;

        // ── 6. Show main window ─────────────────────────────────────
        var mainWindow = new MainWindow(client, speakerId, ffmpeg, config);
        mainWindow.Show();

        // ── 7. Open project from command-line argument (.icproj file association)
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
                        "InsightCast",
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

namespace InsightCast;

using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using InsightCast.Core;
using InsightCast.Models;
using InsightCast.Services;
using InsightCast.Services.Batch;
using InsightCast.Video;
using InsightCast.Views;
using InsightCast.TTS;
using InsightCast.VoiceVox;
using Syncfusion.SfSkinManager;

/// <summary>
/// Application entry point. Handles first-run setup, engine discovery,
/// FFmpeg initialisation, and main window creation.
/// </summary>
public partial class App : Application
{
    private VoiceVoxClient? _voiceVoxClient;
    private TtsEngineManager? _ttsEngineManager;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ── CLI バッチモード判定 ──
        if (e.Args.Length >= 2 && e.Args[0] == "--batch")
        {
            RunBatchHeadless(e.Args);
            return;
        }

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

        // ── 3. Create VOICEVOX client & TTS engine manager ────────────
        var client = wizardClient ?? new VoiceVoxClient(config.EngineUrl);
        _voiceVoxClient = client;
        var ttsManager = new TtsEngineManager(config, client);
        _ttsEngineManager = ttsManager;

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

        // Prevent auto-shutdown when QuickModeWindow dialog closes
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // ── 6. Check for command-line project file ─────────────────────
        string? cmdLineProject = null;
        if (e.Args.Length > 0 && !string.IsNullOrEmpty(e.Args[0]))
        {
            var filePath = e.Args[0];
            if (System.IO.File.Exists(filePath) &&
                filePath.EndsWith(".icproj", StringComparison.OrdinalIgnoreCase))
                cmdLineProject = filePath;
        }

        // ── 7. Show Quick Mode or Main Window ─────────────────────────────
        if (cmdLineProject == null)
        {
            // No project file — show Quick Mode first
            var quickWindow = new QuickModeWindow(ttsManager, speakerId, ffmpeg, config);
            quickWindow.ShowDialog();

            if (quickWindow.OpenDetailEditor)
            {
                var mainWindow = new MainWindow(ttsManager, speakerId, ffmpeg, config);
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                MainWindow = mainWindow;
                mainWindow.Show();
                BackgroundTtsCheck(ttsManager);

                if (quickWindow.LoadedProject != null)
                    mainWindow.LoadProject(quickWindow.LoadedProject);
            }
            else
            {
                // User closed Quick Mode without opening editor — exit
                Shutdown();
                return;
            }
        }
        else
        {
            // Project file specified — go straight to detail editor
            var mainWindow = new MainWindow(ttsManager, speakerId, ffmpeg, config);
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            MainWindow = mainWindow;
            mainWindow.Show();
            BackgroundTtsCheck(ttsManager);

            try
            {
                var project = Models.Project.Load(cmdLineProject);
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

    private void BackgroundTtsCheck(TtsEngineManager ttsManager)
    {
        // VOICEVOX が選択されている場合、エンジンを自動起動
        if (ttsManager.ActiveEngine.EngineType == TtsEngineType.VoiceVox)
        {
            _ = Task.Run(async () =>
            {
                await ttsManager.EnsureVoiceVoxRunningAsync();
            });
        }
    }

    /// <summary>
    /// CLI ヘッドレスバッチモード。GUI を起動せず、バッチ設定 JSON に従って動画を一括エクスポート。
    /// 使用法: InsightCast.exe --batch config.json [--speaker 13] [--output ./output]
    /// </summary>
    private void RunBatchHeadless(string[] args)
    {
        // コンソール出力を有効化（WPF アプリでも CLI 出力可能に）
        NativeMethods.AttachConsole(NativeMethods.ATTACH_PARENT_PROCESS);

        var config = new Config();
        LocalizationService.Initialize(config.Language);

        // 引数パース
        var batchPath = args[1];
        int speakerId = config.DefaultSpeakerId ?? 13;
        string? outputOverride = null;

        for (int i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--speaker" when i + 1 < args.Length:
                    if (int.TryParse(args[++i], out var sid)) speakerId = sid;
                    break;
                case "--output" when i + 1 < args.Length:
                    outputOverride = args[++i];
                    break;
            }
        }

        Console.WriteLine();
        Console.WriteLine("========================================");
        Console.WriteLine(" InsightCast バッチモード (ヘッドレス)");
        Console.WriteLine("========================================");
        Console.WriteLine($"  設定ファイル: {batchPath}");
        Console.WriteLine($"  話者ID: {speakerId}");

        Task.Run(async () =>
        {
            try
            {
                var client = new VoiceVoxClient(config.EngineUrl);
                var batchTtsManager = new TtsEngineManager(config, client);
                var ffmpeg = new FFmpegWrapper();
                var audioCache = new AudioCache();
                var batchService = new BatchExportService(ffmpeg, batchTtsManager.ActiveEngine, audioCache);

                var batchConfig = batchService.LoadBatchConfig(batchPath);

                if (!string.IsNullOrEmpty(outputOverride))
                    batchConfig.GlobalSettings.OutputDirectory = outputOverride;

                Console.WriteLine($"  プロジェクト数: {batchConfig.Projects.Count}");
                Console.WriteLine("========================================");
                Console.WriteLine();

                // デフォルトスタイルを返す関数
                TextStyle DefaultStyle(Scene _) => TextStyle.PRESET_STYLES.First(s => s.Id == "default");

                var progress = new Progress<BatchProgress>(p =>
                {
                    Console.WriteLine($"  [{p.CurrentProjectIndex + 1}/{p.TotalProjects}] {p.CurrentProjectName} - {p.Phase}: {p.CurrentMessage}");
                });

                var result = await batchService.ExecuteBatchAsync(
                    batchConfig, speakerId, DefaultStyle, progress, CancellationToken.None);

                Console.WriteLine();
                Console.WriteLine("========================================");
                Console.WriteLine($" 完了: {result.SuccessCount}/{result.TotalProjects} 成功");
                Console.WriteLine($" 所要時間: {result.Duration:hh\\:mm\\:ss}");

                if (result.FailedProjects.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine(" 失敗:");
                    foreach (var fail in result.FailedProjects)
                        Console.WriteLine($"   - {fail.ProjectFile}: {fail.ErrorMessage}");
                }
                Console.WriteLine("========================================");

                Dispatcher.Invoke(() => Shutdown(result.FailCount > 0 ? 1 : 0));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"エラー: {ex.Message}");
                CrashReporter.WriteCrashReport(ex, "BatchHeadless");
                Dispatcher.Invoke(() => Shutdown(2));
            }
        });
    }

    private static class NativeMethods
    {
        internal const int ATTACH_PARENT_PROCESS = -1;

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        internal static extern bool AttachConsole(int dwProcessId);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _ttsEngineManager?.Dispose();
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

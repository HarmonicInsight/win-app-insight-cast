using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using InsightCast.Core;
using InsightCast.Models;
using InsightCast.Services;
using InsightCast.Services.Claude;
using InsightCast.Video;
using InsightCast.ViewModels;
using InsightCast.TTS;
using InsightCast.VoiceVox;
using InsightCommon.AI;
using Syncfusion.SfSkinManager;

namespace InsightCast.Views
{
    public partial class MainWindow : Window
    {
        private readonly MainWindowViewModel _vm;
        private readonly Config _config;
        private ClaudeService? _claudeService;
        private readonly TtsEngineManager _ttsManager;
        private readonly int _speakerId;
        private readonly FFmpegWrapper? _ffmpegWrapper;

        public MainWindow(TtsEngineManager ttsManager, int speakerId,
                          FFmpegWrapper? ffmpegWrapper, Config config)
        {
            _config = config;
            _ttsManager = ttsManager;
            _speakerId = speakerId;
            _ffmpegWrapper = ffmpegWrapper;
            InitializeComponent();

            // Hide until theme is applied in Loaded to prevent blue ribbon flash
            Opacity = 0;

            _vm = new MainWindowViewModel(ttsManager, speakerId, ffmpegWrapper, config);
            DataContext = _vm;

            // Wire up ViewModel events for UI-specific operations
            _vm.PlayAudioRequested += OnPlayAudioRequested;
            _vm.StopAudioRequested += OnStopAudioRequested;
            _vm.ThumbnailUpdateRequested += OnThumbnailUpdateRequested;
            _vm.StylePreviewUpdateRequested += OnStylePreviewUpdateRequested;
            _vm.OpenFileRequested += OnOpenFileRequested;
            _vm.PreviewVideoReady += OnPreviewVideoReady;
            _vm.ExitRequested += OnExitRequested;
            _vm.ScreenCaptureRequested += OnScreenCaptureRequested;
            _vm.ScreenRecordRequested += OnScreenRecordRequested;
            _vm.ScenesChanged += OnMainViewModelScenesChanged;
            _vm.TemplateApplied += OnTemplateApplied;

            // Wire up logger to log TextBox
            _vm.Logger.LogReceived += OnLogReceived;

            // Set version label dynamically
            var version = typeof(MainWindow).Assembly.GetName().Version;
            if (version != null)
                VersionLabel.Text = $"v{version.Major}.{version.Minor}.{version.Build}";

            Loaded += (_, _) =>
            {
                // ── Phase 1: Essential UI setup (immediate) ────────────────
                SfSkinManager.SetTheme(MainRibbon, new Theme("Office2019White"));
                MainRibbon.HideBackStage();

                // Set dialog service immediately (needed for UI interactions)
                _vm.SetDialogService(new DialogService(this));

                // Default to Video Generation tab
                MainTabControl.SelectedIndex = 1;

                // Initialize TTS engine selection
                InitializeTtsRadioButtons();

                // Show window after theme rendering is complete
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
                {
                    Opacity = 1;
                });

                // ── Phase 2: Deferred initialization (background/low priority) ──
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, async () =>
                {
                    // Load VOICEVOX speakers (network call)
                    await _vm.InitializeAsync();

                    // UI updates after speaker load
                    PopulateRecentFiles();

                    // Initialize subtitle size combos
                    InitializeSubtitleSizeCombo();

                    // Create shared ClaudeService (used by Planning Tab and Chat Panel)
                    _claudeService = new ClaudeService(_config);

                    // Initialize Planning Tab
                    PlanningTabControl.Initialize(_config, _vm.Project, _claudeService.AiService.Config);
                    PlanningTabControl.ScenesChanged += OnPlanningTabScenesChanged;

                    // Initialize Chat Panel for Planning Tab
                    InitializePlanningChatPanel();

                });
            };
        }

        private void InitializePlanningChatPanel()
        {
            if (_claudeService == null) return;

            var toolExecutor = new Services.Claude.VideoToolExecutor(
                () => _vm.Project.Scenes,
                (index, action) =>
                {
                    if (index >= 0 && index < _vm.Project.Scenes.Count)
                    {
                        action(_vm.Project.Scenes[index]);
                        _vm.RefreshSceneList();
                    }
                },
                Dispatcher,
                new Services.ThumbnailService(),
                count =>
                {
                    for (int i = 0; i < (count ?? 1); i++)
                        _vm.Project.Scenes.Add(new Models.Scene());
                    _vm.RefreshSceneList();
                },
                index =>
                {
                    if (index >= 0 && index < _vm.Project.Scenes.Count)
                    {
                        _vm.Project.Scenes.RemoveAt(index);
                        _vm.RefreshSceneList();
                    }
                },
                (from, to) =>
                {
                    if (from >= 0 && from < _vm.Project.Scenes.Count &&
                        to >= 0 && to < _vm.Project.Scenes.Count)
                    {
                        var scene = _vm.Project.Scenes[from];
                        _vm.Project.Scenes.RemoveAt(from);
                        _vm.Project.Scenes.Insert(to, scene);
                        _vm.RefreshSceneList();
                    }
                },
                () => _claudeService?.AiService.Config?.GetApiKey(AiProviderType.OpenAi)
            );

            var chatVm = new ViewModels.ChatPanelViewModel(
                _claudeService,
                toolExecutor,
                () => _config.Language == "EN" ? "EN" : "JA",
                _config);

            PlanningTabControl.InitializeChatPanel(chatVm);
        }

        private void OnPlanningTabScenesChanged()
        {
            Dispatcher.Invoke(() => _vm.RefreshSceneList());
        }

        private void OnMainViewModelScenesChanged()
        {
            Dispatcher.Invoke(() => PlanningTabControl.RefreshScenes());
        }

        private void OnTemplateApplied()
        {
            Dispatcher.Invoke(() => PlanningTabControl.ReloadThumbnailSettings());
        }

        private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Only handle top-level tab changes
            if (e.Source != MainTabControl) return;

            var isPptxTab = MainTabControl.SelectedIndex == 0;
            var isVideoTab = MainTabControl.SelectedIndex == 1;

            // Refresh scene lists
            if (isPptxTab)
                PlanningTabControl.RefreshScenes();
            else if (isVideoTab)
                _vm.RefreshSceneList();

            // Switch ribbon groups visibility
            UpdateRibbonForTab(isPptxTab, isVideoTab);
        }

        private void UpdateRibbonForTab(bool isPptxTab, bool isVideoTab)
        {
            // PPTX tab groups
            if (RibbonPptxReference != null)
                RibbonPptxReference.Visibility = isPptxTab ? Visibility.Visible : Visibility.Collapsed;
            if (RibbonPptxBatch != null)
                RibbonPptxBatch.Visibility = isPptxTab ? Visibility.Visible : Visibility.Collapsed;
            if (RibbonPptxContent != null)
                RibbonPptxContent.Visibility = isPptxTab ? Visibility.Visible : Visibility.Collapsed;
            if (RibbonPptxHelp != null)
                RibbonPptxHelp.Visibility = isPptxTab ? Visibility.Visible : Visibility.Collapsed;

            // Video tab groups
            if (RibbonVideoScene != null)
                RibbonVideoScene.Visibility = isVideoTab ? Visibility.Visible : Visibility.Collapsed;
            if (RibbonVideoProduction != null)
                RibbonVideoProduction.Visibility = isVideoTab ? Visibility.Visible : Visibility.Collapsed;
            if (RibbonVideoExport != null)
                RibbonVideoExport.Visibility = isVideoTab ? Visibility.Visible : Visibility.Collapsed;
            if (RibbonVideoTemplate != null)
                RibbonVideoTemplate.Visibility = isVideoTab ? Visibility.Visible : Visibility.Collapsed;

            // Syncfusion テーマは Collapsed 状態のコントロールに適用されないため、
            // Visible に変更した直後にテーマを再適用する
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, () =>
            {
                SfSkinManager.SetTheme(MainRibbon, new Theme("Office2019White"));
            });
        }

        /// <summary>
        /// Loads an externally-created project (e.g. from QuickMode) into the editor.
        /// </summary>
        public void LoadProject(Project project)
        {
            _vm.LoadProject(project);
        }

        #region ViewModel Event Handlers (UI-specific)

        private void OnExitRequested() => Close();

        private void OnScreenCaptureRequested()
        {
            // Hide the main window so it doesn't appear in the capture.
            // Using Hide/Show instead of Minimize to preserve other windows' Z-order.
            Hide();

            // Small delay to let the window fully hide
            Dispatcher.InvokeAsync(async () =>
            {
                await System.Threading.Tasks.Task.Delay(300);

                var captureWindow = new ScreenCaptureWindow();
                captureWindow.ShowDialog();

                // Restore main window
                Show();
                Activate();

                // Handle result
                if (captureWindow.RecordingRequested && _vm.FfmpegWrapper != null)
                {
                    HandleScreenRecording(captureWindow.RecordingRegion);
                }
                else if (captureWindow.PinnedToScene && captureWindow.CapturedImagePath != null)
                {
                    _vm.ApplyCapturedImage(captureWindow.CapturedImagePath);
                }
                else if (captureWindow.CapturedImagePath != null)
                {
                    _vm.Logger.Log(Services.LocalizationService.GetString(
                        "VM.Capture.Saved", captureWindow.CapturedImagePath));
                }
                else if (captureWindow.CopiedToClipboard)
                {
                    _vm.Logger.Log(Services.LocalizationService.GetString("VM.Capture.Copied"));
                }
            });
        }

        private void OnPlayAudioRequested(string path, double speed)
        {
            Dispatcher.Invoke(() =>
            {
                AudioPlayer.SpeedRatio = speed;
                AudioPlayer.Source = new Uri(path, UriKind.Absolute);
                AudioPlayer.Play();
            });
        }

        private void OnScreenRecordRequested()
        {
            Hide();

            Dispatcher.InvokeAsync(async () =>
            {
                await System.Threading.Tasks.Task.Delay(300);

                // Use ScreenCaptureWindow for region selection only
                var captureWindow = new ScreenCaptureWindow();
                captureWindow.ShowDialog();

                Show();
                Activate();

                if (captureWindow.RecordingRequested && _vm.FfmpegWrapper != null)
                {
                    HandleScreenRecording(captureWindow.RecordingRegion);
                }
                else if (captureWindow.PinnedToScene && captureWindow.CapturedImagePath != null)
                {
                    // User chose pin instead - still honor it
                    _vm.ApplyCapturedImage(captureWindow.CapturedImagePath);
                }
            });
        }

        private void HandleScreenRecording(System.Drawing.Rectangle region)
        {
            while (true)
            {
                _vm.Logger.Log(Services.LocalizationService.GetString("VM.Recording.Started"));
                var recordWindow = new ScreenRecordingWindow(_vm.FfmpegWrapper!, region);
                recordWindow.ShowDialog();

                if (recordWindow.RecordedVideoPath != null)
                {
                    _vm.Logger.Log($"[Recording] Video path: {recordWindow.RecordedVideoPath}");
                    _vm.Logger.Log($"[Recording] Current scene: {(_vm.CurrentScene != null ? _vm.CurrentScene.Id : "null")}");
                    _vm.ApplyCapturedVideo(recordWindow.RecordedVideoPath);
                    _vm.Logger.Log($"[Recording] After apply - MediaType: {_vm.CurrentScene?.MediaType}, MediaPath: {_vm.CurrentScene?.MediaPath}");
                    break;
                }

                if (recordWindow.RetakeRequested)
                {
                    continue;
                }

                _vm.Logger.Log("[Recording] Cancelled - RecordedVideoPath is null");
                break;
            }
        }

        private void OnStopAudioRequested()
        {
            Dispatcher.Invoke(() =>
            {
                AudioPlayer.Stop();
                AudioPlayer.Close();
                AudioPlayer.Source = null;
            });
        }

        private void OnThumbnailUpdateRequested(string? imagePath)
        {
            Dispatcher.Invoke(() =>
            {
                if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
                {
                    PreviewImage.Source = null;
                    return;
                }

                // For video files, extract a thumbnail frame via FFmpeg
                string displayPath = imagePath;
                if (IsVideoFile(imagePath))
                {
                    var thumbPath = ExtractVideoThumbnail(imagePath);
                    if (thumbPath == null)
                    {
                        PreviewImage.Source = null;
                        return;
                    }
                    displayPath = thumbPath;
                }

                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(displayPath, UriKind.Absolute);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.DecodePixelWidth = 240;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    PreviewImage.Source = bitmap;
                }
                catch
                {
                    PreviewImage.Source = null;
                }
            });
        }

        private static bool IsVideoFile(string path)
        {
            var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            return ext is ".mp4" or ".avi" or ".mov" or ".wmv" or ".mkv";
        }

        private string? ExtractVideoThumbnail(string videoPath)
        {
            if (_vm.FfmpegWrapper == null) return null;
            try
            {
                var thumbPath = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    $"ic_thumb_{videoPath.GetHashCode():X8}.jpg");
                var sceneGen = new Video.SceneGenerator(_vm.FfmpegWrapper);
                if (sceneGen.ExtractThumbnail(videoPath, thumbPath, 0.5))
                    return thumbPath;
            }
            catch { }
            return null;
        }

        private static readonly int[] SubtitleFontSizes = { 12, 14, 16, 18, 20, 22, 24, 28, 32, 36, 40, 44, 48, 52, 56, 60, 64, 72 };
        private readonly string _defaultLabel = LocalizationService.GetString("Common.Default");

        private bool _suppressSubtitleSizeChange;

        private void InitializeSubtitleSizeCombo()
        {
            // Scene list: default subtitle size (project-wide)
            DefaultSubtitleSizeCombo.ItemsSource = SubtitleFontSizes;
            SyncDefaultSubtitleSizeCombo(_vm.DefaultSubtitleFontSize);

            // Scene edit: per-scene override ("デフォルト" + size list)
            var items = new List<object> { _defaultLabel };
            items.AddRange(SubtitleFontSizes.Cast<object>());
            SubtitleSizeCombo.ItemsSource = items;
            SubtitleSizeCombo.SelectedIndex = 0;
        }

        private void DefaultSubtitleSizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSubtitleSizeChange) return;
            if (DefaultSubtitleSizeCombo.SelectedItem is int size)
                _vm.DefaultSubtitleFontSize = size;
        }

        private void SyncDefaultSubtitleSizeCombo(int fontSize)
        {
            _suppressSubtitleSizeChange = true;
            if (Array.IndexOf(SubtitleFontSizes, fontSize) >= 0)
                DefaultSubtitleSizeCombo.SelectedItem = fontSize;
            else
                DefaultSubtitleSizeCombo.SelectedItem = SubtitleFontSizes.OrderBy(s => Math.Abs(s - fontSize)).First();
            _suppressSubtitleSizeChange = false;
        }

        private void SubtitleSizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSubtitleSizeChange) return;
            if (SubtitleSizeCombo.SelectedItem is string) // "デフォルト"
                _vm.SubtitleFontSize = null;
            else if (SubtitleSizeCombo.SelectedItem is int size)
                _vm.SubtitleFontSize = size;
        }

        private void SyncSubtitleSizeCombo(int? sceneFontSize)
        {
            _suppressSubtitleSizeChange = true;
            if (sceneFontSize == null)
            {
                SubtitleSizeCombo.SelectedIndex = 0; // "デフォルト"
            }
            else
            {
                var size = sceneFontSize.Value;
                if (Array.IndexOf(SubtitleFontSizes, size) >= 0)
                    SubtitleSizeCombo.SelectedItem = size;
                else
                    SubtitleSizeCombo.SelectedItem = SubtitleFontSizes.OrderBy(s => Math.Abs(s - size)).First();
            }
            _suppressSubtitleSizeChange = false;
        }

        private void OnStylePreviewUpdateRequested(TextStyle style)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    var textColor = Color.FromRgb(
                        (byte)style.TextColor[0],
                        (byte)style.TextColor[1],
                        (byte)style.TextColor[2]);
                    StylePreviewLabel.Foreground = new SolidColorBrush(textColor);
                    StylePreviewLabel.FontWeight = style.FontBold ? FontWeights.Bold : FontWeights.Normal;

                    SyncSubtitleSizeCombo(_vm.SubtitleFontSize);
                }
                catch
                {
                    StylePreviewLabel.Foreground = Brushes.White;
                }
            });
        }

        private void OnOpenFileRequested(string filePath)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _vm.Logger.LogError(LocalizationService.GetString("VM.File.OpenError"), ex);
            }
        }

        private void OnPreviewVideoReady(string videoPath)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    var dialog = new PreviewPlayerDialog(videoPath);
                    dialog.Owner = this;

                    // Enable font size control with live regeneration
                    var effectiveSize = _vm.EffectiveSubtitleFontSize;
                    dialog.EnableFontSizeControl(effectiveSize, async (newFontSize) =>
                    {
                        return await RegeneratePreviewWithFontSize(newFontSize);
                    });

                    dialog.ShowDialog();

                    // Apply the selected font size back to the scene/project
                    var selectedSize = dialog.SelectedFontSize;
                    if (selectedSize != effectiveSize)
                    {
                        if (_vm.CurrentScene?.SubtitleFontSize != null)
                            _vm.SubtitleFontSize = selectedSize;
                        else
                            _vm.DefaultSubtitleFontSize = selectedSize;
                    }
                }
                catch (Exception ex)
                {
                    _vm.Logger.LogError(LocalizationService.GetString("VM.Preview.OpenError"), ex);
                }
            });
        }

        private async Task<string?> RegeneratePreviewWithFontSize(int fontSize)
        {
            if (_vm.CurrentScene == null || _ffmpegWrapper == null) return null;

            var previewDir = Path.Combine(Path.GetTempPath(), "insightcast_cache", "preview");
            Directory.CreateDirectory(previewDir);
            var previewPath = Path.Combine(previewDir, $"preview_{Guid.NewGuid():N}.mp4");

            var resolution = _vm.GetSelectedResolution();
            int exportSpeakerId = _speakerId;
            if (_vm.SelectedExportSpeakerIndex >= 0 && _vm.SelectedExportSpeakerIndex < _vm.ExportSpeakers.Count)
                exportSpeakerId = _vm.ExportSpeakers[_vm.SelectedExportSpeakerIndex].StyleId;

            // Get style with overridden font size
            var style = _vm.GetStyleForScene(_vm.CurrentScene);
            style = new Models.TextStyle
            {
                Id = style.Id,
                Name = style.Name,
                FontFamily = style.FontFamily,
                FontSize = fontSize,
                FontBold = style.FontBold,
                TextColor = (int[])style.TextColor.Clone(),
                StrokeColor = (int[])style.StrokeColor.Clone(),
                StrokeWidth = style.StrokeWidth,
                BackgroundColor = (int[])style.BackgroundColor.Clone(),
                BackgroundOpacity = style.BackgroundOpacity,
                ShadowEnabled = style.ShadowEnabled,
                ShadowColor = (int[])style.ShadowColor.Clone(),
                ShadowOffset = (int[])style.ShadowOffset.Clone()
            };

            var sceneSnapshot = _vm.Project.Clone().Scenes
                .ElementAtOrDefault(_vm.SelectedSceneIndex) ?? _vm.CurrentScene;

            var progress = new Progress<string>(msg => _vm.Logger.Log(msg));

            var exportService = new Services.ExportService(_ffmpegWrapper, _ttsManager.ActiveEngine, new VoiceVox.AudioCache());
            var success = await Task.Run(() =>
                exportService.GeneratePreview(sceneSnapshot, previewPath, resolution, 30,
                    exportSpeakerId, style, progress, CancellationToken.None,
                    _vm.SubtitleLetterbox));

            return success && File.Exists(previewPath) ? previewPath : null;
        }

        private const int MaxLogLength = 100_000;

        private void OnLogReceived(string message)
        {
            Dispatcher.Invoke(() =>
            {
                if (LogTextBox.Text.Length > MaxLogLength)
                {
                    // Trim to last half when exceeding limit
                    LogTextBox.Text = LogTextBox.Text[^(MaxLogLength / 2)..];
                }
                if (LogTextBox.Text.Length > 0) LogTextBox.AppendText(Environment.NewLine);
                LogTextBox.AppendText(message);
                LogTextBox.ScrollToEnd();
            });
        }

        #endregion

        #region Media Element Events

        private void AudioPlayer_MediaEnded(object sender, RoutedEventArgs e)
        {
            AudioPlayer.Stop();
            AudioPlayer.Source = null;
        }

        #endregion

        #region Preview Image Click Handler

        private void PreviewImage_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var scene = _vm.CurrentScene;
            if (scene == null || string.IsNullOrEmpty(scene.MediaPath)) return;

            // Load high-resolution image
            System.Windows.Media.Imaging.BitmapImage? bitmap = null;
            try
            {
                bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(scene.MediaPath);
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
            }
            catch
            {
                if (PreviewImage.Source == null) return;
                bitmap = null;
            }

            var previewWindow = new Window
            {
                Title = LocalizationService.GetString("Preview.WindowTitle"),
                Width = 900,
                Height = 700,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1a, 0x1a, 0x2e))
            };

            // Container with image and overlays
            var container = new Grid { Margin = new Thickness(20) };

            var image = new Image
            {
                Source = bitmap ?? PreviewImage.Source,
                Stretch = System.Windows.Media.Stretch.Uniform
            };
            container.Children.Add(image);

            // Add overlays
            var overlayCanvas = new Canvas
            {
                IsHitTestVisible = false
            };

            // We need to wait for layout to get actual size
            image.Loaded += (s, args) =>
            {
                var imgWidth = image.ActualWidth;
                var imgHeight = image.ActualHeight;
                var scale = imgHeight / 1920.0;

                // Render text overlays
                foreach (var item in _vm.OverlayItems)
                {
                    var overlay = item.Overlay;

                    var tb = new TextBlock
                    {
                        Text = overlay.Text,
                        FontSize = overlay.FontSize * scale,
                        FontWeight = overlay.FontBold ? FontWeights.Bold : FontWeights.Normal,
                        Foreground = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(
                                (byte)overlay.TextColor[0],
                                (byte)overlay.TextColor[1],
                                (byte)overlay.TextColor[2])),
                        TextAlignment = overlay.Alignment switch
                        {
                            Models.TextAlignment.Left => System.Windows.TextAlignment.Left,
                            Models.TextAlignment.Right => System.Windows.TextAlignment.Right,
                            _ => System.Windows.TextAlignment.Center
                        }
                    };

                    // Add stroke/shadow effect
                    tb.Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color = System.Windows.Media.Color.FromRgb(
                            (byte)overlay.StrokeColor[0],
                            (byte)overlay.StrokeColor[1],
                            (byte)overlay.StrokeColor[2]),
                        BlurRadius = overlay.StrokeWidth * 2,
                        ShadowDepth = overlay.ShadowEnabled ? 2 : 0,
                        Opacity = 0.9
                    };

                    // Measure text size
                    tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    var textWidth = tb.DesiredSize.Width;

                    var x = (overlay.XPercent / 100.0) * imgWidth;
                    var y = (overlay.YPercent / 100.0) * imgHeight;

                    // Adjust position based on alignment
                    if (overlay.Alignment == Models.TextAlignment.Center)
                        x -= textWidth / 2;
                    else if (overlay.Alignment == Models.TextAlignment.Right)
                        x -= textWidth;

                    Canvas.SetLeft(tb, x);
                    Canvas.SetTop(tb, y);

                    overlayCanvas.Children.Add(tb);
                }

                // Render subtitle (narration text) at bottom
                var narrationText = scene.NarrationText;
                if (!string.IsNullOrWhiteSpace(narrationText))
                {
                    var style = _vm.GetStyleForScene(scene);

                    // Create background border for subtitle
                    var subtitleBorder = new Border
                    {
                        Background = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromArgb(
                                (byte)(style.BackgroundOpacity * 255),
                                (byte)style.BackgroundColor[0],
                                (byte)style.BackgroundColor[1],
                                (byte)style.BackgroundColor[2])),
                        Padding = new Thickness(16 * scale, 8 * scale, 16 * scale, 8 * scale),
                        CornerRadius = new CornerRadius(4 * scale)
                    };

                    var subtitleText = new TextBlock
                    {
                        Text = narrationText,
                        FontFamily = new System.Windows.Media.FontFamily(style.FontFamily),
                        FontSize = style.FontSize * scale,
                        FontWeight = style.FontBold ? FontWeights.Bold : FontWeights.Normal,
                        Foreground = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(
                                (byte)style.TextColor[0],
                                (byte)style.TextColor[1],
                                (byte)style.TextColor[2])),
                        TextAlignment = System.Windows.TextAlignment.Center,
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = imgWidth * 0.9
                    };

                    // Add stroke/shadow effect
                    if (style.ShadowEnabled || style.StrokeWidth > 0)
                    {
                        subtitleText.Effect = new System.Windows.Media.Effects.DropShadowEffect
                        {
                            Color = System.Windows.Media.Color.FromRgb(
                                (byte)style.StrokeColor[0],
                                (byte)style.StrokeColor[1],
                                (byte)style.StrokeColor[2]),
                            BlurRadius = style.StrokeWidth * 2,
                            ShadowDepth = style.ShadowEnabled ? style.ShadowOffset[0] : 0,
                            Opacity = 0.9
                        };
                    }

                    subtitleBorder.Child = subtitleText;

                    // Measure subtitle size
                    subtitleBorder.Measure(new Size(imgWidth, double.PositiveInfinity));
                    var subtitleWidth = subtitleBorder.DesiredSize.Width;
                    var subtitleHeight = subtitleBorder.DesiredSize.Height;

                    // Position at bottom center (5% from bottom)
                    var subX = (imgWidth - subtitleWidth) / 2;
                    var subY = imgHeight * 0.95 - subtitleHeight;

                    Canvas.SetLeft(subtitleBorder, subX);
                    Canvas.SetTop(subtitleBorder, subY);

                    overlayCanvas.Children.Add(subtitleBorder);
                }

                overlayCanvas.Width = imgWidth;
                overlayCanvas.Height = imgHeight;
            };

            container.Children.Add(overlayCanvas);
            previewWindow.Content = container;
            previewWindow.ShowDialog();
        }

        private System.Windows.Media.Brush GetOverlayBrush(string color)
        {
            return color?.ToLower() switch
            {
                "black" => System.Windows.Media.Brushes.Black,
                "red" => System.Windows.Media.Brushes.Red,
                "blue" => System.Windows.Media.Brushes.Blue,
                "yellow" => System.Windows.Media.Brushes.Yellow,
                "green" => System.Windows.Media.Brushes.Green,
                _ => System.Windows.Media.Brushes.White
            };
        }

        private System.Windows.TextAlignment GetTextAlignment(string alignment)
        {
            return alignment?.ToLower() switch
            {
                "left" => System.Windows.TextAlignment.Left,
                "right" => System.Windows.TextAlignment.Right,
                _ => System.Windows.TextAlignment.Center
            };
        }

        #endregion

        #region ApplicationCommand Handlers (delegate to ViewModel)

        private void NewProject_Executed(object sender, ExecutedRoutedEventArgs e)
            => _vm.NewProjectCommand.Execute(null);

        private void OpenProject_Executed(object sender, ExecutedRoutedEventArgs e)
            => _vm.OpenProjectCommand.Execute(null);

        private void SaveProject_Executed(object sender, ExecutedRoutedEventArgs e)
            => _vm.SaveProjectCommand.Execute(null);

        #endregion

        #region Keyboard Shortcuts

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.S && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                _vm.SaveProjectAsCommand.Execute(null);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.T && Keyboard.Modifiers == ModifierKeys.Control)
            {
                _vm.AddSceneCommand.Execute(null);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Delete && Keyboard.Modifiers == ModifierKeys.None
                && Keyboard.FocusedElement is not TextBox)
            {
                _vm.RemoveSceneCommand.Execute(null);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Up && Keyboard.Modifiers == ModifierKeys.Control)
            {
                _vm.MoveSceneUpCommand.Execute(null);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Down && Keyboard.Modifiers == ModifierKeys.Control)
            {
                _vm.MoveSceneDownCommand.Execute(null);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.F1 && Keyboard.Modifiers == ModifierKeys.None)
            {
                _vm.ShowTutorialCommand.Execute(null);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.PrintScreen || (e.Key == Key.F5 && Keyboard.Modifiers == ModifierKeys.Control))
            {
                _vm.ScreenCaptureCommand.Execute(null);
                e.Handled = true;
            }
        }

        #endregion

        #region Custom Title Bar & Window Resize

        private const int ResizeBorderWidth = 6;

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;
            var source = HwndSource.FromHwnd(hwnd);
            source?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_NCHITTEST = 0x0084;
            if (msg == WM_NCHITTEST)
            {
                var result = HitTestEdge(lParam);
                if (result != 0)
                {
                    handled = true;
                    return (IntPtr)result;
                }
            }
            return IntPtr.Zero;
        }

        private int HitTestEdge(IntPtr lParam)
        {
            var screenX = (short)(lParam.ToInt64() & 0xFFFF);
            var screenY = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
            var point = PointFromScreen(new Point(screenX, screenY));
            var w = ActualWidth;
            var h = ActualHeight;
            var left = point.X < ResizeBorderWidth;
            var right = point.X > w - ResizeBorderWidth;
            var top = point.Y < ResizeBorderWidth;
            var bottom = point.Y > h - ResizeBorderWidth;

            if (top && left) return 13;      // HTTOPLEFT
            if (top && right) return 14;     // HTTOPRIGHT
            if (bottom && left) return 16;   // HTBOTTOMLEFT
            if (bottom && right) return 17;  // HTBOTTOMRIGHT
            if (left) return 10;             // HTLEFT
            if (right) return 11;            // HTRIGHT
            if (top) return 12;              // HTTOP
            if (bottom) return 15;           // HTBOTTOM
            return 0;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                // Double-click to maximize/restore
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            }
            else
            {
                // Drag to move window
                if (WindowState == WindowState.Maximized)
                {
                    // Restore before dragging from maximized
                    var point = PointToScreen(e.GetPosition(this));
                    WindowState = WindowState.Normal;
                    Left = point.X - (ActualWidth / 2);
                    Top = point.Y - 20;
                }
                DragMove();
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void LangSwitchButton_Click(object sender, RoutedEventArgs e)
        {
            var newLang = LocalizationService.ToggleLanguage();
            _config.Language = newLang;
            UpdateLanguageRadioButtons();
        }

        private void TextOverlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (_vm.CurrentScene == null) return;

            var mediaPath = _vm.CurrentScene.MediaPath;
            var dlg = new TextOverlayDialog(_vm.CurrentScene.TextOverlays, mediaPath, _vm.DefaultSubtitleFontSize) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                _vm.CurrentScene.TextOverlays.Clear();
                _vm.CurrentScene.TextOverlays.AddRange(dlg.ResultOverlays);
                _vm.RefreshOverlayList();
            }
        }

        // ── PPTX tab ribbon relay handlers ──
        private void RibbonExportPptx_Click(object sender, RoutedEventArgs e) { /* TODO: wire to new prompt-based flow */ }
        private void RibbonAiImages_Click(object sender, RoutedEventArgs e) { /* TODO: wire to new prompt-based flow */ }

        // ── Video tab PPTX export ──
        private async void RibbonVideoExportPptx_Click(object sender, RoutedEventArgs e)
        {
            if (_vm?.Project.Scenes == null || _vm.Project.Scenes.Count == 0) return;

            string projectTitle = _vm.Project.ProjectPath != null
                ? Path.GetFileNameWithoutExtension(_vm.Project.ProjectPath)
                : "presentation";

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PowerPoint|*.pptx",
                FileName = projectTitle + ".pptx",
                Title = LocalizationService.GetString("AiFlow.ExportPptx")
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var scenes = _vm.Project.Scenes.ToList();
                await System.Threading.Tasks.Task.Run(() =>
                    PptxGeneratorService.GenerateFromScenes(scenes, projectTitle, dlg.FileName));
                MessageBox.Show(
                    LocalizationService.GetString("AiFlow.PptxSaved"),
                    LocalizationService.GetString("AiFlow.ExportPptx"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    LocalizationService.GetString("Common.ErrorWithMessage", ex.Message),
                    LocalizationService.GetString("Common.Error"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DetailToggleButton_Click(object sender, RoutedEventArgs e)
        {
            _vm.IsSimpleMode = !_vm.IsSimpleMode;
        }

        private void Window_StateChanged(object? sender, EventArgs e)
        {
            // Update maximize button icon: restore ↔ maximize (Segoe MDL2 Assets glyphs)
            if (MaximizeIcon != null)
            {
                MaximizeIcon.Text = WindowState == WindowState.Maximized
                    ? "\uE923"   // Restore
                    : "\uE922";  // Maximize
                MaximizeButton.ToolTip = WindowState == WindowState.Maximized
                    ? LocalizationService.GetString("Window.Restore")
                    : LocalizationService.GetString("Window.Maximize");
            }
        }

        #endregion

        #region Recent Files

        private void PopulateRecentFiles()
        {
            var files = _vm.RecentFiles;
            BackstageRecentFiles.ItemsSource = files.Select(f =>
            {
                var lastAccessed = DateTime.MinValue;
                try
                {
                    if (File.Exists(f))
                        lastAccessed = File.GetLastWriteTime(f);
                }
                catch { /* ignore */ }
                return new RecentFileInfo
                {
                    FullPath = f,
                    FileName = Path.GetFileName(f),
                    LastAccessed = lastAccessed
                };
            }).ToList();

            // Update language radio buttons
            UpdateLanguageRadioButtons();
        }

        private void UpdateLanguageRadioButtons()
        {
            var currentLang = LocalizationService.CurrentLanguage;
            LangJaRadio.IsChecked = currentLang == "ja";
            LangEnRadio.IsChecked = currentLang == "en";
        }

        #endregion

        #region Drag & Drop

        private void Window_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
                DropOverlay.Visibility = Visibility.Visible;
            }
            else
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
            }
        }

        protected override void OnDragLeave(DragEventArgs e)
        {
            base.OnDragLeave(e);
            // Only hide if the mouse has left the window entirely
            var pos = e.GetPosition(this);
            if (pos.X <= 0 || pos.Y <= 0 || pos.X >= ActualWidth || pos.Y >= ActualHeight)
                DropOverlay.Visibility = Visibility.Collapsed;
        }

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            DropOverlay.Visibility = Visibility.Collapsed;

            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files == null || files.Length == 0) return;

            var docExtensions = new[] { ".pptx", ".docx", ".xlsx", ".pdf" };
            var pptxFiles = files.Where(f => docExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())).ToArray();
            var mediaFiles = files.Where(f =>
            {
                var ext = Path.GetExtension(f).ToLowerInvariant();
                return ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif"
                    or ".mp4" or ".avi" or ".mov" or ".wmv" or ".mkv";
            }).ToArray();

            foreach (var pptx in pptxFiles)
            {
                await _vm.ImportPptxFromPathAsync(pptx);
            }

            if (mediaFiles.Length > 0)
            {
                _vm.AddMediaFilesAsScenes(mediaFiles);
            }
        }

        #endregion

        #region Window Lifecycle

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_vm.CanClose())
            {
                e.Cancel = true;
                return;
            }
            OnStopAudioRequested();

            // Unsubscribe event handlers to prevent memory leaks
            _vm.PlayAudioRequested -= OnPlayAudioRequested;
            _vm.StopAudioRequested -= OnStopAudioRequested;
            _vm.ThumbnailUpdateRequested -= OnThumbnailUpdateRequested;
            _vm.StylePreviewUpdateRequested -= OnStylePreviewUpdateRequested;
            _vm.OpenFileRequested -= OnOpenFileRequested;
            _vm.PreviewVideoReady -= OnPreviewVideoReady;
            _vm.ExitRequested -= OnExitRequested;
            _vm.ScreenCaptureRequested -= OnScreenCaptureRequested;
            _vm.ScreenRecordRequested -= OnScreenRecordRequested;
            _vm.ScenesChanged -= OnMainViewModelScenesChanged;
            _vm.Logger.LogReceived -= OnLogReceived;
            PlanningTabControl.ScenesChanged -= OnPlanningTabScenesChanged;
        }

        #endregion

        #region Syncfusion Backstage Event Handlers

        private void BackStageNew_Click(object sender, RoutedEventArgs e)
        {
            MainRibbon.HideBackStage();
            _vm.NewProjectCommand.Execute(null);
        }

        private void BackStageOpen_Click(object sender, RoutedEventArgs e)
        {
            MainRibbon.HideBackStage();
            _vm.OpenProjectCommand.Execute(null);
        }

        private void BackStageSave_Click(object sender, RoutedEventArgs e)
        {
            MainRibbon.HideBackStage();
            _vm.SaveProjectCommand.Execute(null);
        }

        private void BackStageSaveAs_Click(object sender, RoutedEventArgs e)
        {
            MainRibbon.HideBackStage();
            _vm.SaveProjectAsCommand.Execute(null);
        }

        private void BackStageRecentFile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string path)
            {
                MainRibbon.HideBackStage();
                _vm.OpenRecentFileCommand.Execute(path);
            }
        }

        private void BackStageImportPptx_Click(object sender, RoutedEventArgs e)
        {
            MainRibbon.HideBackStage();
            _vm.ImportPptxCommand.Execute(null);
        }

        private void BackStageImportJson_Click(object sender, RoutedEventArgs e)
        {
            MainRibbon.HideBackStage();
            _vm.ImportJsonCommand.Execute(null);
        }

        private void BackStageBatchExport_Click(object sender, RoutedEventArgs e)
        {
            MainRibbon.HideBackStage();
            _vm.BatchExportCommand.Execute(null);
        }

        private void BackStageLanguageRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton radio && radio.Tag is string lang)
            {
                LocalizationService.SetLanguage(lang);
                _config.Language = lang;
            }
        }

        private bool _isSwitchingTtsEngine;

        private async void BackStageTtsRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (_isSwitchingTtsEngine) return;
            if (sender is RadioButton radio && radio.Tag is string tag)
            {
                if (Enum.TryParse<TTS.TtsEngineType>(tag, out var engineType))
                {
                    _isSwitchingTtsEngine = true;
                    try
                    {
                        _ttsManager.SwitchEngine(engineType);
                        await _vm.OnTtsEngineChanged();
                        UpdateTtsConstraints();
                    }
                    finally
                    {
                        _isSwitchingTtsEngine = false;
                    }
                }
            }
        }

        private void UpdateTtsConstraints()
        {
            if (TtsConstraintsText == null) return;
            var constraints = _ttsManager.ActiveEngine.Constraints;
            TtsConstraintsText.Text = string.Join("\n", constraints.Select(c => $"- {c}"));
        }

        private void InitializeTtsRadioButtons()
        {
            var current = _ttsManager.ActiveEngine.EngineType;
            switch (current)
            {
                case TTS.TtsEngineType.EdgeNeural:
                    TtsEdgeRadio.IsChecked = true;
                    break;
                case TTS.TtsEngineType.VoiceVox:
                    TtsVoiceVoxRadio.IsChecked = true;
                    break;
                case TTS.TtsEngineType.WindowsOneCore:
                    TtsWindowsRadio.IsChecked = true;
                    break;
            }
            UpdateTtsConstraints();
        }

        private void BackStageQuickMode_Click(object sender, RoutedEventArgs e)
        {
            MainRibbon.HideBackStage();
            var quickWindow = new QuickModeWindow(_ttsManager, _speakerId, _ffmpegWrapper, _config)
            {
                Owner = this
            };
            quickWindow.ShowDialog();
        }

        private void BackStageLicense_Click(object sender, RoutedEventArgs e)
        {
            // InsightCommon 共通ライセンスダイアログを使用（隠しコマンド対応）
            _vm.ShowLicenseManagerCommand.Execute(null);
        }

        private void BackStageExit_Click(object sender, RoutedEventArgs e)
        {
            MainRibbon.HideBackStage();
            _vm.ExitCommand.Execute(null);
        }

        #endregion

        #region PPTX Ribbon Handlers

        private void RibbonRefAddFile_Click(object sender, RoutedEventArgs e)
        {
            PlanningTabControl.RibbonAddFile();
        }

        private void RibbonRefAddFolder_Click(object sender, RoutedEventArgs e)
        {
            PlanningTabControl.RibbonAddFolder();
        }

        private void RibbonRefClearAll_Click(object sender, RoutedEventArgs e)
        {
            PlanningTabControl.RibbonClearAllReferences();
        }

        private void RibbonBatchCreate_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Batch template creation
        }

        private void RibbonTemplateLib_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Template library
        }

        private void RibbonPromptLib_Click(object sender, RoutedEventArgs e)
        {
            PlanningTabControl.RibbonFocusPromptTree();
        }

        private void RibbonSolution_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Solutions
        }

        private void RibbonHelp_Click(object sender, RoutedEventArgs e)
        {
            var helpWindow = new HelpWindow { Owner = this };
            helpWindow.ShowDialog();
        }

        #endregion
    }

    /// <summary>Helper class for recent file display</summary>
    public class RecentFileInfo
    {
        public string FullPath { get; set; } = "";
        public string FileName { get; set; } = "";
        public DateTime LastAccessed { get; set; }
        public string LastAccessedDisplay => LastAccessed == DateTime.MinValue
            ? ""
            : LastAccessed.ToString("yyyy/MM/dd HH:mm");
    }
}

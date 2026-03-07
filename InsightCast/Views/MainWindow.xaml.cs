using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
using InsightCast.VoiceVox;
using Syncfusion.SfSkinManager;

namespace InsightCast.Views
{
    public partial class MainWindow : Window
    {
        private readonly MainWindowViewModel _vm;
        private readonly Config _config;
        private ChatPanelViewModel? _chatVm;
        private ClaudeService? _claudeService;
        private double _lastAiPanelWidth = 420;
        private readonly VoiceVoxClient _voiceVoxClient;
        private readonly int _speakerId;
        private readonly FFmpegWrapper? _ffmpegWrapper;

        public MainWindow(VoiceVoxClient voiceVoxClient, int speakerId,
                          FFmpegWrapper? ffmpegWrapper, Config config)
        {
            _config = config;
            _voiceVoxClient = voiceVoxClient;
            _speakerId = speakerId;
            _ffmpegWrapper = ffmpegWrapper;
            InitializeComponent();

            // Hide until theme is applied in Loaded to prevent blue ribbon flash
            Opacity = 0;

            _vm = new MainWindowViewModel(voiceVoxClient, speakerId, ffmpegWrapper, config);
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
                    PlanningTabControl.Initialize(_config, _vm.Project, _claudeService);
                    PlanningTabControl.ScenesChanged += OnPlanningTabScenesChanged;
                    PlanningTabControl.TitleCreatorPopOutRequested += OpenTitleCreatorDialog;

                    // Initialize AI Assistant panel (heavy)
                    InitializeChatPanel();
                });
            };
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
            if (RibbonPptxExport != null)
                RibbonPptxExport.Visibility = isPptxTab ? Visibility.Visible : Visibility.Collapsed;
            if (RibbonPptxData != null)
                RibbonPptxData.Visibility = isPptxTab ? Visibility.Visible : Visibility.Collapsed;
            if (RibbonPptxTools != null)
                RibbonPptxTools.Visibility = isPptxTab ? Visibility.Visible : Visibility.Collapsed;

            // Video tab groups
            if (RibbonVideoScene != null)
                RibbonVideoScene.Visibility = isVideoTab ? Visibility.Visible : Visibility.Collapsed;
            if (RibbonVideoProduction != null)
                RibbonVideoProduction.Visibility = isVideoTab ? Visibility.Visible : Visibility.Collapsed;
            if (RibbonVideoExport != null)
                RibbonVideoExport.Visibility = isVideoTab ? Visibility.Visible : Visibility.Collapsed;
            if (RibbonVideoTemplate != null)
                RibbonVideoTemplate.Visibility = isVideoTab ? Visibility.Visible : Visibility.Collapsed;
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

        private static readonly int[] SubtitleFontSizes = { 24, 28, 32, 36, 40, 44, 48, 52, 56, 60, 64, 72 };
        private readonly string _defaultLabel = LocalizationService.GetString("Common.Default");

        private void InitializeSubtitleSizeCombo()
        {
            // Scene-level combo: "デフォルト" + size list
            var items = new List<object> { _defaultLabel };
            items.AddRange(SubtitleFontSizes.Cast<object>());
            SubtitleSizeCombo.ItemsSource = items;
            SubtitleSizeCombo.SelectedIndex = 0; // Default
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

        private bool _suppressSubtitleSizeChange;

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

                    // Sync subtitle size combo with per-scene override
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
                    dialog.ShowDialog();
                }
                catch (Exception ex)
                {
                    _vm.Logger.LogError(LocalizationService.GetString("VM.Preview.OpenError"), ex);
                }
            });
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
            _chatVm?.RefreshForLanguageChange();
            UpdateLanguageRadioButtons();
        }

        private void TitleCreatorButton_Click(object sender, RoutedEventArgs e)
        {
            OpenTitleCreatorDialog();
        }

        private void TextOverlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (_vm.CurrentScene == null) return;

            var mediaPath = _vm.CurrentScene.MediaPath;
            var dlg = new TextOverlayDialog(_vm.CurrentScene.TextOverlays, mediaPath) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                _vm.CurrentScene.TextOverlays.Clear();
                _vm.CurrentScene.TextOverlays.AddRange(dlg.ResultOverlays);
                _vm.RefreshOverlayList();
            }
        }

        // ── PPTX tab ribbon relay handlers ──
        private void RibbonExportPptx_Click(object sender, RoutedEventArgs e) => PlanningTabControl.ExecuteExportPptx();
        private void RibbonExportJson_Click(object sender, RoutedEventArgs e) => PlanningTabControl.ExecuteExportJson();
        private void RibbonImportJson_Click(object sender, RoutedEventArgs e) => PlanningTabControl.ExecuteImportJson();
        private void RibbonAiImages_Click(object sender, RoutedEventArgs e) => PlanningTabControl.ExecuteAiGenerateImages();
        private void RibbonThumbnail_Click(object sender, RoutedEventArgs e) => PlanningTabControl.ExecuteOpenThumbnailCreator();

        private void AIPromptExecute_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new AIPromptExecuteDialog { Owner = this };
            if (dialog.ShowDialog() == true && dialog.SelectedPreset != null)
            {
                if (_chatVm != null)
                {
                    var preset = dialog.SelectedPreset;
                    var lang = Services.LocalizationService.CurrentLanguage;

                    // Set the tool-enabled prompt
                    _chatVm.AiInput = preset.GetPrompt(lang);

                    // Open AI panel if closed
                    if (!_chatVm.IsChatOpen)
                    {
                        _chatVm.IsChatOpen = true;
                        AiPanelColumn.MinWidth = 280;
                        AiPanelColumn.Width = new GridLength(_lastAiPanelWidth, GridUnitType.Pixel);
                    }

                    // Execute the prompt via Claude API with tools
                    if (_chatVm.ExecutePromptCommand.CanExecute(null))
                    {
                        _chatVm.ExecutePromptCommand.Execute(null);
                    }
                }
            }
        }

        private void OpenTitleCreatorDialog()
        {
            var viewModel = PlanningTabControl.ViewModel;
            if (viewModel == null)
            {
                MessageBox.Show("企画タブが初期化されていません。", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dialog = new TitleCreatorDialog { Owner = this };
            dialog.SetViewModel(viewModel);
            dialog.AddToSceneRequested += (imagePath) =>
            {
                _vm.ApplyCapturedImage(imagePath);
                _vm.Logger.Log($"タイトル画像をシーンに追加しました: {imagePath}");
            };
            dialog.ShowDialog();
        }

        private AiAssistantWindow? _aiAssistantWindow;

        private void InitializeChatPanel()
        {
            var thumbnailService = new ThumbnailService();
            var toolExecutor = new VideoToolExecutor(
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
                thumbnailService,
                addScene: (index) => Dispatcher.Invoke(() =>
                {
                    _vm.Project.AddScene(index);
                    _vm.NotifyScenesChanged();
                }),
                removeScene: (index) => Dispatcher.Invoke(() =>
                {
                    _vm.Project.RemoveScene(index);
                    _vm.NotifyScenesChanged();
                }),
                moveScene: (from, to) => Dispatcher.Invoke(() =>
                {
                    _vm.Project.MoveScene(from, to);
                    _vm.NotifyScenesChanged();
                }),
                getOpenAIApiKey: () => _config.OpenAIApiKey ?? "");

            _chatVm = new ChatPanelViewModel(
                _claudeService!,
                toolExecutor,
                () => LocalizationService.CurrentLanguage,
                _config);

            ChatPanel.DataContext = _chatVm;
            ChatPanel.PopOutRequested += OnPopOutAiAssistant;
            ChatPanel.CloseRequested += OnCloseChatPanel;

            // Open AI panel by default
            _chatVm.IsChatOpen = true;
            AiPanelColumn.MinWidth = 280;
            AiPanelColumn.Width = new GridLength(_lastAiPanelWidth, GridUnitType.Pixel);
        }

        private void OnPopOutAiAssistant()
        {
            if (_chatVm == null) return;

            // If already popped out, bring existing window to front
            if (_aiAssistantWindow != null)
            {
                _aiAssistantWindow.Activate();
                return;
            }

            _chatVm.IsPoppedOut = true;
            _aiAssistantWindow = new AiAssistantWindow { Owner = this };
            _aiAssistantWindow.SetViewModel(_chatVm);
            _aiAssistantWindow.Closed += (_, _) =>
            {
                _aiAssistantWindow = null;
                if (_chatVm != null)
                    _chatVm.IsPoppedOut = false;
            };
            _aiAssistantWindow.Show();
        }

        private void AiToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (_chatVm == null) return;

            // Toggle button is now mainly for opening
            if (!_chatVm.IsChatOpen)
            {
                _chatVm.IsChatOpen = true;
                AiPanelColumn.MinWidth = 280;
                AiPanelColumn.Width = new GridLength(_lastAiPanelWidth, GridUnitType.Pixel);
            }
            else
            {
                // Can also close by toggle button
                CloseChatPanel();
            }
        }

        private void OnCloseChatPanel()
        {
            CloseChatPanel();
        }

        private void CloseChatPanel()
        {
            if (_chatVm == null) return;

            if (AiPanelColumn.ActualWidth > 0)
                _lastAiPanelWidth = AiPanelColumn.ActualWidth;

            _chatVm.IsChatOpen = false;
            AiPanelColumn.MinWidth = 0;
            AiPanelColumn.Width = new GridLength(0);
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
            PlanningTabControl.TitleCreatorPopOutRequested -= OpenTitleCreatorDialog;
            ChatPanel.PopOutRequested -= OnPopOutAiAssistant;
            ChatPanel.CloseRequested -= OnCloseChatPanel;

            // Close popout window if open
            _aiAssistantWindow?.Close();
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
                _chatVm?.RefreshForLanguageChange();
            }
        }

        private void BackStageQuickMode_Click(object sender, RoutedEventArgs e)
        {
            MainRibbon.HideBackStage();
            var quickWindow = new QuickModeWindow(_voiceVoxClient, _speakerId, _ffmpegWrapper, _config)
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

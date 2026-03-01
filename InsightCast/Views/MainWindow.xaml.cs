using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Ribbon;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using InsightCast.Core;
using InsightCast.Models;
using InsightCast.Services;
using InsightCast.Services.Claude;
using InsightCast.Video;
using InsightCast.ViewModels;
using InsightCast.VoiceVox;

namespace InsightCast.Views
{
    public partial class MainWindow : Window
    {
        private readonly MainWindowViewModel _vm;
        private readonly Config _config;
        private ChatPanelViewModel? _chatVm;
        private double _lastAiPanelWidth = 420;

        public MainWindow(VoiceVoxClient voiceVoxClient, int speakerId,
                          FFmpegWrapper? ffmpegWrapper, Config config)
        {
            _config = config;
            InitializeComponent();

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
            _vm.ScenesChanged += OnMainViewModelScenesChanged;

            // Wire up logger to log TextBox
            _vm.Logger.LogReceived += OnLogReceived;

            // Set version label dynamically
            var version = typeof(MainWindow).Assembly.GetName().Version;
            if (version != null)
                VersionLabel.Text = $"v{version.Major}.{version.Minor}.{version.Build}";

            Loaded += async (_, _) =>
            {
                _vm.SetDialogService(new DialogService(this));
                await _vm.InitializeAsync();
                PopulateRecentFiles();

                // Initialize Planning Tab
                PlanningTabControl.Initialize(_config, _vm.Project);

                // Sync scene lists when planning tab modifies scenes
                PlanningTabControl.ScenesChanged += OnPlanningTabScenesChanged;

                // Initialize AI Assistant panel
                InitializeChatPanel();

                // Default to Video Generation tab (index 1)
                MainTabControl.SelectedIndex = 1;
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

        private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Only handle top-level tab changes
            if (e.Source != MainTabControl) return;

            // Refresh both scene lists when switching tabs to ensure they're in sync
            if (MainTabControl.SelectedIndex == 0)
            {
                // Switching to Planning tab - refresh planning scenes
                PlanningTabControl.RefreshScenes();
            }
            else if (MainTabControl.SelectedIndex == 1)
            {
                // Switching to Video Generation tab - refresh scene list
                _vm.RefreshSceneList();
            }
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

        private void OnPlayAudioRequested(string path, double speed)
        {
            Dispatcher.Invoke(() =>
            {
                AudioPlayer.SpeedRatio = speed;
                AudioPlayer.Source = new Uri(path, UriKind.Absolute);
                AudioPlayer.Play();
            });
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
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
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
            }
        }

        #endregion

        #region Custom Title Bar

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
        }

        private AiAssistantWindow? _aiAssistantWindow;

        private void InitializeChatPanel()
        {
            // 初回起動時にデフォルトのマイプロンプトを登録
            PromptLibraryService.SeedDefaultPrompts();

            var claudeService = new ClaudeService(_config);
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
                claudeService,
                toolExecutor,
                () => LocalizationService.CurrentLanguage,
                _config,
                _config.ClaudeModelIndex);

            ChatPanel.DataContext = _chatVm;
            ChatPanel.PopOutRequested += OnPopOutAiAssistant;

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

            _chatVm.IsChatOpen = !_chatVm.IsChatOpen;

            if (_chatVm.IsChatOpen)
            {
                AiPanelColumn.MinWidth = 280;
                AiPanelColumn.Width = new GridLength(_lastAiPanelWidth, GridUnitType.Pixel);
            }
            else
            {
                if (AiPanelColumn.ActualWidth > 0)
                    _lastAiPanelWidth = AiPanelColumn.ActualWidth;
                AiPanelColumn.MinWidth = 0;
                AiPanelColumn.Width = new GridLength(0);
            }
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
            RecentFilesMenu.Items.Clear();
            var files = _vm.RecentFiles;
            if (files.Count == 0)
            {
                var empty = new RibbonApplicationMenuItem
                {
                    Header = LocalizationService.GetString("Common.None"),
                    IsEnabled = false
                };
                RecentFilesMenu.Items.Add(empty);
                return;
            }

            foreach (var file in files)
            {
                var item = new RibbonApplicationMenuItem
                {
                    Header = Path.GetFileName(file),
                    ToolTip = file,
                    CommandParameter = file,
                    Command = _vm.OpenRecentFileCommand
                };
                RecentFilesMenu.Items.Add(item);
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
            _vm.ScenesChanged -= OnMainViewModelScenesChanged;
            _vm.Logger.LogReceived -= OnLogReceived;
            PlanningTabControl.ScenesChanged -= OnPlanningTabScenesChanged;
            ChatPanel.PopOutRequested -= OnPopOutAiAssistant;

            // Close popout window if open
            _aiAssistantWindow?.Close();
        }

        #endregion
    }
}

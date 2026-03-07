using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using InsightCast.Core;
using InsightCast.Models;
using InsightCast.Services;
using InsightCast.Utils;
using InsightCast.Video;
using InsightCast.VoiceVox;

namespace InsightCast.Views
{
    public partial class QuickModeWindow : Window
    {
        private readonly VoiceVoxClient _voiceVoxClient;
        private readonly int _defaultSpeakerId;
        private readonly FFmpegWrapper? _ffmpegWrapper;
        private readonly Config _config;

        private string? _pptxPath;
        private List<SlideData>? _slides;
        private bool _dropJustHandled;

        /// <summary>True if the user chose to open the detail editor.</summary>
        public bool OpenDetailEditor { get; private set; }

        /// <summary>The loaded project (for passing to MainWindow).</summary>
        public Project? LoadedProject { get; private set; }

        public QuickModeWindow(VoiceVoxClient client, int speakerId, FFmpegWrapper? ffmpeg, Config config)
        {
            InitializeComponent();
            _voiceVoxClient = client;
            _defaultSpeakerId = speakerId;
            _ffmpegWrapper = ffmpeg;
            _config = config;

            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            await LoadSpeakers();
            RestorePreferences();
        }

        private void RestorePreferences()
        {
            // Speech speed
            var savedSpeed = _config.QuickSpeechSpeed;
            for (int i = 0; i < SpeedCombo.Items.Count; i++)
            {
                if (SpeedCombo.Items[i] is ComboBoxItem item && item.Tag is string tagStr
                    && double.TryParse(tagStr, out double tagVal) && Math.Abs(tagVal - savedSpeed) < 0.01)
                {
                    SpeedCombo.SelectedIndex = i;
                    break;
                }
            }

            // Font size
            var savedFontSize = _config.QuickSubtitleFontSize;
            FontSizeCombo.Text = savedFontSize.ToString();

            // Resolution
            var savedResIndex = _config.QuickResolutionIndex;
            if (savedResIndex >= 0 && savedResIndex < ResolutionCombo.Items.Count)
                ResolutionCombo.SelectedIndex = savedResIndex;
        }

        private void SavePreferences()
        {
            _config.BeginUpdate();

            // Speech speed
            if (SpeedCombo.SelectedItem is ComboBoxItem speedItem && speedItem.Tag is string speedStr
                && double.TryParse(speedStr, out double speed))
                _config.QuickSpeechSpeed = speed;

            // Font size
            if (int.TryParse(FontSizeCombo.Text, out int fontSize) && fontSize > 0)
                _config.QuickSubtitleFontSize = fontSize;

            // Speaker
            _config.QuickSpeakerIndex = SpeakerCombo.SelectedIndex;

            // Resolution
            _config.QuickResolutionIndex = ResolutionCombo.SelectedIndex;

            _config.EndUpdate();
        }

        private async Task LoadSpeakers()
        {
            try
            {
                var speakers = await _voiceVoxClient.GetSpeakersAsync();
                SpeakerCombo.Items.Clear();

                int selectedIndex = 0;
                int index = 0;
                foreach (var speaker in speakers)
                {
                    if (!speaker.TryGetProperty("name", out var nameProp)) continue;
                    var speakerName = VoiceVoxClient.GetLocalizedSpeakerName(nameProp.GetString() ?? "Unknown");
                    if (!speaker.TryGetProperty("styles", out var styles)) continue;

                    foreach (var style in styles.EnumerateArray())
                    {
                        if (!style.TryGetProperty("id", out var idProp)) continue;
                        var styleId = idProp.GetInt32();
                        var styleName = VoiceVoxClient.GetLocalizedStyleName(
                            style.TryGetProperty("name", out var snProp) ? snProp.GetString() ?? "" : "");

                        var item = new ComboBoxItem
                        {
                            Content = $"{speakerName} ({styleName})",
                            Tag = styleId
                        };
                        SpeakerCombo.Items.Add(item);

                        if (styleId == _defaultSpeakerId)
                            selectedIndex = index;
                        index++;
                    }
                }

                if (SpeakerCombo.Items.Count > 0)
                {
                    var savedIdx = _config.QuickSpeakerIndex;
                    SpeakerCombo.SelectedIndex = (savedIdx >= 0 && savedIdx < SpeakerCombo.Items.Count)
                        ? savedIdx : selectedIndex;
                }
            }
            catch
            {
                var item = new ComboBoxItem { Content = "VOICEVOX (default)", Tag = _defaultSpeakerId };
                SpeakerCombo.Items.Add(item);
                SpeakerCombo.SelectedIndex = 0;
            }
        }

        // ── Drag & Drop ──

        private void Window_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            _dropJustHandled = true;
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            var docExtensions = new[] { ".pptx", ".docx", ".xlsx", ".pdf" };
            var docFile = files?.FirstOrDefault(f =>
                docExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));

            if (docFile != null)
                await LoadDocument(docFile);
        }

        private void DropZone_Click(object sender, MouseButtonEventArgs e)
        {
            if (_dropJustHandled)
            {
                _dropJustHandled = false;
                return;
            }

            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = LocalizationService.GetString("Quick.SelectPptx"),
                Filter = Services.DocumentConverterService.GetFileFilter()
            };

            if (dlg.ShowDialog() == true)
                _ = LoadDocument(dlg.FileName);
        }

        private async Task LoadDocument(string path)
        {
            // Convert non-PPTX to PPTX first
            if (Services.DocumentConverterService.NeedsConversion(path))
            {
                var converter = new Services.DocumentConverterService();
                var converted = await Task.Run(() => converter.ConvertToPptx(path));
                if (converted == null)
                {
                    MessageBox.Show(
                        LocalizationService.GetString("DocConvert.Failed", Path.GetExtension(path)),
                        "InsightCast", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                path = converted;
            }
            await LoadPptx(path);
        }

        private async Task LoadPptx(string path)
        {
            try
            {
                _pptxPath = path;

                var outputDir = Path.Combine(
                    Path.GetTempPath(), "insightcast_cache", "pptx_slides",
                    $"quick_{Guid.NewGuid():N}");

                var importer = new PptxImporter((_, _, _) => { });
                _slides = await Task.Run(() => importer.ImportPptx(path, outputDir));

                if (_slides.Count == 0)
                {
                    MessageBox.Show(
                        LocalizationService.GetString("VM.Pptx.NoSlides"),
                        "InsightCast", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Update UI to show loaded state
                DropPrompt.Visibility = Visibility.Collapsed;
                FileLoadedPanel.Visibility = Visibility.Visible;
                LoadedFileName.Text = Path.GetFileName(path);
                LoadedSlideCount.Text = string.Format(
                    LocalizationService.GetString("Quick.SlideCount"), _slides.Count);
                GenerateButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    LocalizationService.GetString("VM.Pptx.Error", ex.Message),
                    "InsightCast", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Generate ──

        private async void GenerateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_slides == null || _slides.Count == 0) return;

            if (_ffmpegWrapper == null || !_ffmpegWrapper.CheckAvailable())
            {
                MessageBox.Show(
                    LocalizationService.GetString("VM.Export.NoFFmpeg"),
                    "InsightCast", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Save preferences for next time
            SavePreferences();

            // Get selected speaker
            int speakerId = _defaultSpeakerId;
            if (SpeakerCombo.SelectedItem is ComboBoxItem item && item.Tag is int sid)
                speakerId = sid;

            // Get resolution
            string resolution = "1920x1080";
            if (ResolutionCombo.SelectedItem is ComboBoxItem resItem && resItem.Tag is string res)
                resolution = res;

            // Get speech speed
            double speechSpeed = 1.2;
            if (SpeedCombo.SelectedItem is ComboBoxItem speedItem && speedItem.Tag is string speedStr
                && double.TryParse(speedStr, out double spd))
                speechSpeed = spd;

            // Get font size
            int subtitleFontSize = 28;
            if (int.TryParse(FontSizeCombo.Text, out int fs) && fs > 0)
                subtitleFontSize = fs;

            // Ask for output path
            var saveDlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = LocalizationService.GetString("VM.Export.SaveTitle"),
                Filter = "MP4 (*.mp4)|*.mp4",
                DefaultExt = ".mp4",
                FileName = Path.GetFileNameWithoutExtension(_pptxPath) + ".mp4"
            };

            if (saveDlg.ShowDialog() != true) return;

            // Build project from slides
            var project = new Project { DefaultSubtitleFontSize = subtitleFontSize };
            foreach (var slide in _slides)
            {
                var scene = new Scene
                {
                    NarrationText = slide.Notes,
                    SubtitleText = slide.Notes,
                    SpeechSpeed = speechSpeed
                };
                if (!string.IsNullOrEmpty(slide.ImagePath) && File.Exists(slide.ImagePath))
                {
                    scene.MediaPath = slide.ImagePath;
                    scene.MediaType = MediaType.Image;
                }
                project.Scenes.Add(scene);
            }

            // Disable UI during export
            GenerateButton.IsEnabled = false;
            GenerateButton.Content = LocalizationService.GetString("Quick.Generating");
            DropZone.IsEnabled = false;

            try
            {
                var audioCache = new AudioCache();
                var exportService = new ExportService(_ffmpegWrapper, _voiceVoxClient, audioCache);
                var baseStyle = TextStyle.PRESET_STYLES.First(s => s.Id == "default");
                var defaultStyle = new TextStyle
                {
                    Id = baseStyle.Id, Name = baseStyle.Name,
                    FontFamily = baseStyle.FontFamily, FontSize = subtitleFontSize,
                    FontBold = baseStyle.FontBold, TextColor = baseStyle.TextColor,
                    StrokeColor = baseStyle.StrokeColor, StrokeWidth = baseStyle.StrokeWidth,
                    BackgroundColor = baseStyle.BackgroundColor, BackgroundOpacity = baseStyle.BackgroundOpacity,
                    ShadowEnabled = baseStyle.ShadowEnabled, ShadowColor = baseStyle.ShadowColor,
                    ShadowOffset = baseStyle.ShadowOffset
                };
                TextStyle GetStyle(Scene _) => defaultStyle;

                var progress = new Progress<string>(msg =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (msg.StartsWith("[") && msg.Contains('/'))
                        {
                            var bracket = msg.IndexOf(']');
                            if (bracket > 0)
                            {
                                var parts = msg[1..bracket].Split('/');
                                if (parts.Length == 2
                                    && int.TryParse(parts[0], out int current)
                                    && int.TryParse(parts[1], out int total)
                                    && total > 0)
                                {
                                    var pct = (int)((double)current / total * 100);
                                    GenerateButton.Content = $"{LocalizationService.GetString("Quick.Generating")} {pct}%";
                                }
                            }
                        }
                    });
                });

                var result = await Task.Run(() =>
                    exportService.ExportFull(project, saveDlg.FileName, resolution, 30,
                        speakerId, GetStyle, progress, CancellationToken.None));

                if (result.Success)
                {
                    var openFile = MessageBox.Show(
                        LocalizationService.GetString("Quick.ExportDone"),
                        "InsightCast", MessageBoxButton.YesNo, MessageBoxImage.Information);

                    if (openFile == MessageBoxResult.Yes)
                    {
                        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(saveDlg.FileName) { UseShellExecute = true }); }
                        catch { }
                    }
                }
                else
                {
                    MessageBox.Show(
                        LocalizationService.GetString("VM.Export.Failed", result.ErrorMessage ?? "Unknown error"),
                        "InsightCast", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "InsightCast", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                GenerateButton.IsEnabled = true;
                GenerateButton.Content = LocalizationService.GetString("Quick.Generate");
                DropZone.IsEnabled = true;
            }
        }

        // ── Navigation ──

        private void DetailEditor_Click(object sender, RoutedEventArgs e)
        {
            SavePreferences();

            // Get current settings
            double speechSpeed = 1.2;
            if (SpeedCombo.SelectedItem is ComboBoxItem speedItem && speedItem.Tag is string speedStr
                && double.TryParse(speedStr, out double spd))
                speechSpeed = spd;
            int subtitleFontSize = 28;
            if (int.TryParse(FontSizeCombo.Text, out int fs) && fs > 0)
                subtitleFontSize = fs;

            // If slides are loaded, create a project to pass to MainWindow
            if (_slides != null && _slides.Count > 0)
            {
                var project = new Project { DefaultSubtitleFontSize = subtitleFontSize };
                foreach (var slide in _slides)
                {
                    var scene = new Scene
                    {
                        NarrationText = slide.Notes,
                        SubtitleText = slide.Notes,
                        SpeechSpeed = speechSpeed
                    };
                    if (!string.IsNullOrEmpty(slide.ImagePath) && File.Exists(slide.ImagePath))
                    {
                        scene.MediaPath = slide.ImagePath;
                        scene.MediaType = MediaType.Image;
                    }
                    project.Scenes.Add(scene);
                }
                LoadedProject = project;
            }

            OpenDetailEditor = true;
            Close();
        }

        // ── Window chrome ──

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1) DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using InsightCast.Services;

namespace InsightCast.Views
{
    public partial class PreviewPlayerDialog : Window
    {
        private readonly List<string> _videoFiles = new();
        private int _currentSceneIndex;
        private bool _isPlaying;
        private bool _isSeeking;
        private readonly DispatcherTimer _positionTimer;

        // Font size regeneration support
        private static readonly int[] FontSizes = { 12, 14, 16, 18, 20, 22, 24, 28, 32, 36, 40, 44, 48, 52, 56, 60, 64, 72 };
        private int _currentFontSize;
        private Func<int, Task<string?>>? _regenerateCallback;
        private CancellationTokenSource? _regenerateCts;

        public PreviewPlayerDialog()
        {
            InitializeComponent();

            _positionTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _positionTimer.Tick += PositionTimer_Tick;

            Loaded += PreviewPlayerDialog_Loaded;
        }

        /// <summary>
        /// Creates a PreviewPlayerDialog with a single video file.
        /// </summary>
        public PreviewPlayerDialog(string videoFilePath) : this()
        {
            _videoFiles.Add(videoFilePath);
        }

        /// <summary>
        /// Creates a PreviewPlayerDialog with multiple video files (scenes).
        /// </summary>
        public PreviewPlayerDialog(IEnumerable<string> videoFilePaths) : this()
        {
            _videoFiles.AddRange(videoFilePaths);
        }

        /// <summary>
        /// Enables font size adjustment in the preview dialog.
        /// The callback receives the new font size and should return the new video path, or null on failure.
        /// </summary>
        public void EnableFontSizeControl(int currentFontSize, Func<int, Task<string?>> regenerateCallback)
        {
            _currentFontSize = currentFontSize;
            _regenerateCallback = regenerateCallback;

            _suppressFontSizeChange = true;
            FontSizeCombo.ItemsSource = FontSizes;
            FontSizeCombo.SelectedItem = currentFontSize;
            if (FontSizeCombo.SelectedItem == null)
            {
                // Find closest
                int closest = FontSizes[0];
                foreach (var s in FontSizes)
                    if (Math.Abs(s - currentFontSize) < Math.Abs(closest - currentFontSize))
                        closest = s;
                FontSizeCombo.SelectedItem = closest;
            }
            _suppressFontSizeChange = false;
            FontSizePanel.Visibility = Visibility.Visible;
        }

        /// <summary>Gets the font size selected by the user (for applying back to the scene).</summary>
        public int SelectedFontSize => _currentFontSize;

        private void PreviewPlayerDialog_Loaded(object sender, RoutedEventArgs e)
        {
            MediaPlayer.Volume = VolumeSlider.Value;

            if (_videoFiles.Count > 0)
            {
                LoadScene(0);
            }
            else
            {
                SceneLabel.Text = LocalizationService.GetString("Preview.Scene", 0, 0);
                PlayPauseBtn.IsEnabled = false;
            }
        }

        // ── Scene Management ────────────────────────────────────────────

        private void LoadScene(int index)
        {
            if (index < 0 || index >= _videoFiles.Count) return;

            _currentSceneIndex = index;
            StopPlayback();

            try
            {
                MediaPlayer.Source = new Uri(_videoFiles[index], UriKind.Absolute);
                UpdateSceneLabel();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    LocalizationService.GetString("Preview.LoadError", ex.Message),
                    LocalizationService.GetString("Common.Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void UpdateSceneLabel()
        {
            SceneLabel.Text = LocalizationService.GetString("Preview.Scene", _currentSceneIndex + 1, _videoFiles.Count);
        }

        // ── Playback Control ────────────────────────────────────────────

        private void Play()
        {
            MediaPlayer.Play();
            _isPlaying = true;
            PlayPauseBtn.Content = LocalizationService.GetString("Preview.Pause");
            _positionTimer.Start();
        }

        private void Pause()
        {
            MediaPlayer.Pause();
            _isPlaying = false;
            PlayPauseBtn.Content = LocalizationService.GetString("Preview.Play");
            _positionTimer.Stop();
        }

        private void StopPlayback()
        {
            MediaPlayer.Stop();
            _isPlaying = false;
            PlayPauseBtn.Content = LocalizationService.GetString("Preview.Play");
            _positionTimer.Stop();
            CurrentTimeLabel.Text = "00:00";
            SeekSlider.Value = 0;
        }

        // ── Event Handlers: Media ───────────────────────────────────────

        private TimeSpan? _pendingSeek;
        private bool _autoPlayAfterSeek;

        private void MediaPlayer_MediaOpened(object sender, RoutedEventArgs e)
        {
            if (MediaPlayer.NaturalDuration.HasTimeSpan)
            {
                var duration = MediaPlayer.NaturalDuration.TimeSpan;
                SeekSlider.Maximum = duration.TotalSeconds;
                TotalTimeLabel.Text = FormatTime(duration);
            }
            else
            {
                SeekSlider.Maximum = 100;
                TotalTimeLabel.Text = "--:--";
            }

            // Handle pending seek from regeneration
            if (_pendingSeek.HasValue)
            {
                var seekTo = _pendingSeek.Value;
                _pendingSeek = null;
                if (MediaPlayer.NaturalDuration.HasTimeSpan && seekTo <= MediaPlayer.NaturalDuration.TimeSpan)
                {
                    MediaPlayer.Position = seekTo;
                    SeekSlider.Value = seekTo.TotalSeconds;
                    CurrentTimeLabel.Text = FormatTime(seekTo);
                }
                if (_autoPlayAfterSeek)
                {
                    Play();
                    _autoPlayAfterSeek = false;
                    return;
                }
            }

            SeekSlider.Value = 0;
            CurrentTimeLabel.Text = "00:00";

            // Auto-play when media is loaded
            Play();
        }

        private void MediaPlayer_MediaEnded(object sender, RoutedEventArgs e)
        {
            _positionTimer.Stop();
            _isPlaying = false;
            PlayPauseBtn.Content = LocalizationService.GetString("Preview.Play");

            // Auto-advance to next scene
            if (_currentSceneIndex < _videoFiles.Count - 1)
            {
                LoadScene(_currentSceneIndex + 1);
                Play();
            }
            else
            {
                // Reset to beginning of current scene
                SeekSlider.Value = 0;
                CurrentTimeLabel.Text = "00:00";
            }
        }

        private void MediaPlayer_MediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            _positionTimer.Stop();
            _isPlaying = false;
            PlayPauseBtn.Content = LocalizationService.GetString("Preview.Play");

            var errorMsg = e.ErrorException?.Message ?? "";
            MessageBox.Show(
                LocalizationService.GetString("Preview.PlayError", errorMsg),
                LocalizationService.GetString("Preview.PlayError.Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        // ── Event Handlers: Transport Buttons ───────────────────────────

        private void PlayPauseBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_isPlaying)
            {
                Pause();
            }
            else
            {
                Play();
            }
        }

        private void Rewind10Btn_Click(object sender, RoutedEventArgs e)
        {
            if (MediaPlayer.NaturalDuration.HasTimeSpan)
            {
                var newPos = MediaPlayer.Position - TimeSpan.FromSeconds(10);
                if (newPos < TimeSpan.Zero) newPos = TimeSpan.Zero;
                MediaPlayer.Position = newPos;
                UpdateSeekPosition();
            }
        }

        private void Forward10Btn_Click(object sender, RoutedEventArgs e)
        {
            if (MediaPlayer.NaturalDuration.HasTimeSpan)
            {
                var duration = MediaPlayer.NaturalDuration.TimeSpan;
                var newPos = MediaPlayer.Position + TimeSpan.FromSeconds(10);
                if (newPos > duration) newPos = duration;
                MediaPlayer.Position = newPos;
                UpdateSeekPosition();
            }
        }

        private void PrevSceneBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_currentSceneIndex > 0)
            {
                LoadScene(_currentSceneIndex - 1);
            }
        }

        private void NextSceneBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_currentSceneIndex < _videoFiles.Count - 1)
            {
                LoadScene(_currentSceneIndex + 1);
            }
        }

        // ── Event Handlers: Seek ────────────────────────────────────────

        private void SeekSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _isSeeking = true;
        }

        private void SeekSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            _isSeeking = false;
            if (MediaPlayer.NaturalDuration.HasTimeSpan)
            {
                MediaPlayer.Position = TimeSpan.FromSeconds(SeekSlider.Value);
            }
        }

        private void SeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isSeeking)
            {
                CurrentTimeLabel.Text = FormatTime(TimeSpan.FromSeconds(SeekSlider.Value));
            }
        }

        // ── Event Handlers: Volume & Speed ──────────────────────────────

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (MediaPlayer != null)
                MediaPlayer.Volume = VolumeSlider.Value;
        }

        private void SpeedComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SpeedComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tagStr)
            {
                if (double.TryParse(tagStr, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double speed))
                {
                    MediaPlayer.SpeedRatio = speed;
                }
            }
        }

        // ── Font Size ───────────────────────────────────────────────────

        private bool _suppressFontSizeChange;

        private async void FontSizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressFontSizeChange) return;
            if (FontSizeCombo.SelectedItem is not int newSize) return;
            if (newSize == _currentFontSize) return;
            if (_regenerateCallback == null) return;

            _currentFontSize = newSize;

            // Cancel any in-progress regeneration
            _regenerateCts?.Cancel();
            _regenerateCts = new CancellationTokenSource();
            var cts = _regenerateCts;

            // Show converting overlay
            var resumePosition = MediaPlayer.Position;
            var wasPlaying = _isPlaying;
            Pause();
            ConvertingOverlay.Visibility = Visibility.Visible;
            FontSizeCombo.IsEnabled = false;

            try
            {
                var newPath = await _regenerateCallback(newSize);

                if (cts.IsCancellationRequested) return;

                if (newPath != null)
                {
                    StopPlayback();
                    _videoFiles.Clear();
                    _videoFiles.Add(newPath);
                    _currentSceneIndex = 0;

                    _pendingSeek = resumePosition;
                    _autoPlayAfterSeek = wasPlaying;
                    MediaPlayer.Source = new Uri(newPath, UriKind.Absolute);
                    UpdateSceneLabel();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Font size regeneration failed: {ex.Message}");
            }
            finally
            {
                ConvertingOverlay.Visibility = Visibility.Collapsed;
                FontSizeCombo.IsEnabled = true;
            }
        }

        // ── Timer ───────────────────────────────────────────────────────

        private void PositionTimer_Tick(object? sender, EventArgs e)
        {
            if (!_isSeeking)
            {
                UpdateSeekPosition();
            }
        }

        private void UpdateSeekPosition()
        {
            if (MediaPlayer.NaturalDuration.HasTimeSpan)
            {
                var pos = MediaPlayer.Position;
                SeekSlider.Value = pos.TotalSeconds;
                CurrentTimeLabel.Text = FormatTime(pos);
            }
        }

        // ── Close ───────────────────────────────────────────────────────

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _regenerateCts?.Cancel();
            _positionTimer.Stop();
            MediaPlayer.Stop();
            MediaPlayer.Source = null;
        }

        // ── Helpers ─────────────────────────────────────────────────────

        private static string FormatTime(TimeSpan time)
        {
            return $"{(int)time.TotalMinutes:D2}:{time.Seconds:D2}";
        }
    }
}

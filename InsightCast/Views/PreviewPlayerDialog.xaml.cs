using System;
using System.Collections.Generic;
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

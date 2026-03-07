using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using InsightCast.Video;

namespace InsightCast.Views
{
    public partial class ScreenRecordingWindow : Window
    {
        private readonly FFmpegWrapper _ffmpeg;
        private readonly Rectangle _region;
        private readonly string _outputPath;
        private Process? _ffmpegProcess;
        private readonly DispatcherTimer _timer;
        private readonly Stopwatch _stopwatch = new();
        private RecordingBorderWindow? _borderWindow;
        private bool _stopped;
        private int _countdown = 3;

        /// <summary>null = cancelled or failed, non-null = user chose to use the video.</summary>
        public string? RecordedVideoPath { get; private set; }

        /// <summary>true = user wants to retake (reopen capture).</summary>
        public bool RetakeRequested { get; private set; }

        public ScreenRecordingWindow(FFmpegWrapper ffmpeg, Rectangle region)
        {
            InitializeComponent();

            _ffmpeg = ffmpeg;
            _region = region;

            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "InsightCast", "Recordings");
            Directory.CreateDirectory(dir);
            _outputPath = Path.Combine(dir, $"rec_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _timer.Tick += Timer_Tick;

            Loaded += OnLoaded;
            Closing += OnClosing;
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            TimerText.Text = _stopwatch.Elapsed.ToString(@"mm\:ss");
        }

        // ---------------------------------------------------------------
        // Countdown
        // ---------------------------------------------------------------

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            await RunCountdown();
            StartRecording();
        }

        private async System.Threading.Tasks.Task RunCountdown()
        {
            CountdownText.Visibility = Visibility.Visible;
            RecordingPanel.Visibility = Visibility.Collapsed;
            PreviewPanel.Visibility = Visibility.Collapsed;

            for (int i = _countdown; i >= 1; i--)
            {
                CountdownText.Text = i.ToString();
                await System.Threading.Tasks.Task.Delay(1000);
            }

            CountdownText.Visibility = Visibility.Collapsed;
            RecordingPanel.Visibility = Visibility.Visible;
        }

        // ---------------------------------------------------------------
        // Recording
        // ---------------------------------------------------------------

        private void StartRecording()
        {
            var ffmpegPath = _ffmpeg.FfmpegPath;
            if (string.IsNullOrEmpty(ffmpegPath))
            {
                MessageBox.Show("FFmpeg not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
                return;
            }

            // -draw_mouse 1 : capture mouse cursor
            var args = $"-y -f gdigrab " +
                       $"-draw_mouse 1 " +
                       $"-offset_x {_region.X} -offset_y {_region.Y} " +
                       $"-video_size {_region.Width}x{_region.Height} " +
                       $"-framerate 30 " +
                       $"-i desktop " +
                       $"-c:v libx264 -pix_fmt yuv420p -preset fast " +
                       $"\"{_outputPath}\"";

            _ffmpegProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                }
            };

            try
            {
                _ffmpegProcess.Start();
                _ffmpegProcess.BeginErrorReadLine();

                // Show recording border around the captured region
                _borderWindow = new RecordingBorderWindow(_region);
                _borderWindow.Show();

                _stopwatch.Start();
                _timer.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Recording failed to start: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        }

        private void StopRecording()
        {
            if (_stopped) return;
            _stopped = true;
            _timer.Stop();
            _stopwatch.Stop();

            // Close recording border
            _borderWindow?.Close();
            _borderWindow = null;

            try
            {
                if (_ffmpegProcess != null && !_ffmpegProcess.HasExited)
                {
                    // Send 'q' + newline to FFmpeg's stdin for graceful stop (finalizes MP4)
                    _ffmpegProcess.StandardInput.WriteLine("q");
                    _ffmpegProcess.StandardInput.Flush();

                    if (!_ffmpegProcess.WaitForExit(8000))
                    {
                        _ffmpegProcess.Kill();
                        _ffmpegProcess.WaitForExit(2000);
                    }
                }
            }
            catch
            {
                // Best effort
            }
        }

        // ---------------------------------------------------------------
        // Button handlers
        // ---------------------------------------------------------------

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            StopRecording();
            ShowPreviewControls();
        }

        private void ShowPreviewControls()
        {
            RecordingPanel.Visibility = Visibility.Collapsed;
            PreviewPanel.Visibility = Visibility.Visible;
            Width = 320;
        }

        private void PreviewButton_Click(object sender, RoutedEventArgs e)
        {
            if (!File.Exists(_outputPath)) return;
            try
            {
                Process.Start(new ProcessStartInfo(_outputPath) { UseShellExecute = true });
            }
            catch { }
        }

        private void UseButton_Click(object sender, RoutedEventArgs e)
        {
            // Ensure FFmpeg has fully stopped before checking file
            StopRecording();

            if (File.Exists(_outputPath) && new FileInfo(_outputPath).Length > 0)
            {
                RecordedVideoPath = _outputPath;
            }
            else
            {
                MessageBox.Show($"録画ファイルが見つかりません: {_outputPath}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            Close();
        }

        private void RetakeButton_Click(object sender, RoutedEventArgs e)
        {
            // Clean up failed recording
            try { if (File.Exists(_outputPath)) File.Delete(_outputPath); } catch { }
            RetakeRequested = true;
            Close();
        }

        // ---------------------------------------------------------------
        // Cleanup
        // ---------------------------------------------------------------

        private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            StopRecording();
        }
    }
}

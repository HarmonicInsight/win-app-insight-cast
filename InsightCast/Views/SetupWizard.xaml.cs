using System;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using InsightCast.Services;
using InsightCast.VoiceVox;
using Microsoft.Win32;

namespace InsightCast.Views;

/// <summary>
/// Setup wizard window for first-time InsightCast configuration.
/// Guides the user through engine detection, speaker selection, and initial setup.
/// </summary>
public partial class SetupWizard : Window
{
    private const int PAGE_WELCOME = 0;
    private const int PAGE_ENGINE = 1;
    private const int PAGE_SPEAKER = 2;
    private const int PAGE_COMPLETE = 3;
    private const int TOTAL_PAGES = 4;

    private int _currentPage;
    private VoiceVoxClient? _client;
    private EngineLauncher? _launcher;
    private int _selectedSpeakerId = -1;
    private string? _engineBaseUrl;

    private readonly TextBlock[] _stepLabels;

    public SetupWizard()
    {
        InitializeComponent();

        _stepLabels = new[] { StepLabel1, StepLabel2, StepLabel3, StepLabel4 };
        _currentPage = PAGE_WELCOME;
        UpdateNavigation();
    }

    // ── Public Result Accessors ───────────────────────────────────────

    /// <summary>
    /// Returns the configured VoiceVoxClient, or null if setup was not completed.
    /// </summary>
    public VoiceVoxClient? GetClient() => _client;

    /// <summary>
    /// Returns the EngineLauncher instance, or null if the engine was not launched locally.
    /// </summary>
    public EngineLauncher? GetLauncher() => _launcher;

    /// <summary>
    /// Returns the selected default speaker style ID, or -1 if none was selected.
    /// </summary>
    public int GetSpeakerId() => _selectedSpeakerId;

    // ── Navigation ────────────────────────────────────────────────────

    private void UpdateNavigation()
    {
        // Update step indicator highlights
        for (int i = 0; i < _stepLabels.Length; i++)
        {
            if (i < _currentPage)
            {
                _stepLabels[i].Foreground = (System.Windows.Media.Brush)FindResource("Success");
                _stepLabels[i].FontWeight = FontWeights.Normal;
            }
            else if (i == _currentPage)
            {
                _stepLabels[i].Foreground = (System.Windows.Media.Brush)FindResource("BrandPrimary");
                _stepLabels[i].FontWeight = FontWeights.SemiBold;
            }
            else
            {
                _stepLabels[i].Foreground = (System.Windows.Media.Brush)FindResource("TextTertiary");
                _stepLabels[i].FontWeight = FontWeights.Normal;
            }
        }

        // Update tab selection
        WizardPages.SelectedIndex = _currentPage;

        // Back button visibility
        BackButton.Visibility = _currentPage > PAGE_WELCOME ? Visibility.Visible : Visibility.Collapsed;

        // Next vs Finish button visibility
        if (_currentPage == PAGE_COMPLETE)
        {
            NextButton.Visibility = Visibility.Collapsed;
            FinishButton.Visibility = Visibility.Visible;
        }
        else
        {
            NextButton.Visibility = Visibility.Visible;
            FinishButton.Visibility = Visibility.Collapsed;
        }

        // Disable Next on engine page until engine is found
        if (_currentPage == PAGE_ENGINE)
        {
            NextButton.IsEnabled = _client != null && _engineBaseUrl != null;
        }
        else if (_currentPage == PAGE_SPEAKER)
        {
            NextButton.IsEnabled = _selectedSpeakerId >= 0;
        }
        else
        {
            NextButton.IsEnabled = true;
        }
    }

    private void NextPage()
    {
        if (_currentPage < TOTAL_PAGES - 1)
        {
            _currentPage++;
            UpdateNavigation();
            OnPageEntered(_currentPage);
        }
    }

    private void PrevPage()
    {
        if (_currentPage > PAGE_WELCOME)
        {
            _currentPage--;
            UpdateNavigation();
        }
    }

    private void OnPageEntered(int page)
    {
        switch (page)
        {
            case PAGE_ENGINE:
                StartDetection();
                break;
            case PAGE_SPEAKER:
                LoadSpeakers();
                break;
        }
    }

    // ── Button Handlers ───────────────────────────────────────────────

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        NextPage();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        PrevPage();
    }

    private void FinishButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _client?.Dispose();
        _client = null;
        _launcher?.Dispose();
        _launcher = null;
        DialogResult = false;
        Close();
    }

    private void LaunchEngineButton_Click(object sender, RoutedEventArgs e)
    {
        LaunchEngine();
    }

    private void RetryDetectionButton_Click(object sender, RoutedEventArgs e)
    {
        StartDetection();
    }

    private void ManualSetupButton_Click(object sender, RoutedEventArgs e)
    {
        ManualSetup();
    }

    // ── Page 2: Engine Detection ──────────────────────────────────────

    private async void StartDetection()
    {
        // Reset UI to scanning state
        Dispatcher.Invoke(() =>
        {
            EngineStatusLabel.Text = LocalizationService.GetString("Setup.Engine.Searching");
            EngineProgressBar.IsIndeterminate = true;
            EngineProgressBar.Visibility = Visibility.Visible;
            EnginePathLabel.Text = "";
            LaunchEngineButton.Visibility = Visibility.Collapsed;
            RetryDetectionButton.Visibility = Visibility.Collapsed;
            ManualSetupButton.Visibility = Visibility.Collapsed;
            NextButton.IsEnabled = false;
        });

        try
        {
            var client = new VoiceVoxClient();
            var engineInfo = await Task.Run(() => client.DiscoverEngineAsync());

            if (engineInfo != null)
            {
                // Dispose previous client if it exists
                _client?.Dispose();
                _client = client;
                OnEngineFound(engineInfo.BaseUrl);
            }
            else
            {
                client.Dispose();
                OnEngineNotFound();
            }
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() =>
            {
                EngineStatusLabel.Text = LocalizationService.GetString("Setup.Engine.DetectError", ex.Message);
                EngineProgressBar.IsIndeterminate = false;
                EngineProgressBar.Visibility = Visibility.Collapsed;
                RetryDetectionButton.Visibility = Visibility.Visible;
                ManualSetupButton.Visibility = Visibility.Visible;
            });
        }
    }

    private void OnEngineFound(string baseUrl)
    {
        _engineBaseUrl = baseUrl;

        Dispatcher.Invoke(() =>
        {
            EngineStatusLabel.Text = LocalizationService.GetString("Setup.Engine.Found");
            EngineStatusLabel.Foreground = (System.Windows.Media.Brush)FindResource("Success");
            EngineProgressBar.IsIndeterminate = false;
            EngineProgressBar.Visibility = Visibility.Collapsed;
            EnginePathLabel.Text = LocalizationService.GetString("Setup.Engine.ConnectedTo", baseUrl);
            LaunchEngineButton.Visibility = Visibility.Collapsed;
            RetryDetectionButton.Visibility = Visibility.Collapsed;
            ManualSetupButton.Visibility = Visibility.Collapsed;
            NextButton.IsEnabled = true;
        });
    }

    private void OnEngineNotFound()
    {
        Dispatcher.Invoke(() =>
        {
            EngineStatusLabel.Text = LocalizationService.GetString("Setup.Engine.NotFound");
            EngineStatusLabel.Foreground = (System.Windows.Media.Brush)FindResource("Warning");
            EngineProgressBar.IsIndeterminate = false;
            EngineProgressBar.Visibility = Visibility.Collapsed;
            EnginePathLabel.Text = LocalizationService.GetString("Setup.Engine.NotFound.Hint");
            LaunchEngineButton.Visibility = Visibility.Visible;
            RetryDetectionButton.Visibility = Visibility.Visible;
            ManualSetupButton.Visibility = Visibility.Visible;
            NextButton.IsEnabled = false;
        });
    }

    private async void LaunchEngine()
    {
        Dispatcher.Invoke(() =>
        {
            EngineStatusLabel.Text = LocalizationService.GetString("Setup.Engine.Launching");
            EngineStatusLabel.Foreground = (System.Windows.Media.Brush)FindResource("TextTertiary");
            EngineProgressBar.IsIndeterminate = true;
            EngineProgressBar.Visibility = Visibility.Visible;
            EnginePathLabel.Text = "";
            LaunchEngineButton.IsEnabled = false;
            RetryDetectionButton.Visibility = Visibility.Collapsed;
            ManualSetupButton.Visibility = Visibility.Collapsed;
        });

        try
        {
            _launcher?.Dispose();
            _launcher = new EngineLauncher();
            var launched = await Task.Run(() => _launcher.Launch());

            if (launched)
            {
                var client = new VoiceVoxClient();
                var engineInfo = await Task.Run(() => client.DiscoverEngineAsync());

                if (engineInfo != null)
                {
                    _client = client;
                    OnEngineFound(engineInfo.BaseUrl);
                }
                else
                {
                    client.Dispose();
                    OnEngineNotFound();
                }
            }
            else
            {
                Dispatcher.Invoke(() =>
                {
                    EngineStatusLabel.Text = LocalizationService.GetString("Setup.Engine.LaunchFailed");
                    EngineStatusLabel.Foreground = (System.Windows.Media.Brush)FindResource("Warning");
                    EngineProgressBar.IsIndeterminate = false;
                    EngineProgressBar.Visibility = Visibility.Collapsed;
                    LaunchEngineButton.IsEnabled = true;
                    LaunchEngineButton.Visibility = Visibility.Visible;
                    RetryDetectionButton.Visibility = Visibility.Visible;
                    ManualSetupButton.Visibility = Visibility.Visible;
                });
            }
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() =>
            {
                EngineStatusLabel.Text = LocalizationService.GetString("Setup.Engine.LaunchError", ex.Message);
                EngineStatusLabel.Foreground = (System.Windows.Media.Brush)FindResource("Warning");
                EngineProgressBar.IsIndeterminate = false;
                EngineProgressBar.Visibility = Visibility.Collapsed;
                LaunchEngineButton.IsEnabled = true;
                LaunchEngineButton.Visibility = Visibility.Visible;
                RetryDetectionButton.Visibility = Visibility.Visible;
                ManualSetupButton.Visibility = Visibility.Visible;
            });
        }
    }

    private void ManualSetup()
    {
        var dialog = new OpenFileDialog
        {
            Title = LocalizationService.GetString("Setup.Engine.SelectExe"),
            Filter = LocalizationService.GetString("Setup.Engine.ExeFilter"),
            FileName = "run.exe"
        };

        if (dialog.ShowDialog(this) == true)
        {
            var selectedPath = dialog.FileName;

            Dispatcher.Invoke(() =>
            {
                EngineStatusLabel.Text = LocalizationService.GetString("Setup.Engine.Connecting");
                EngineStatusLabel.Foreground = (System.Windows.Media.Brush)FindResource("TextTertiary");
                EngineProgressBar.IsIndeterminate = true;
                EngineProgressBar.Visibility = Visibility.Visible;
                EnginePathLabel.Text = LocalizationService.GetString("Setup.Engine.Path", selectedPath);
                LaunchEngineButton.Visibility = Visibility.Collapsed;
                RetryDetectionButton.Visibility = Visibility.Collapsed;
                ManualSetupButton.Visibility = Visibility.Collapsed;
            });

            LaunchManualEngine(selectedPath);
        }
    }

    private async void LaunchManualEngine(string enginePath)
    {
        try
        {
            _launcher?.Dispose();
            _launcher = new EngineLauncher(enginePath);
            var launched = await Task.Run(() => _launcher.Launch());

            if (launched)
            {
                var client = new VoiceVoxClient();
                var engineInfo = await Task.Run(() => client.DiscoverEngineAsync());

                if (engineInfo != null)
                {
                    _client = client;
                    OnEngineFound(engineInfo.BaseUrl);
                }
                else
                {
                    client.Dispose();
                    OnEngineNotFound();
                }
            }
            else
            {
                OnEngineNotFound();
            }
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() =>
            {
                EngineStatusLabel.Text = LocalizationService.GetString("Setup.Engine.LaunchError", ex.Message);
                EngineStatusLabel.Foreground = (System.Windows.Media.Brush)FindResource("Warning");
                EngineProgressBar.IsIndeterminate = false;
                EngineProgressBar.Visibility = Visibility.Collapsed;
                LaunchEngineButton.Visibility = Visibility.Visible;
                RetryDetectionButton.Visibility = Visibility.Visible;
                ManualSetupButton.Visibility = Visibility.Visible;
            });
        }
    }

    // ── Page 3: Speaker Selection ─────────────────────────────────────

    private async void LoadSpeakers()
    {
        Dispatcher.Invoke(() =>
        {
            SpeakerStatusLabel.Text = LocalizationService.GetString("Setup.Speaker.Loading");
            SpeakerStatusLabel.Foreground = (System.Windows.Media.Brush)FindResource("TextTertiary");
            SpeakerProgressBar.IsIndeterminate = true;
            SpeakerProgressBar.Visibility = Visibility.Visible;
            SpeakerResultPanel.Visibility = Visibility.Collapsed;
            NextButton.IsEnabled = false;
        });

        try
        {
            if (_client == null)
            {
                Dispatcher.Invoke(() =>
                {
                    SpeakerStatusLabel.Text = LocalizationService.GetString("Setup.Speaker.NoConnection");
                    SpeakerStatusLabel.Foreground = (System.Windows.Media.Brush)FindResource("Warning");
                    SpeakerProgressBar.IsIndeterminate = false;
                    SpeakerProgressBar.Visibility = Visibility.Collapsed;
                });
                return;
            }

            var (speaker, styleId) = await Task.Run(() => _client.GetDefaultSpeakerAsync());

            if (speaker.HasValue && styleId >= 0)
            {
                _selectedSpeakerId = styleId;

                var speakerName = "";
                if (speaker.Value.TryGetProperty("name", out var nameElement))
                {
                    var rawName = nameElement.GetString() ?? LocalizationService.GetString("Common.Unknown");
                    speakerName = VoiceVox.VoiceVoxClient.GetLocalizedSpeakerName(rawName);
                }

                Dispatcher.Invoke(() =>
                {
                    SpeakerStatusLabel.Text = LocalizationService.GetString("Setup.Speaker.Found");
                    SpeakerStatusLabel.Foreground = (System.Windows.Media.Brush)FindResource("Success");
                    SpeakerProgressBar.IsIndeterminate = false;
                    SpeakerProgressBar.Visibility = Visibility.Collapsed;
                    SpeakerNameLabel.Text = speakerName;
                    SpeakerIdLabel.Text = LocalizationService.GetString("Setup.Speaker.StyleId", styleId);
                    SpeakerResultPanel.Visibility = Visibility.Visible;
                    NextButton.IsEnabled = true;
                });
            }
            else
            {
                Dispatcher.Invoke(() =>
                {
                    SpeakerStatusLabel.Text = LocalizationService.GetString("Setup.Speaker.NotFound");
                    SpeakerStatusLabel.Foreground = (System.Windows.Media.Brush)FindResource("Warning");
                    SpeakerProgressBar.IsIndeterminate = false;
                    SpeakerProgressBar.Visibility = Visibility.Collapsed;
                });
            }
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() =>
            {
                SpeakerStatusLabel.Text = LocalizationService.GetString("Setup.Speaker.Error", ex.Message);
                SpeakerStatusLabel.Foreground = (System.Windows.Media.Brush)FindResource("Warning");
                SpeakerProgressBar.IsIndeterminate = false;
                SpeakerProgressBar.Visibility = Visibility.Collapsed;
            });
        }
    }
}

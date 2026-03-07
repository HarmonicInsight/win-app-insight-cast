using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using InsightCast.Core;
using InsightCast.Models;
using InsightCast.Services;
using InsightCast.Services.OpenAI;

namespace InsightCast.Views
{
    public partial class AIProjectGenerateDialog : Window
    {
        private readonly Config _config;
        private readonly IOpenAIService _openAIService;
        private CancellationTokenSource? _cts;

        public Project? GeneratedProject { get; private set; }

        public AIProjectGenerateDialog(Config config, IOpenAIService openAIService)
        {
            InitializeComponent();
            _config = config;
            _openAIService = openAIService;
            RestoreLastSettings();
        }

        private async void GenerateButton_Click(object sender, RoutedEventArgs e)
        {
            var topic = TopicTextBox.Text?.Trim();
            if (string.IsNullOrEmpty(topic))
            {
                ShowError(LocalizationService.GetString("AIGenerate.Error.NoTopic"));
                return;
            }

            if (!_openAIService.IsConfigured)
            {
                var apiKey = ApiKeyManager.GetApiKey(_config);
                if (string.IsNullOrEmpty(apiKey))
                {
                    ShowError(LocalizationService.GetString("AIGenerate.Error.NoApiKey"));
                    return;
                }
                await _openAIService.ConfigureAsync(apiKey);
            }

            var style = GetSelectedStyle();
            var sceneCount = GetSelectedSceneCount();
            var generateImages = GenerateImagesCheckBox.IsChecked == true;

            SetUIGenerating(true);
            HideError();
            _cts = new CancellationTokenSource();

            try
            {
                var project = await GenerateProjectAsync(topic, style, sceneCount, generateImages, _cts.Token);
                if (project != null)
                {
                    GeneratedProject = project;
                    SaveCurrentSettings();
                    DialogResult = true;
                    Close();
                }
            }
            catch (OperationCanceledException)
            {
                // Cancelled by user
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
            finally
            {
                SetUIGenerating(false);
                _cts?.Dispose();
                _cts = null;
            }
        }

        private async Task<Project?> GenerateProjectAsync(string topic, string style, int sceneCount, bool generateImages, CancellationToken ct)
        {
            UpdateProgress(LocalizationService.GetString("AIGenerate.Progress.Generating"));

            var project = new Project();
            project.Scenes.Clear();

            // Generate narration for each scene
            var durationPerScene = 30; // seconds
            for (int i = 0; i < sceneCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                UpdateProgress(string.Format(LocalizationService.GetString("AIGenerate.Progress.Scene"), i + 1, sceneCount));

                var request = new TextGenerationRequest
                {
                    Topic = $"{topic} - Part {i + 1} of {sceneCount}",
                    Style = style,
                    TargetDurationSeconds = durationPerScene,
                    AdditionalInstructions = i == 0
                        ? "This is the introduction. Start with an engaging hook."
                        : i == sceneCount - 1
                            ? "This is the conclusion. Summarize key points and end memorably."
                            : $"This is part {i + 1}. Continue the narrative naturally."
                };

                var result = await _openAIService.GenerateNarrationAsync(request, ct);
                if (!result.Success)
                {
                    ShowError(result.ErrorMessage ?? "Failed to generate narration");
                    return null;
                }

                var scene = new Scene
                {
                    NarrationText = result.Text ?? string.Empty,
                    SubtitleText = result.Text ?? string.Empty
                };

                // Generate image if requested
                if (generateImages)
                {
                    ct.ThrowIfCancellationRequested();
                    UpdateProgress(string.Format(LocalizationService.GetString("AIGenerate.Progress.Image"), i + 1, sceneCount));

                    var imageRequest = new ImageGenerationRequest
                    {
                        Description = $"Visual representation for: {topic} - Scene {i + 1}",
                        Style = "photorealistic"
                    };

                    var imageResult = await _openAIService.GenerateImageAsync(imageRequest, ct);
                    if (imageResult.Success && !string.IsNullOrEmpty(imageResult.ImagePath))
                    {
                        scene.MediaPath = imageResult.ImagePath;
                    }
                }

                project.Scenes.Add(scene);
            }

            return project;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            DialogResult = false;
            Close();
        }

        private string GetSelectedStyle()
        {
            if (StyleCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
                return tag;
            return "educational";
        }

        private int GetSelectedSceneCount()
        {
            if (SceneCountCombo.SelectedItem is ComboBoxItem item &&
                int.TryParse(item.Content?.ToString(), out int count))
                return count;
            return 5;
        }

        private void SetUIGenerating(bool generating)
        {
            GenerateButton.IsEnabled = !generating;
            TopicTextBox.IsEnabled = !generating;
            StyleCombo.IsEnabled = !generating;
            SceneCountCombo.IsEnabled = !generating;
            GenerateImagesCheckBox.IsEnabled = !generating;
            ProgressPanel.Visibility = generating ? Visibility.Visible : Visibility.Collapsed;
            CancelButton.Content = generating
                ? LocalizationService.GetString("Common.Cancel")
                : LocalizationService.GetString("Common.Close");
        }

        private void UpdateProgress(string message)
        {
            Dispatcher.Invoke(() =>
            {
                ProgressLabel.Text = message;
            });
        }

        private void ShowError(string message)
        {
            ErrorPanel.Visibility = Visibility.Visible;
            ErrorLabel.Text = message;
        }

        private void HideError()
        {
            ErrorPanel.Visibility = Visibility.Collapsed;
            ErrorLabel.Text = string.Empty;
        }

        // ── 前回設定の保存・復元 ──

        private void RestoreLastSettings()
        {
            // TODO: implement settings persistence
        }

        private void SaveCurrentSettings()
        {
            // TODO: implement settings persistence
        }
    }
}

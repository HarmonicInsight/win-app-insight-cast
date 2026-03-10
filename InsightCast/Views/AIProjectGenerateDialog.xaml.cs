using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using InsightCast.Core;
using InsightCast.Models;
using InsightCast.Services;

namespace InsightCast.Views
{
    public partial class AIProjectGenerateDialog : Window
    {
        private readonly Config _config;
        private CancellationTokenSource? _cts;

        public Project? GeneratedProject { get; private set; }

        public AIProjectGenerateDialog(Config config)
        {
            InitializeComponent();
            _config = config;
            RestoreLastSettings();

            // Image generation not available (OpenAI removed)
            GenerateImagesCheckBox.IsChecked = false;
            GenerateImagesCheckBox.IsEnabled = false;
        }

        private async void GenerateButton_Click(object sender, RoutedEventArgs e)
        {
            var topic = TopicTextBox.Text?.Trim();
            if (string.IsNullOrEmpty(topic))
            {
                ShowError(LocalizationService.GetString("AIGenerate.Error.NoTopic"));
                return;
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

        private Task<Project?> GenerateProjectAsync(string topic, string style, int sceneCount, bool generateImages, CancellationToken ct)
        {
            UpdateProgress(LocalizationService.GetString("AIGenerate.Progress.Generating"));

            var project = new Project();
            project.Scenes.Clear();

            // Generate placeholder scenes (AI generation removed)
            for (int i = 0; i < sceneCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                UpdateProgress(string.Format(LocalizationService.GetString("AIGenerate.Progress.Scene"), i + 1, sceneCount));

                var scene = new Scene
                {
                    NarrationText = $"{topic} - Part {i + 1} of {sceneCount}",
                    SubtitleText = $"{topic} - Part {i + 1} of {sceneCount}"
                };

                project.Scenes.Add(scene);
            }

            return Task.FromResult<Project?>(project);
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
            var style = _config.Get<string>("AIGenerate.LastStyle");
            if (!string.IsNullOrEmpty(style))
            {
                for (int i = 0; i < StyleCombo.Items.Count; i++)
                {
                    if (StyleCombo.Items[i] is ComboBoxItem item && item.Tag is string tag && tag == style)
                    {
                        StyleCombo.SelectedIndex = i;
                        break;
                    }
                }
            }

            var sceneCount = _config.Get<int?>("AIGenerate.LastSceneCount");
            if (sceneCount > 0)
            {
                for (int i = 0; i < SceneCountCombo.Items.Count; i++)
                {
                    if (SceneCountCombo.Items[i] is ComboBoxItem item &&
                        int.TryParse(item.Content?.ToString(), out int count) && count == sceneCount)
                    {
                        SceneCountCombo.SelectedIndex = i;
                        break;
                    }
                }
            }

            var genImages = _config.Get<bool?>("AIGenerate.LastGenerateImages");
            if (genImages.HasValue)
                GenerateImagesCheckBox.IsChecked = genImages.Value;
        }

        private void SaveCurrentSettings()
        {
            _config.BeginUpdate();
            _config.Set("AIGenerate.LastStyle", GetSelectedStyle());
            _config.Set("AIGenerate.LastSceneCount", GetSelectedSceneCount());
            _config.Set("AIGenerate.LastGenerateImages", GenerateImagesCheckBox.IsChecked == true);
            _config.EndUpdate();
        }
    }
}

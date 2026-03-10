using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using InsightCast.Core;
using InsightCast.Models;
using InsightCast.Services;

namespace InsightCast.Views
{
    public class MediaItem
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName => Path.GetFileName(FilePath);
        public ImageSource? Thumbnail { get; set; }
        public bool IsVideo { get; set; }
    }

    public class ScenePreviewItem
    {
        public int Index { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Duration { get; set; } = string.Empty;
        public string Script { get; set; } = string.Empty;
        public string ImagePrompt { get; set; } = string.Empty;
        public ImageSource? Thumbnail { get; set; }
        public string? MediaPath { get; set; }
    }

    public partial class VideoWizardDialog : Window
    {
        private readonly Config _config;
        private readonly Project _project;

        private int _currentStep = 1;
        private readonly ObservableCollection<MediaItem> _mediaItems = new();
        private readonly List<ScenePreviewItem> _generatedScenes = new();

        public bool WizardCompleted { get; private set; }

        public VideoWizardDialog(Config config, Project project)
        {
            _config = config;
            _project = project;
            InitializeComponent();
            MediaList.ItemsSource = _mediaItems;
        }

        private void UpdateStepIndicators()
        {
            var activeStyle = FindResource("StepIndicatorActive") as Style;
            var completeStyle = FindResource("StepIndicatorComplete") as Style;
            var defaultStyle = FindResource("StepIndicator") as Style;

            Step1Indicator.Style = _currentStep == 1 ? activeStyle : (_currentStep > 1 ? completeStyle : defaultStyle);
            Step2Indicator.Style = _currentStep == 2 ? activeStyle : (_currentStep > 2 ? completeStyle : defaultStyle);
            Step3Indicator.Style = _currentStep == 3 ? activeStyle : (_currentStep > 3 ? completeStyle : defaultStyle);
            Step4Indicator.Style = _currentStep == 4 ? activeStyle : defaultStyle;

            Step1Panel.Visibility = _currentStep == 1 ? Visibility.Visible : Visibility.Collapsed;
            Step2Panel.Visibility = _currentStep == 2 ? Visibility.Visible : Visibility.Collapsed;
            Step3Panel.Visibility = _currentStep == 3 ? Visibility.Visible : Visibility.Collapsed;
            Step4Panel.Visibility = _currentStep == 4 ? Visibility.Visible : Visibility.Collapsed;

            BackButton.IsEnabled = _currentStep > 1;
            NextButton.Content = _currentStep == 4
                ? LocalizationService.GetString("Wizard.Generate")
                : LocalizationService.GetString("Wizard.Next");
        }

        private void OnMediaDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void OnMediaDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                AddMediaFiles(files);
            }
        }

        private void OnBrowseMedia(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Multiselect = true,
                Filter = LocalizationService.GetString("VM.Media.Filter")
            };

            if (dialog.ShowDialog() == true)
            {
                AddMediaFiles(dialog.FileNames);
            }
        }

        private void AddMediaFiles(string[] files)
        {
            var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };
            var videoExtensions = new[] { ".mp4", ".avi", ".mov", ".wmv", ".mkv", ".webm" };

            foreach (var file in files)
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (!imageExtensions.Contains(ext) && !videoExtensions.Contains(ext))
                    continue;

                if (_mediaItems.Any(m => m.FilePath == file))
                    continue;

                var item = new MediaItem
                {
                    FilePath = file,
                    IsVideo = videoExtensions.Contains(ext),
                    Thumbnail = LoadThumbnail(file, videoExtensions.Contains(ext))
                };
                _mediaItems.Add(item);
            }

            UpdateMediaListVisibility();
        }

        private ImageSource? LoadThumbnail(string path, bool isVideo)
        {
            try
            {
                if (isVideo)
                {
                    // For videos, just show a placeholder or first frame if possible
                    return null;
                }

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path);
                bitmap.DecodePixelWidth = 100;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        private void OnRemoveMedia(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is MediaItem item)
            {
                _mediaItems.Remove(item);
                UpdateMediaListVisibility();
            }
        }

        private void UpdateMediaListVisibility()
        {
            if (_mediaItems.Count > 0)
            {
                DropHint.Visibility = Visibility.Collapsed;
                MediaList.Visibility = Visibility.Visible;
            }
            else
            {
                DropHint.Visibility = Visibility.Visible;
                MediaList.Visibility = Visibility.Collapsed;
            }
        }

        private void OnBack(object sender, RoutedEventArgs e)
        {
            if (_currentStep > 1)
            {
                _currentStep--;
                UpdateStepIndicators();
            }
        }

        private async void OnNext(object sender, RoutedEventArgs e)
        {
            if (_currentStep == 4)
            {
                // Generate and apply
                ApplyToProject();
                WizardCompleted = true;
                DialogResult = true;
                Close();
                return;
            }

            // Validate current step
            if (!ValidateCurrentStep())
                return;

            _currentStep++;
            UpdateStepIndicators();

            // If entering step 4, generate the preview
            if (_currentStep == 4)
            {
                UpdateSummary();
                await GeneratePreviewAsync();
            }
        }

        private bool ValidateCurrentStep()
        {
            switch (_currentStep)
            {
                case 1:
                    if (_mediaItems.Count == 0)
                    {
                        MessageBox.Show(LocalizationService.GetString("Wizard.Error.NoMedia"),
                            LocalizationService.GetString("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }
                    return true;
                case 2:
                    if (PurposeCustom.IsChecked == true && string.IsNullOrWhiteSpace(CustomPurposeText.Text))
                    {
                        MessageBox.Show(LocalizationService.GetString("Wizard.Error.NoPurpose"),
                            LocalizationService.GetString("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }
                    return true;
                case 3:
                    return true;
                default:
                    return true;
            }
        }

        private string GetSelectedPurpose()
        {
            if (PurposeIntro.IsChecked == true) return "product_intro";
            if (PurposeTutorial.IsChecked == true) return "tutorial";
            if (PurposeExplain.IsChecked == true) return "explanation";
            if (PurposePromo.IsChecked == true) return "promotion";
            if (PurposeCustom.IsChecked == true) return CustomPurposeText.Text;
            return "product_intro";
        }

        private string GetPurposeDisplayText()
        {
            if (PurposeIntro.IsChecked == true) return LocalizationService.GetString("Wizard.Purpose.Intro");
            if (PurposeTutorial.IsChecked == true) return LocalizationService.GetString("Wizard.Purpose.Tutorial");
            if (PurposeExplain.IsChecked == true) return LocalizationService.GetString("Wizard.Purpose.Explain");
            if (PurposePromo.IsChecked == true) return LocalizationService.GetString("Wizard.Purpose.Promo");
            if (PurposeCustom.IsChecked == true) return CustomPurposeText.Text;
            return "";
        }

        private int GetSelectedDuration()
        {
            if (Duration15.IsChecked == true) return 15;
            if (Duration30.IsChecked == true) return 30;
            if (Duration60.IsChecked == true) return 60;
            if (Duration90.IsChecked == true) return 90;
            if (Duration120.IsChecked == true) return 120;
            if (DurationCustom.IsChecked == true && int.TryParse(CustomDurationText.Text, out var custom))
                return Math.Clamp(custom, 10, 300);
            return 30;
        }

        private void UpdateSummary()
        {
            SummaryMedia.Text = string.Format(LocalizationService.GetString("Wizard.Summary.Media"), _mediaItems.Count);
            SummaryPurpose.Text = string.Format(LocalizationService.GetString("Wizard.Summary.Purpose"), GetPurposeDisplayText());
            SummaryDuration.Text = string.Format(LocalizationService.GetString("Wizard.Summary.Duration"), GetSelectedDuration());

            var sceneCount = CalculateSceneCount();
            SummaryScenes.Text = string.Format(LocalizationService.GetString("Wizard.Summary.Scenes"), sceneCount);
        }

        private int CalculateSceneCount()
        {
            var duration = GetSelectedDuration();
            var mediaCount = _mediaItems.Count;

            // Roughly 8-12 seconds per scene for narration
            var scenesFromDuration = Math.Max(1, duration / 10);

            // Use media count as minimum, but cap based on duration
            return Math.Max(mediaCount, Math.Min(scenesFromDuration, mediaCount + 2));
        }

        private Task GeneratePreviewAsync()
        {
            GeneratingIndicator.Visibility = Visibility.Visible;
            PreviewScroll.Visibility = Visibility.Collapsed;
            NextButton.IsEnabled = false;

            _generatedScenes.Clear();

            try
            {
                var purposeText = GetPurposeDisplayText();
                var sceneCount = CalculateSceneCount();
                var duration = GetSelectedDuration();
                var secondsPerScene = duration / sceneCount;

                // Use template-based generation (OpenAI removed)
                GenerateTemplateScenes(purposeText, sceneCount, secondsPerScene);

                ScenePreviewList.ItemsSource = _generatedScenes;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Generation error: {ex.Message}");
                GenerateTemplateScenes(GetPurposeDisplayText(), CalculateSceneCount(), GetSelectedDuration() / CalculateSceneCount());
                ScenePreviewList.ItemsSource = _generatedScenes;
            }
            finally
            {
                GeneratingIndicator.Visibility = Visibility.Collapsed;
                PreviewScroll.Visibility = Visibility.Visible;
                NextButton.IsEnabled = true;
            }

            return Task.CompletedTask;
        }

        private string BuildGenerationPrompt(string purpose, int sceneCount, int secondsPerScene)
        {
            var mediaDescriptions = string.Join(", ", _mediaItems.Select((m, i) => $"素材{i + 1}: {m.FileName}"));

            return $@"以下の条件で動画のナレーションスクリプトを{sceneCount}シーン分作成してください。

目的: {purpose}
素材: {mediaDescriptions}
各シーンの長さ: 約{secondsPerScene}秒

出力形式（各シーンごとに）:
シーン1:
タイトル: [シーンのタイトル]
スクリプト: [ナレーション文]
画像プロンプト: [DALL-E用の英語プロンプト]

シーン2:
...

必ず{sceneCount}シーン分出力してください。";
        }

        private void ParseGeneratedScenes(string aiResponse, int expectedCount, int secondsPerScene)
        {
            var lines = aiResponse.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var currentScene = new ScenePreviewItem();
            var sceneIndex = 0;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                if (trimmed.StartsWith("シーン") && trimmed.Contains(":"))
                {
                    if (sceneIndex > 0 && !string.IsNullOrEmpty(currentScene.Script))
                    {
                        FinalizeScene(currentScene, sceneIndex - 1, secondsPerScene);
                        _generatedScenes.Add(currentScene);
                    }
                    currentScene = new ScenePreviewItem();
                    sceneIndex++;
                }
                else if (trimmed.StartsWith("タイトル:"))
                {
                    currentScene.Title = trimmed.Substring("タイトル:".Length).Trim();
                }
                else if (trimmed.StartsWith("スクリプト:"))
                {
                    currentScene.Script = trimmed.Substring("スクリプト:".Length).Trim();
                }
                else if (trimmed.StartsWith("画像プロンプト:"))
                {
                    currentScene.ImagePrompt = trimmed.Substring("画像プロンプト:".Length).Trim();
                }
            }

            // Add last scene
            if (!string.IsNullOrEmpty(currentScene.Script))
            {
                FinalizeScene(currentScene, sceneIndex - 1, secondsPerScene);
                _generatedScenes.Add(currentScene);
            }

            // If we didn't get enough scenes, fill with templates
            while (_generatedScenes.Count < expectedCount)
            {
                var idx = _generatedScenes.Count;
                var scene = new ScenePreviewItem
                {
                    Title = LocalizationService.GetString("Wizard.SceneTitle", idx + 1),
                    Script = LocalizationService.GetString("Wizard.EditScript"),
                    ImagePrompt = "professional presentation slide"
                };
                FinalizeScene(scene, idx, secondsPerScene);
                _generatedScenes.Add(scene);
            }
        }

        private void FinalizeScene(ScenePreviewItem scene, int index, int secondsPerScene)
        {
            scene.Index = index + 1;
            scene.Duration = LocalizationService.GetString("Wizard.Duration", secondsPerScene);

            if (string.IsNullOrEmpty(scene.Title))
                scene.Title = LocalizationService.GetString("Wizard.SceneTitle", index + 1);

            // Assign media if available
            if (index < _mediaItems.Count)
            {
                scene.MediaPath = _mediaItems[index].FilePath;
                scene.Thumbnail = _mediaItems[index].Thumbnail;
            }
        }

        private void GenerateTemplateScenes(string purpose, int sceneCount, int secondsPerScene)
        {
            var templates = GetTemplatesForPurpose(purpose);

            for (int i = 0; i < sceneCount; i++)
            {
                var templateIndex = Math.Min(i, templates.Length - 1);
                var template = templates[templateIndex];

                var scene = new ScenePreviewItem
                {
                    Index = i + 1,
                    Title = template.title,
                    Duration = $"{secondsPerScene}秒",
                    Script = template.script,
                    ImagePrompt = template.prompt
                };

                if (i < _mediaItems.Count)
                {
                    scene.MediaPath = _mediaItems[i].FilePath;
                    scene.Thumbnail = _mediaItems[i].Thumbnail;
                }

                _generatedScenes.Add(scene);
            }
        }

        private (string title, string script, string prompt)[] GetTemplatesForPurpose(string purpose)
        {
            // Default templates based on purpose type
            return new[]
            {
                ("導入", "こんにちは。今日は〇〇についてご紹介します。", "professional introduction scene, clean modern design"),
                ("概要", "まず、基本的な概要からご説明します。", "overview diagram, infographic style"),
                ("詳細説明", "それでは、詳しく見ていきましょう。", "detailed explanation, step by step visual"),
                ("メリット", "この機能のメリットをご紹介します。", "benefits showcase, positive imagery"),
                ("まとめ", "以上が〇〇の紹介でした。ご視聴ありがとうございました。", "conclusion scene, call to action")
            };
        }

        private void ApplyToProject()
        {
            // Clear existing scenes except the first one
            while (_project.Scenes.Count > 1)
            {
                _project.RemoveScene(_project.Scenes.Count - 1);
            }

            // Apply generated scenes
            for (int i = 0; i < _generatedScenes.Count; i++)
            {
                var generated = _generatedScenes[i];

                Scene scene;
                if (i == 0)
                {
                    scene = _project.Scenes[0];
                }
                else
                {
                    _project.AddScene();
                    scene = _project.Scenes[i];
                }

                scene.Title = generated.Title;
                scene.NarrationText = generated.Script;
                scene.SubtitleText = generated.Script;

                if (!string.IsNullOrEmpty(generated.MediaPath))
                {
                    scene.MediaPath = generated.MediaPath;
                    var ext = Path.GetExtension(generated.MediaPath).ToLowerInvariant();
                    scene.MediaType = new[] { ".mp4", ".avi", ".mov", ".wmv", ".mkv", ".webm" }.Contains(ext)
                        ? MediaType.Video
                        : MediaType.Image;
                }
            }
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

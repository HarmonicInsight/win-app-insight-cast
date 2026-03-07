using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using InsightCast.Core;
using InsightCast.Models;
using InsightCast.Services;
using InsightCast.Services.Claude;
using InsightCast.Services.OpenAI;
using InsightCast.ViewModels;
using Microsoft.Win32;

namespace InsightCast.Views
{
    public partial class PlanningTab : UserControl
    {
        private PlanningViewModel? _viewModel;
        private WorkingFolderViewModel? _workingFolderVm;
        private IClaudeService? _claudeService;
        private IOpenAIService? _openAIService;
        private Config? _config;
        private ChapterStructure? _generatedChapters;
        private CancellationTokenSource? _cts;

        public PlanningTab()
        {
            InitializeComponent();
        }

        /// <summary>Gets the PlanningViewModel for this tab.</summary>
        public PlanningViewModel? ViewModel => _viewModel;

        /// <summary>Gets the WorkingFolderViewModel for this tab.</summary>
        public WorkingFolderViewModel? WorkingFolderViewModel => _workingFolderVm;

        public void Initialize(Config config, Project project, IClaudeService? claudeService = null)
        {
            _viewModel = new PlanningViewModel(config, project);
            _claudeService = claudeService;
            _config = config;
            DataContext = _viewModel;

            // Initialize working folder panel
            _workingFolderVm = new WorkingFolderViewModel();
            WorkingFolderPanelControl.Initialize(_workingFolderVm);

            // Load working folder if project has one
            if (!string.IsNullOrEmpty(project.WorkingFolderPath) && Directory.Exists(project.WorkingFolderPath))
            {
                _workingFolderVm.LoadFromFolder(project.WorkingFolderPath);
            }

            // Show scene list if project already has scenes
            UpdateSceneListVisibility();
        }

        public void RefreshScenes()
        {
            _viewModel?.RefreshSceneList();
            UpdateSceneListVisibility();
        }

        /// <summary>サムネイル設定をProjectから再読み込み（テンプレート適用後に呼び出す）</summary>
        public void ReloadThumbnailSettings()
        {
            _viewModel?.LoadThumbnailSettingsFromProject();
        }

        /// <summary>Raised when scenes are added, removed, or modified in the planning tab.</summary>
        public event Action? ScenesChanged
        {
            add { if (_viewModel != null) _viewModel.ScenesChanged += value; }
            remove { if (_viewModel != null) _viewModel.ScenesChanged -= value; }
        }

        /// <summary>Event raised when the user clicks the thumbnail creator button.</summary>
        public event Action? TitleCreatorPopOutRequested;

        // ── Step 1: AI構成生成 ──

        private async void GenerateChapters_Click(object sender, RoutedEventArgs e)
        {
            if (_claudeService == null || !_claudeService.IsConfigured)
            {
                MessageBox.Show(
                    LocalizationService.GetString("AiChapter.NotConfigured"),
                    LocalizationService.GetString("Common.Error"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var referenceContext = _workingFolderVm?.BuildReferenceContext() ?? "";
            var materialCount = _workingFolderVm?.GetCheckedFileNodes().Count ?? 0;

            MaterialStatusText.Text = LocalizationService.GetString("AiChapter.MaterialCount")
                + $" {materialCount}";

            var chapterCountItem = ChapterCountCombo.SelectedItem as ComboBoxItem;
            if (chapterCountItem == null) return;
            var chapterCount = int.Parse(chapterCountItem.Content.ToString()!);

            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            GenerateChaptersButton.IsEnabled = false;
            ChapterProgressBar.Visibility = Visibility.Visible;
            ChapterStatusText.Text = LocalizationService.GetString("AiChapter.Generating");

            try
            {
                var service = new ChapterGenerationService(_claudeService);
                _generatedChapters = await service.GenerateChaptersAsync(
                    referenceContext,
                    chapterCount,
                    InstructionsBox.Text,
                    _cts.Token);

                DisplayChapters(_generatedChapters);
                ChapterStatusText.Text = LocalizationService.GetString("AiChapter.Generated",
                    _generatedChapters.Chapters.Count);
            }
            catch (OperationCanceledException)
            {
                ChapterStatusText.Text = LocalizationService.GetString("AiChapter.Cancelled");
            }
            catch (Exception ex)
            {
                ChapterStatusText.Text = LocalizationService.GetString("Common.ErrorWithMessage", ex.Message);
                MessageBox.Show(ex.Message, LocalizationService.GetString("Common.Error"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                GenerateChaptersButton.IsEnabled = true;
                ChapterProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        private void DisplayChapters(ChapterStructure chapters)
        {
            VideoTitleText.Text = chapters.VideoTitle;
            ChapterPreviewPanel.Children.Clear();

            for (int i = 0; i < chapters.Chapters.Count; i++)
            {
                var ch = chapters.Chapters[i];

                // Compact: "1. Title — narration preview..."
                var panel = new DockPanel { Margin = new Thickness(0, 0, 0, 3) };

                var num = new TextBlock
                {
                    Text = $"{i + 1}.",
                    FontWeight = FontWeights.Bold, FontSize = 11,
                    Foreground = (Brush)FindResource("AccentGold"),
                    Width = 22, Margin = new Thickness(0, 0, 4, 0)
                };
                panel.Children.Add(num);
                DockPanel.SetDock(num, Dock.Left);

                var title = new TextBlock
                {
                    FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground = (Brush)FindResource("TextPrimary")
                };
                title.Inlines.Add(new Run(ch.Title) { FontWeight = FontWeights.SemiBold });
                if (!string.IsNullOrWhiteSpace(ch.Narration))
                {
                    var preview = ch.Narration.Length > 60 ? ch.Narration[..60] + "..." : ch.Narration;
                    title.Inlines.Add(new Run($" — {preview}")
                    {
                        Foreground = (Brush)FindResource("TextTertiary"),
                        FontSize = 10
                    });
                }
                panel.Children.Add(title);

                ChapterPreviewPanel.Children.Add(panel);
            }

            ChapterPreviewCard.Visibility = Visibility.Visible;
        }

        // ── Step 2: シーンに展開（編集用） ──

        private void ExpandToScenes_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null || _generatedChapters == null) return;

            _viewModel.Project.Scenes.Clear();
            foreach (var ch in _generatedChapters.Chapters)
            {
                var scene = new Scene
                {
                    Title = ch.Title,
                    NarrationText = ch.Narration,
                    SubtitleText = ch.Narration
                };
                if (!string.IsNullOrWhiteSpace(ch.ImageDescription))
                {
                    scene.AIGeneration = new AIGenerationSettings
                    {
                        GenerateImage = true,
                        ImageDescription = ch.ImageDescription,
                        NarrationTopic = ch.Title
                    };
                }
                _viewModel.Project.Scenes.Add(scene);
            }

            _viewModel.RefreshSceneList();
            UpdateSceneListVisibility();
        }

        // ── Step 5: PPTX出力（ゴール） ──

        private async void ExportPptx_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null || _viewModel.Project.Scenes.Count == 0) return;

            var chapters = BuildChaptersFromScenes();

            var dlg = new SaveFileDialog
            {
                Filter = "PowerPoint|*.pptx",
                FileName = !string.IsNullOrEmpty(chapters.VideoTitle)
                    ? chapters.VideoTitle + ".pptx"
                    : "presentation.pptx",
                Title = LocalizationService.GetString("AiFlow.ExportPptx")
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                await Task.Run(() => PptxGeneratorService.GeneratePptx(chapters, dlg.FileName));
                ImageGenStatusText.Text = LocalizationService.GetString("AiFlow.PptxSaved");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    LocalizationService.GetString("Common.ErrorWithMessage", ex.Message),
                    LocalizationService.GetString("Common.Error"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private ChapterStructure BuildChaptersFromScenes()
        {
            var chapters = new ChapterStructure
            {
                VideoTitle = _viewModel?.Project.ProjectPath != null
                    ? Path.GetFileNameWithoutExtension(_viewModel.Project.ProjectPath)
                    : "",
                Chapters = new List<ChapterItem>()
            };

            if (_viewModel == null) return chapters;

            foreach (var scene in _viewModel.Project.Scenes)
            {
                chapters.Chapters.Add(new ChapterItem
                {
                    Title = scene.Title ?? "",
                    Narration = scene.NarrationText ?? "",
                    ImageDescription = scene.AIGeneration?.ImageDescription ?? ""
                });
            }
            return chapters;
        }

        private void ApplyChapterAISettings(ChapterStructure chapters)
        {
            if (_viewModel == null) return;
            var scenes = _viewModel.Project.Scenes;

            // PPTX import adds a title slide at index 0; chapter slides start at index 1.
            int offset = (scenes.Count == chapters.Chapters.Count + 1) ? 1 : 0;

            for (int i = 0; i < chapters.Chapters.Count; i++)
            {
                var sceneIdx = i + offset;
                if (sceneIdx >= scenes.Count) break;

                var ch = chapters.Chapters[i];
                var scene = scenes[sceneIdx];

                if (string.IsNullOrEmpty(scene.NarrationText))
                    scene.NarrationText = ch.Narration;

                if (string.IsNullOrEmpty(scene.Title))
                    scene.Title = ch.Title;

                if (!string.IsNullOrWhiteSpace(ch.ImageDescription))
                {
                    scene.AIGeneration ??= new AIGenerationSettings();
                    scene.AIGeneration.GenerateImage = true;
                    scene.AIGeneration.ImageDescription = ch.ImageDescription;
                    scene.AIGeneration.NarrationTopic = ch.Title;
                }
            }
        }

        // ── Step 3: AI画像生成 ──

        private async void AiGenerateImages_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null) return;

            var scenesNeedingImages = _viewModel.Project.Scenes
                .Where(s => s.AIGeneration?.CanGenerateImage == true && !s.HasMedia)
                .ToList();

            if (scenesNeedingImages.Count == 0)
            {
                MessageBox.Show(
                    LocalizationService.GetString("AiGenerate.NoScenesNeedImages"),
                    LocalizationService.GetString("Common.Info"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            await EnsureOpenAIConfigured();
            if (_openAIService == null || !_openAIService.IsConfigured)
            {
                MessageBox.Show(
                    LocalizationService.GetString("AiGenerate.NotConfigured"),
                    LocalizationService.GetString("Common.Error"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                LocalizationService.GetString("AiGenerate.ConfirmImages", scenesNeedingImages.Count),
                LocalizationService.GetString("AiGenerate.Title"),
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            var button = sender as Button;
            if (button != null) button.IsEnabled = false;

            var cts = new CancellationTokenSource();
            int generated = 0;
            int failed = 0;

            try
            {
                for (int i = 0; i < scenesNeedingImages.Count; i++)
                {
                    var scene = scenesNeedingImages[i];
                    cts.Token.ThrowIfCancellationRequested();

                    ImageGenStatusText.Text = LocalizationService.GetString("AiGenerate.Progress",
                        i + 1, scenesNeedingImages.Count);

                    var request = new ImageGenerationRequest
                    {
                        Description = scene.AIGeneration!.ImageDescription!,
                        Style = scene.AIGeneration.ImageStyle
                    };

                    var result = await _openAIService.GenerateImageAsync(request, cts.Token);
                    if (result.Success && !string.IsNullOrEmpty(result.ImagePath))
                    {
                        scene.MediaPath = result.ImagePath;
                        scene.MediaType = MediaType.Image;
                        scene.AIGeneration.GenerateImage = false;
                        generated++;
                    }
                    else
                    {
                        failed++;
                    }
                }

                _viewModel.RefreshSceneList();
                UpdateSceneListVisibility();

                ImageGenStatusText.Text = failed == 0
                    ? LocalizationService.GetString("AiGenerate.ImagesComplete", generated)
                    : LocalizationService.GetString("AiGenerate.ImagesPartial", generated, failed);
            }
            catch (OperationCanceledException)
            {
                ImageGenStatusText.Text = LocalizationService.GetString("AiChapter.Cancelled");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    LocalizationService.GetString("Common.ErrorWithMessage", ex.Message),
                    LocalizationService.GetString("Common.Error"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (button != null) button.IsEnabled = true;
            }
        }

        // ── Public methods for Ribbon relay ──

        public void ExecuteExportPptx() => ExportPptx_Click(this, new RoutedEventArgs());
        public void ExecuteExportJson() => ExportJson_Click(this, new RoutedEventArgs());
        public void ExecuteImportJson() => ImportJson_Click(this, new RoutedEventArgs());
        public void ExecuteAiGenerateImages() => AiGenerateImages_Click(this, new RoutedEventArgs());
        public void ExecuteOpenThumbnailCreator() => TitleCreatorPopOutRequested?.Invoke();

        // ── Copy prompt ──

        private void CopyPrompt_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string prompt && !string.IsNullOrWhiteSpace(prompt))
            {
                try
                {
                    Clipboard.SetText(prompt);
                    ImageGenStatusText.Text = LocalizationService.GetString("AiFlow.PromptCopied");
                }
                catch { }
            }
        }

        // ── JSON Export / Import ──

        private void ExportJson_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null || _viewModel.Project.Scenes.Count == 0) return;

            var dlg = new SaveFileDialog
            {
                Filter = "JSON|*.json",
                FileName = "scenes.json",
                Title = LocalizationService.GetString("AiFlow.JsonExport")
            };
            if (dlg.ShowDialog() != true) return;

            var scenes = _viewModel.Project.Scenes.Select((s, i) => new SceneJsonItem
            {
                Index = i + 1,
                Title = s.Title ?? "",
                Narration = s.NarrationText ?? "",
                ImagePrompt = s.AIGeneration?.ImageDescription ?? ""
            }).ToList();

            var json = JsonSerializer.Serialize(scenes, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
            File.WriteAllText(dlg.FileName, json, System.Text.Encoding.UTF8);
            ImageGenStatusText.Text = LocalizationService.GetString("AiFlow.JsonExported");
        }

        private void ImportJson_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null) return;

            var dlg = new OpenFileDialog
            {
                Filter = "JSON|*.json",
                Title = LocalizationService.GetString("AiFlow.JsonImport")
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var json = File.ReadAllText(dlg.FileName, System.Text.Encoding.UTF8);
                var items = JsonSerializer.Deserialize<List<SceneJsonItem>>(json);
                if (items == null || items.Count == 0) return;

                // Merge into existing scenes or create new ones
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    Scene scene;
                    if (i < _viewModel.Project.Scenes.Count)
                    {
                        scene = _viewModel.Project.Scenes[i];
                    }
                    else
                    {
                        scene = new Scene();
                        _viewModel.Project.Scenes.Add(scene);
                    }

                    if (!string.IsNullOrEmpty(item.Title))
                        scene.Title = item.Title;
                    if (!string.IsNullOrEmpty(item.Narration))
                    {
                        scene.NarrationText = item.Narration;
                        scene.SubtitleText = item.Narration;
                    }
                    if (!string.IsNullOrEmpty(item.ImagePrompt))
                    {
                        scene.AIGeneration ??= new AIGenerationSettings();
                        scene.AIGeneration.ImageDescription = item.ImagePrompt;
                        scene.AIGeneration.GenerateImage = true;
                    }
                }

                _viewModel.RefreshSceneList();
                UpdateSceneListVisibility();
                ImageGenStatusText.Text = LocalizationService.GetString("AiFlow.JsonImported", items.Count);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    LocalizationService.GetString("Common.ErrorWithMessage", ex.Message),
                    LocalizationService.GetString("Common.Error"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private class SceneJsonItem
        {
            [JsonPropertyName("index")]
            public int Index { get; set; }

            [JsonPropertyName("title")]
            public string Title { get; set; } = "";

            [JsonPropertyName("narration")]
            public string Narration { get; set; } = "";

            [JsonPropertyName("image_prompt")]
            public string ImagePrompt { get; set; } = "";
        }

        // ── サムネイルクリエーター ──

        private void OpenThumbnailCreator_Click(object sender, RoutedEventArgs e)
        {
            TitleCreatorPopOutRequested?.Invoke();
        }

        // ── Helpers ──

        private void UpdateSceneListVisibility()
        {
            if (_viewModel == null) return;
            var hasScenes = _viewModel.Project.Scenes.Count > 0;
            SceneListCard.Visibility = hasScenes ? Visibility.Visible : Visibility.Collapsed;
            EmptyState.Visibility = hasScenes ? Visibility.Collapsed : Visibility.Visible;
            if (hasScenes)
            {
                SceneCountText.Text = LocalizationService.GetString("AiWorkspace.SceneCount",
                    _viewModel.Project.Scenes.Count);
            }
        }

        private async Task EnsureOpenAIConfigured()
        {
            if (_openAIService == null)
                _openAIService = new OpenAIService();

            if (!_openAIService.IsConfigured && _config != null)
            {
                var apiKey = ApiKeyManager.GetApiKey(_config);
                if (!string.IsNullOrEmpty(apiKey))
                    await _openAIService.ConfigureAsync(apiKey);
            }
        }
    }
}

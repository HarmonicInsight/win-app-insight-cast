using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using InsightCast.ViewModels;
using InsightCommon.AI;

namespace InsightCast.Views
{
    public partial class TitleCreatorDialog : Window
    {
        private PlanningViewModel? _viewModel;
        private AiImageService? _imageService;
        private CancellationTokenSource? _aiCts;

        /// <summary>
        /// Event raised when user wants to add the generated image to the current scene.
        /// The string parameter is the path to the generated image file.
        /// </summary>
        public event Action<string>? AddToSceneRequested;

        public TitleCreatorDialog()
        {
            InitializeComponent();
            InitializeAiImagePanel();
        }

        private void InitializeAiImagePanel()
        {
            var models = AiImageService.AvailableModels;
            foreach (var m in models)
                AiModelCombo.Items.Add(new ComboBoxItem { Content = m.DisplayName, Tag = m.Id });
            if (AiModelCombo.Items.Count > 0)
                AiModelCombo.SelectedIndex = 0;
        }

        /// <summary>
        /// Set the ViewModel for this dialog. Call this after construction.
        /// </summary>
        public void SetViewModel(PlanningViewModel viewModel)
        {
            _viewModel = viewModel;
            DataContext = viewModel;
        }

        private void MainColorPalette_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is int index && _viewModel != null)
            {
                _viewModel.SelectedMainColorIndex = index;
                MainColorToggle.IsChecked = false;
            }
        }

        private void SubColorPalette_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is int index && _viewModel != null)
            {
                _viewModel.SelectedSubColorIndex = index;
                SubColorToggle.IsChecked = false;
            }
        }

        private void SubSubColorPalette_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is int index && _viewModel != null)
            {
                _viewModel.SelectedSubSubColorIndex = index;
                SubSubColorToggle.IsChecked = false;
            }
        }

        private void BgColorPalette_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is int index && _viewModel != null)
            {
                _viewModel.SelectedBgColorIndex = index;
                BgColorToggle.IsChecked = false;
            }
        }

        private void AddToScene_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null) return;

            // Generate thumbnail first if not already generated
            _viewModel.GenerateThumbnailCommand.Execute(null);

            // Get the last generated thumbnail path from temp folder
            var tempDir = Path.Combine(Path.GetTempPath(), "InsightCast", "Thumbnails");
            if (!Directory.Exists(tempDir)) return;

            var files = Directory.GetFiles(tempDir, "thumb_*.png");
            if (files.Length == 0) return;

            // Get the most recent file
            var latestFile = files[0];
            var latestTime = File.GetLastWriteTime(latestFile);
            foreach (var file in files)
            {
                var time = File.GetLastWriteTime(file);
                if (time > latestTime)
                {
                    latestTime = time;
                    latestFile = file;
                }
            }

            // Fire event to add to scene
            AddToSceneRequested?.Invoke(latestFile);
        }

        private void AiImageBtn_Click(object sender, RoutedEventArgs e)
        {
            AiImagePanel.Visibility = AiImagePanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private async void AiGenerate_Click(object sender, RoutedEventArgs e)
        {
            var prompt = AiPromptBox.Text?.Trim();
            if (string.IsNullOrEmpty(prompt))
            {
                AiStatusText.Text = "プロンプトを入力してください。";
                return;
            }

            var selectedModel = AiModelCombo.SelectedItem as ComboBoxItem;
            var modelId = selectedModel?.Tag as string ?? "dall-e-3";

            var selectedSize = AiSizeCombo.SelectedItem as ComboBoxItem;
            var size = selectedSize?.Tag as string ?? "1280x720";

            // Load config (reuses API keys from AiSettingsDialog)
            var config = AiProviderConfig.Load("INMV");
            _imageService ??= new AiImageService(config);

            AiGenerateBtn.IsEnabled = false;
            AiStatusText.Text = "生成中...";
            _aiCts?.Cancel();
            _aiCts = new CancellationTokenSource();

            try
            {
                var result = await _imageService.GenerateImageAsync(new AiImageRequest
                {
                    Prompt = prompt,
                    ModelId = modelId,
                    Size = size,
                    Quality = "standard",
                }, _aiCts.Token);

                if (result.Success && !string.IsNullOrEmpty(result.ImagePath))
                {
                    // Set as thumbnail background
                    if (_viewModel != null)
                    {
                        _viewModel.SetThumbnailBackground(result.ImagePath);
                        AiStatusText.Text = "背景に適用しました。";
                    }
                    else
                    {
                        AiStatusText.Text = $"生成完了: {result.ImagePath}";
                    }
                }
                else
                {
                    AiStatusText.Text = result.ErrorMessage ?? "生成に失敗しました。";
                }
            }
            catch (OperationCanceledException)
            {
                AiStatusText.Text = "キャンセルされました。";
            }
            catch (Exception ex)
            {
                AiStatusText.Text = $"エラー: {ex.Message}";
            }
            finally
            {
                AiGenerateBtn.IsEnabled = true;
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

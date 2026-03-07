using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using InsightCast.Models;
using InsightCast.Services;
using InsightCast.Services.Claude;

namespace InsightCast.Views
{
    public partial class AiChapterDialog : Window
    {
        private readonly IClaudeService _claudeService;
        private readonly string _referenceContext;
        private ChapterStructure? _generatedChapters;
        private CancellationTokenSource? _cts;

        public ChapterStructure? Result { get; private set; }

        public AiChapterDialog(IClaudeService claudeService, string referenceContext, int materialCount)
        {
            InitializeComponent();
            _claudeService = claudeService;
            _referenceContext = referenceContext;
            MaterialCountText.Text = $"{materialCount} files";
        }

        private async void Generate_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            var chapterCountItem = ChapterCountCombo.SelectedItem as ComboBoxItem;
            if (chapterCountItem == null) return;
            var chapterCount = int.Parse(chapterCountItem.Content.ToString()!);

            GenerateButton.IsEnabled = false;
            ApplyButton.IsEnabled = false;
            ProgressBar.Visibility = Visibility.Visible;
            StatusText.Text = LocalizationService.GetString("AiChapter.Generating");

            try
            {
                var service = new ChapterGenerationService(_claudeService);
                _generatedChapters = await service.GenerateChaptersAsync(
                    _referenceContext,
                    chapterCount,
                    InstructionsBox.Text,
                    _cts.Token);

                DisplayChapters(_generatedChapters);
                ApplyButton.IsEnabled = true;
                StatusText.Text = LocalizationService.GetString("AiChapter.Generated", _generatedChapters.Chapters.Count);
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = LocalizationService.GetString("AiChapter.Cancelled");
            }
            catch (Exception ex)
            {
                StatusText.Text = LocalizationService.GetString("Common.ErrorWithMessage", ex.Message);
                MessageBox.Show(ex.Message, LocalizationService.GetString("Common.Error"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                GenerateButton.IsEnabled = true;
                ProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        private void DisplayChapters(ChapterStructure chapters)
        {
            ChapterPreviewPanel.Children.Clear();

            // Video title
            var titleBlock = new TextBlock
            {
                Text = chapters.VideoTitle,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 12),
                TextWrapping = TextWrapping.Wrap
            };
            ChapterPreviewPanel.Children.Add(titleBlock);

            for (int i = 0; i < chapters.Chapters.Count; i++)
            {
                var ch = chapters.Chapters[i];
                var border = new Border
                {
                    BorderBrush = (System.Windows.Media.Brush)FindResource("BorderDefault"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(10),
                    Margin = new Thickness(0, 0, 0, 6)
                };

                var stack = new StackPanel();

                // Chapter header
                var header = new TextBlock
                {
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 4),
                    TextWrapping = TextWrapping.Wrap
                };
                header.Inlines.Add(new System.Windows.Documents.Run($"#{i + 1} ")
                {
                    Foreground = (System.Windows.Media.Brush)FindResource("AccentGold")
                });
                header.Inlines.Add(new System.Windows.Documents.Run(ch.Title));
                stack.Children.Add(header);

                // Narration
                stack.Children.Add(new TextBlock
                {
                    Text = ch.Narration,
                    FontSize = 11.5,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary"),
                    Margin = new Thickness(0, 0, 0, 4)
                });

                // Image description
                if (!string.IsNullOrWhiteSpace(ch.ImageDescription))
                {
                    stack.Children.Add(new TextBlock
                    {
                        Text = $"[Image] {ch.ImageDescription}",
                        FontSize = 10,
                        FontStyle = FontStyles.Italic,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = (System.Windows.Media.Brush)FindResource("TextTertiary")
                    });
                }

                border.Child = stack;
                ChapterPreviewPanel.Children.Add(border);
            }
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            Result = _generatedChapters;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            DialogResult = false;
            Close();
        }
    }
}

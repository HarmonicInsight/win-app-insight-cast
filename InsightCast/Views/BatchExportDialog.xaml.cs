using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using InsightCast.Models;
using InsightCast.Services;
using InsightCast.Services.Batch;

namespace InsightCast.Views
{
    public partial class BatchExportDialog : Window
    {
        private readonly IBatchExportService _batchService;
        private readonly int _defaultSpeakerId;
        private readonly Func<Scene, TextStyle> _getStyleForScene;
        private CancellationTokenSource? _cts;

        public ObservableCollection<BatchProjectViewModel> Projects { get; } = new();

        public BatchExportDialog(
            IBatchExportService batchService,
            int defaultSpeakerId,
            Func<Scene, TextStyle> getStyleForScene)
        {
            InitializeComponent();
            _batchService = batchService;
            _defaultSpeakerId = defaultSpeakerId;
            _getStyleForScene = getStyleForScene;
            ProjectListView.ItemsSource = Projects;

            // Default output directory
            OutputDirTextBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = LocalizationService.GetString("BatchExport.AddProjects"),
                Filter = LocalizationService.GetString("JsonImport.Filter"),
                Multiselect = true
            };

            if (dialog.ShowDialog(this) == true)
            {
                foreach (var file in dialog.FileNames)
                {
                    if (!Projects.Any(p => p.FilePath == file))
                    {
                        Projects.Add(new BatchProjectViewModel
                        {
                            FilePath = file,
                            FileName = Path.GetFileName(file),
                            OutputName = Path.ChangeExtension(Path.GetFileName(file), ".mp4"),
                            Status = LocalizationService.GetString("BatchExport.Status.Waiting")
                        });
                    }
                }
            }
        }

        private void RemoveButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = ProjectListView.SelectedItems.Cast<BatchProjectViewModel>().ToList();
            foreach (var item in selected)
            {
                Projects.Remove(item);
            }
        }

        private void BrowseOutputDir_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = LocalizationService.GetString("BatchExport.OutputDirectory"),
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = "フォルダを選択",
                Filter = "Folder|*.folder",
                InitialDirectory = OutputDirTextBox.Text
            };

            // フォルダ選択のためのワークアラウンド
            if (!string.IsNullOrEmpty(OutputDirTextBox.Text) && Directory.Exists(OutputDirTextBox.Text))
            {
                dialog.InitialDirectory = OutputDirTextBox.Text;
            }

            if (dialog.ShowDialog(this) == true)
            {
                var path = Path.GetDirectoryName(dialog.FileName);
                if (!string.IsNullOrEmpty(path))
                {
                    OutputDirTextBox.Text = path;
                }
            }
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (Projects.Count == 0)
            {
                MessageBox.Show(
                    LocalizationService.GetString("BatchExport.NoProjects"),
                    LocalizationService.GetString("Common.Error"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _cts = new CancellationTokenSource();
            StartButton.IsEnabled = false;
            AddButton.IsEnabled = false;
            RemoveButton.IsEnabled = false;
            CancelButton.Content = LocalizationService.GetString("Common.Cancel");

            var config = BuildBatchConfig();
            var progress = new Progress<BatchProgress>(UpdateProgress);

            try
            {
                var result = await _batchService.ExecuteBatchAsync(
                    config, _defaultSpeakerId, _getStyleForScene, progress, _cts.Token);

                ShowResult(result);
            }
            catch (OperationCanceledException)
            {
                ProgressLabel.Text = LocalizationService.GetString("VM.Export.Cancelled");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, LocalizationService.GetString("Common.Error"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                StartButton.IsEnabled = true;
                AddButton.IsEnabled = true;
                RemoveButton.IsEnabled = true;
                CancelButton.Content = LocalizationService.GetString("Common.Close");
                _cts = null;
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null)
            {
                _cts.Cancel();
            }
            else
            {
                DialogResult = true;
                Close();
            }
        }

        private void UpdateProgress(BatchProgress progress)
        {
            Dispatcher.Invoke(() =>
            {
                ProgressBar.Value = progress.OverallProgress;
                ProgressLabel.Text = string.Format(
                    LocalizationService.GetString("BatchExport.Progress"),
                    progress.CurrentProjectIndex + 1, progress.TotalProjects);
                CurrentProjectLabel.Text = progress.CurrentProjectName;

                if (progress.CurrentProjectIndex < Projects.Count)
                {
                    Projects[progress.CurrentProjectIndex].Status =
                        LocalizationService.GetString("BatchExport.Status.Processing");
                }
            });
        }

        private void ShowResult(BatchResult result)
        {
            // Update status for each project
            foreach (var success in result.SuccessfulProjects)
            {
                var project = Projects.FirstOrDefault(p => p.FilePath == success.ProjectFile);
                if (project != null)
                {
                    project.Status = LocalizationService.GetString("BatchExport.Status.Success");
                }
            }

            foreach (var failed in result.FailedProjects)
            {
                var project = Projects.FirstOrDefault(p => p.FilePath == failed.ProjectFile);
                if (project != null)
                {
                    project.Status = $"{LocalizationService.GetString("BatchExport.Status.Failed")}: {failed.ErrorMessage}";
                }
            }

            var message = string.Format(
                LocalizationService.GetString("BatchExport.Complete"),
                result.SuccessCount, result.FailCount,
                result.Duration.ToString(@"hh\:mm\:ss"));

            MessageBox.Show(message, LocalizationService.GetString("BatchExport.Title"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private BatchConfig BuildBatchConfig()
        {
            return new BatchConfig
            {
                BatchName = $"Batch_{DateTime.Now:yyyyMMdd_HHmmss}",
                GlobalSettings = new BatchGlobalSettings
                {
                    OutputDirectory = OutputDirTextBox.Text,
                    ContinueOnError = ContinueOnErrorCheckBox.IsChecked == true
                },
                Projects = Projects.Select(p => new BatchProjectItem
                {
                    ProjectFile = p.FilePath,
                    OutputName = p.OutputName
                }).ToList()
            };
        }
    }

    public class BatchProjectViewModel : INotifyPropertyChanged
    {
        private string _status = string.Empty;

        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string OutputName { get; set; } = string.Empty;

        public string Status
        {
            get => _status;
            set
            {
                _status = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}

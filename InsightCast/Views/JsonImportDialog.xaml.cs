using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using InsightCast.Models;
using InsightCast.Services;
using InsightCast.Services.Batch;

namespace InsightCast.Views
{
    public partial class JsonImportDialog : Window
    {
        private readonly IBatchExportService _batchService;
        private Project? _importedProject;
        private string? _selectedPath;

        public Project? ImportedProject => _importedProject;

        public JsonImportDialog(IBatchExportService batchService)
        {
            InitializeComponent();
            _batchService = batchService;
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = LocalizationService.GetString("JsonImport.Title"),
                Filter = LocalizationService.GetString("JsonImport.Filter")
            };

            if (dialog.ShowDialog(this) == true)
            {
                _selectedPath = dialog.FileName;
                FilePathLabel.Text = dialog.FileName;
                LoadPreview(dialog.FileName);
            }
        }

        private void LoadPreview(string path)
        {
            ErrorLabel.Text = string.Empty;
            ProjectNameLabel.Text = string.Empty;
            SceneCountLabel.Text = string.Empty;
            ResolutionLabel.Text = string.Empty;
            ImportButton.IsEnabled = false;
            _importedProject = null;

            try
            {
                _importedProject = _batchService.ImportProjectFromJson(path);

                ProjectNameLabel.Text = Path.GetFileNameWithoutExtension(path);
                SceneCountLabel.Text = _importedProject.Scenes.Count.ToString();
                ResolutionLabel.Text = _importedProject.Output.Resolution;
                ImportButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                ErrorLabel.Text = ex.Message;
            }
        }

        private void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            if (_importedProject != null)
            {
                _importedProject.ProjectPath = null; // Mark as new project
                DialogResult = true;
                Close();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using InsightCast.ViewModels;

namespace InsightCast.Views
{
    public partial class TitleCreatorDialog : Window
    {
        private PlanningViewModel? _viewModel;

        /// <summary>
        /// Event raised when user wants to add the generated image to the current scene.
        /// The string parameter is the path to the generated image file.
        /// </summary>
        public event Action<string>? AddToSceneRequested;

        public TitleCreatorDialog()
        {
            InitializeComponent();
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

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

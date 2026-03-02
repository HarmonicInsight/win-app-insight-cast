using System;
using System.Windows;
using System.Windows.Controls;
using InsightCast.Core;
using InsightCast.Models;
using InsightCast.Services;
using InsightCast.ViewModels;
using Microsoft.Win32;

namespace InsightCast.Views
{
    public partial class PlanningTab : UserControl
    {
        private PlanningViewModel? _viewModel;

        public PlanningTab()
        {
            InitializeComponent();
        }

        public void Initialize(Config config, Project project)
        {
            _viewModel = new PlanningViewModel(config, project);
            DataContext = _viewModel;
        }

        public void RefreshScenes()
        {
            _viewModel?.RefreshSceneList();
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

        private void MainColorPalette_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is int index && _viewModel != null)
            {
                _viewModel.SelectedMainColorIndex = index;
                MainColorToggle.IsChecked = false;
            }
        }

        private void SubColorPalette_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is int index && _viewModel != null)
            {
                _viewModel.SelectedSubColorIndex = index;
                SubColorToggle.IsChecked = false;
            }
        }

        private void SubSubColorPalette_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is int index && _viewModel != null)
            {
                _viewModel.SelectedSubSubColorIndex = index;
                SubSubColorToggle.IsChecked = false;
            }
        }

        private void BgColorPalette_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is int index && _viewModel != null)
            {
                _viewModel.SelectedBgColorIndex = index;
                BgColorToggle.IsChecked = false;
            }
        }
    }
}

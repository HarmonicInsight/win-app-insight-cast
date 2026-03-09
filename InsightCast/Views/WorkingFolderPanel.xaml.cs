using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using InsightCast.Models;
using InsightCast.ViewModels;
using Microsoft.Win32;

namespace InsightCast.Views
{
    public partial class WorkingFolderPanel : UserControl
    {
        private WorkingFolderViewModel? _vm;

        public WorkingFolderPanel()
        {
            InitializeComponent();
        }

        public void Initialize(WorkingFolderViewModel viewModel)
        {
            _vm = viewModel;
            DataContext = _vm;
            UpdateDropZoneVisibility();
            _vm.RootNodes.CollectionChanged += (_, _) => UpdateDropZoneVisibility();
        }

        private void UpdateDropZoneVisibility()
        {
            var empty = _vm?.RootNodes.Count == 0;
            DropZone.Visibility = empty == true ? Visibility.Visible : Visibility.Collapsed;
            FilterBar.Visibility = empty == true ? Visibility.Collapsed : Visibility.Visible;
        }

        // ── Drag & Drop ────────────────────────────────

        private void OnDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private async void OnDrop(object sender, DragEventArgs e)
        {
            if (_vm == null) return;
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

            var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (paths == null) return;

            foreach (var path in paths)
            {
                if (Directory.Exists(path))
                    await _vm.AddFolderAsync(null, path);
                else if (File.Exists(path))
                    await _vm.AddFilesAsync(null, new[] { path });
            }
        }

        // ── Empty state click ────────────────────────────

        private async void DropZone_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_vm == null) return;

            var dlg = new OpenFileDialog
            {
                Multiselect = true,
                Filter = Services.LocalizationService.GetString("WorkFolder.FileFilter"),
                Title = Services.LocalizationService.GetString("WorkFolder.AddFile")
            };

            if (dlg.ShowDialog() == true)
                await _vm.AddFilesAsync(null, dlg.FileNames);
        }

        // ── Public triggers (called from Ribbon) ────────

        public void TriggerAddFile() => AddFile_Click(this, new RoutedEventArgs());

        public void TriggerAddFolder() => AddFolder_Click(this, new RoutedEventArgs());

        // ── Toolbar buttons ────────────────────────────

        private void NewFolder_Click(object sender, RoutedEventArgs e)
        {
            if (_vm == null) return;

            var name = PromptInput(
                Services.LocalizationService.GetString("WorkFolder.NewFolder"),
                Services.LocalizationService.GetString("WorkFolder.FolderName"),
                Services.LocalizationService.GetString("WorkFolder.DefaultFolderName"));
            if (name != null)
                _vm.CreateFolder(null, name);
        }

        private async void AddFile_Click(object sender, RoutedEventArgs e)
        {
            if (_vm == null) return;

            var dlg = new OpenFileDialog
            {
                Multiselect = true,
                Filter = Services.LocalizationService.GetString("WorkFolder.FileFilter"),
                Title = Services.LocalizationService.GetString("WorkFolder.AddFile")
            };

            if (dlg.ShowDialog() == true)
                await _vm.AddFilesAsync(null, dlg.FileNames);
        }

        private async void AddFolder_Click(object sender, RoutedEventArgs e)
        {
            if (_vm == null) return;

            var folderPath = ShowFolderPicker();
            if (folderPath != null)
                await _vm.AddFolderAsync(null, folderPath);
        }

        // ── Context menu handlers ──────────────────────

        private WorkingFolderTreeNode? GetNodeFromSender(object sender)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is WorkingFolderTreeNode node)
                return node;
            if (sender is FrameworkElement element && element.DataContext is WorkingFolderTreeNode node2)
                return node2;
            return null;
        }

        private void ContextNewFolder_Click(object sender, RoutedEventArgs e)
        {
            if (_vm == null) return;
            var parent = GetNodeFromSender(sender);
            if (parent == null || !parent.IsFolder) return;

            var name = PromptInput(
                Services.LocalizationService.GetString("WorkFolder.NewFolder"),
                Services.LocalizationService.GetString("WorkFolder.FolderName"),
                Services.LocalizationService.GetString("WorkFolder.DefaultFolderName"));
            if (name != null)
                _vm.CreateFolder(parent, name);
        }

        private async void ContextAddFile_Click(object sender, RoutedEventArgs e)
        {
            if (_vm == null) return;
            var parent = GetNodeFromSender(sender);
            if (parent != null && !parent.IsFolder) parent = parent.Parent;

            var dlg = new OpenFileDialog
            {
                Multiselect = true,
                Filter = Services.LocalizationService.GetString("WorkFolder.FileFilter"),
                Title = Services.LocalizationService.GetString("WorkFolder.AddFile")
            };

            if (dlg.ShowDialog() == true)
                await _vm.AddFilesAsync(parent, dlg.FileNames);
        }

        private async void ContextAddFolder_Click(object sender, RoutedEventArgs e)
        {
            if (_vm == null) return;
            var parent = GetNodeFromSender(sender);
            if (parent != null && !parent.IsFolder) parent = parent.Parent;

            var folderPath = ShowFolderPicker();
            if (folderPath != null)
                await _vm.AddFolderAsync(parent, folderPath);
        }

        private void ContextRename_Click(object sender, RoutedEventArgs e)
        {
            if (_vm == null) return;
            var node = GetNodeFromSender(sender);
            if (node == null) return;

            var newName = PromptInput(
                Services.LocalizationService.GetString("WorkFolder.Rename"),
                Services.LocalizationService.GetString("WorkFolder.NewName"),
                node.Name);
            if (newName != null && newName != node.Name)
                _vm.RenameNode(node, newName);
        }

        private void ContextDelete_Click(object sender, RoutedEventArgs e)
        {
            if (_vm == null) return;
            var node = GetNodeFromSender(sender);
            if (node == null) return;

            var result = MessageBox.Show(
                Services.LocalizationService.GetString("WorkFolder.DeleteConfirm", node.Name),
                Services.LocalizationService.GetString("Common.Delete"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
                _vm.DeleteNode(node);
        }

        private void ContextOpen_Click(object sender, RoutedEventArgs e)
        {
            var node = GetNodeFromSender(sender);
            if (node == null || node.IsFolder) return;

            try
            {
                Process.Start(new ProcessStartInfo(node.FullPath) { UseShellExecute = true });
            }
            catch { }
        }

        private void InlineDelete_Click(object sender, RoutedEventArgs e)
        {
            if (_vm == null) return;
            var node = GetNodeFromSender(sender);
            if (node == null) return;
            _vm.DeleteNode(node);
        }

        // ── Tree events ────────────────────────────────

        private void CheckBox_Changed(object sender, RoutedEventArgs e)
        {
            _vm?.NotifyCheckedChanged();
        }

        private void Tree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (MaterialTreeView.SelectedItem is WorkingFolderTreeNode node && !node.IsFolder)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(node.FullPath) { UseShellExecute = true });
                }
                catch { }
            }
        }

        // ── Filter ─────────────────────────────────────

        private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _vm?.ApplyFilter(FilterBox.Text);
        }

        private void ClearFilter_Click(object sender, RoutedEventArgs e)
        {
            FilterBox.Text = "";
        }

        // ── Expand/Collapse ────────────────────────────

        private void ExpandAll_Click(object sender, RoutedEventArgs e)
        {
            SetTreeExpansion(MaterialTreeView, true);
        }

        private void CollapseAll_Click(object sender, RoutedEventArgs e)
        {
            SetTreeExpansion(MaterialTreeView, false);
        }

        private static void SetTreeExpansion(ItemsControl control, bool expand)
        {
            foreach (var item in control.Items)
            {
                var container = control.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
                if (container == null) continue;
                container.IsExpanded = expand;
                SetTreeExpansion(container, expand);
            }
        }

        // ── Helpers ────────────────────────────────────

        private static string? ShowFolderPicker()
        {
            var dlg = new OpenFileDialog
            {
                Title = Services.LocalizationService.GetString("WorkFolder.AddFolder"),
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = "Folder Selection",
                Filter = "Folders|\n",
                ValidateNames = false
            };

            if (dlg.ShowDialog() != true) return null;

            var path = Path.GetDirectoryName(dlg.FileName);
            return Directory.Exists(path) ? path : null;
        }

        private static string? PromptInput(string title, string prompt, string defaultValue)
        {
            var dlg = new Window
            {
                Title = title,
                Width = 350,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Owner = Application.Current.MainWindow
            };

            var textBox = new TextBox { Text = defaultValue, Margin = new Thickness(12), VerticalAlignment = VerticalAlignment.Center };
            var okBtn = new Button { Content = "OK", Width = 80, Height = 28, IsDefault = true, Margin = new Thickness(4) };
            var cancelBtn = new Button { Content = Services.LocalizationService.GetString("Common.Cancel"), Width = 80, Height = 28, IsCancel = true, Margin = new Thickness(4) };

            string? result = null;
            okBtn.Click += (_, _) => { result = textBox.Text; dlg.Close(); };
            cancelBtn.Click += (_, _) => dlg.Close();

            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12, 0, 12, 8) };
            buttonPanel.Children.Add(okBtn);
            buttonPanel.Children.Add(cancelBtn);

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = prompt, Margin = new Thickness(12, 12, 12, 4), FontSize = 12 });
            stack.Children.Add(textBox);
            stack.Children.Add(buttonPanel);

            dlg.Content = stack;
            textBox.SelectAll();
            textBox.Focus();
            dlg.ShowDialog();
            return result;
        }
    }
}

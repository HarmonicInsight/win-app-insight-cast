using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using InsightCast.Core;
using InsightCast.Models;
using InsightCast.Views;
using InsightCommon.License;
using InsightCommon.UI;
using InsightCommon.Theme;

namespace InsightCast.Services
{
    public class DialogService : IDialogService
    {
        private readonly Window _owner;

        public DialogService(Window owner)
        {
            _owner = owner;
        }

        public string? ShowOpenFileDialog(string title, string filter, string? defaultExt = null)
        {
            var dlg = new OpenFileDialog { Title = title, Filter = filter };
            if (defaultExt != null) dlg.DefaultExt = defaultExt;
            return dlg.ShowDialog(_owner) == true ? dlg.FileName : null;
        }

        public string[]? ShowOpenFileDialogMultiple(string title, string filter, string? defaultExt = null)
        {
            var dlg = new OpenFileDialog { Title = title, Filter = filter, Multiselect = true };
            if (defaultExt != null) dlg.DefaultExt = defaultExt;
            return dlg.ShowDialog(_owner) == true ? dlg.FileNames : null;
        }

        public string? ShowSaveFileDialog(string title, string filter, string? defaultExt = null, string? fileName = null)
        {
            var dlg = new SaveFileDialog { Title = title, Filter = filter };
            if (defaultExt != null) dlg.DefaultExt = defaultExt;
            if (fileName != null) dlg.FileName = fileName;
            return dlg.ShowDialog(_owner) == true ? dlg.FileName : null;
        }

        public bool ShowConfirmation(string message, string title)
        {
            return MessageBox.Show(_owner, message, title,
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        }

        public void ShowInfo(string message, string title)
        {
            MessageBox.Show(_owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public void ShowWarning(string message, string title)
        {
            MessageBox.Show(_owner, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        public void ShowError(string message, string title)
        {
            MessageBox.Show(_owner, message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        public bool ShowYesNo(string message, string title)
        {
            return MessageBox.Show(_owner, message, title,
                MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes;
        }

        public BGMSettings? ShowBgmDialog(BGMSettings? currentSettings)
        {
            var dlg = new BGMDialog(currentSettings) { Owner = _owner };
            return dlg.ShowDialog() == true ? dlg.GetSettings() : null;
        }

        public TextStyle? ShowTextStyleDialog(TextStyle? currentStyle)
        {
            var dlg = new TextStyleDialog(currentStyle) { Owner = _owner };
            return dlg.ShowDialog() == true ? dlg.GetSelectedStyle() : null;
        }

        public int ShowListSelectDialog(string title, string[] items)
        {
            var dlg = new Window
            {
                Title = title,
                Width = 380,
                Height = 320,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = _owner,
                ResizeMode = ResizeMode.NoResize
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var listBox = new ListBox { Margin = new Thickness(8) };
            foreach (var item in items)
                listBox.Items.Add(item);
            if (items.Length > 0) listBox.SelectedIndex = 0;
            listBox.MouseDoubleClick += (_, _) => { if (listBox.SelectedIndex >= 0) dlg.DialogResult = true; };
            Grid.SetRow(listBox, 0);
            grid.Children.Add(listBox);

            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(8, 4, 8, 8)
            };
            var okBtn = new Button { Content = "OK", Width = 80, Height = 28, Margin = new Thickness(4, 0, 0, 0), IsDefault = true };
            var cancelBtn = new Button { Content = LocalizationService.GetString("Common.Cancel"), Width = 80, Height = 28, Margin = new Thickness(4, 0, 0, 0), IsCancel = true };
            okBtn.Click += (_, _) => { if (listBox.SelectedIndex >= 0) dlg.DialogResult = true; };
            btnPanel.Children.Add(okBtn);
            btnPanel.Children.Add(cancelBtn);
            Grid.SetRow(btnPanel, 1);
            grid.Children.Add(btnPanel);

            dlg.Content = grid;
            return dlg.ShowDialog() == true ? listBox.SelectedIndex : -1;
        }

        public void ShowLicenseDialog(Config config)
        {
            // InsightCommon 共通ライセンスダイアログを使用
            var licenseManager = new InsightLicenseManager("INMV", "Insight Training Studio");
            var dialog = new InsightLicenseDialog(new LicenseDialogOptions
            {
                ProductCode = "INMV",
                ProductName = "Insight Training Studio",
                ThemeMode = InsightThemeMode.Light,
                Locale = "ja",
                LicenseManager = licenseManager,
                Features = new[]
                {
                    new FeatureDefinition("subtitle", LocalizationService.GetString("Dialog.Feature.Subtitle")),
                    new FeatureDefinition("subtitle_style", LocalizationService.GetString("Dialog.Feature.SubtitleStyle")),
                    new FeatureDefinition("transition", LocalizationService.GetString("Dialog.Feature.Transition")),
                    new FeatureDefinition("pptx_import", LocalizationService.GetString("Dialog.Feature.PptxImport")),
                },
                FeatureMatrix = new Dictionary<string, InsightCommon.License.PlanCode[]>
                {
                    ["subtitle"]       = new[] { InsightCommon.License.PlanCode.Trial, InsightCommon.License.PlanCode.Biz, InsightCommon.License.PlanCode.Ent },
                    ["subtitle_style"] = new[] { InsightCommon.License.PlanCode.Trial, InsightCommon.License.PlanCode.Biz, InsightCommon.License.PlanCode.Ent },
                    ["transition"]     = new[] { InsightCommon.License.PlanCode.Trial, InsightCommon.License.PlanCode.Biz, InsightCommon.License.PlanCode.Ent },
                    ["pptx_import"]    = new[] { InsightCommon.License.PlanCode.Trial, InsightCommon.License.PlanCode.Biz, InsightCommon.License.PlanCode.Ent },
                },
            });
            dialog.Owner = _owner;
            dialog.ShowDialog();

            // 共通ライセンスマネージャーの結果をアプリConfigに同期
            var license = licenseManager.CurrentLicense;
            if (license.IsValid && !string.IsNullOrEmpty(license.Key))
            {
                config.BeginUpdate();
                config.LicenseKey = license.Key;
                config.LicenseEmail = license.Email ?? "";
                config.EndUpdate();
            }
        }

        public (int action, int selectedIndex, string? newName) ShowTemplateDialog(string title, List<ProjectTemplate> templates)
        {
            int resultAction = 0;
            int resultIndex = -1;
            string? resultName = null;

            var dlg = new Window
            {
                Title = title,
                Width = 500,
                Height = 400,
                MinWidth = 400,
                MinHeight = 300,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = _owner,
                ResizeMode = ResizeMode.CanResize
            };

            var mainGrid = new Grid { Margin = new Thickness(12) };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Template list
            var listBox = new ListBox { Margin = new Thickness(0, 0, 0, 8) };
            foreach (var t in templates)
            {
                var item = new StackPanel { Margin = new Thickness(4) };
                item.Children.Add(new TextBlock { Text = t.Name, FontWeight = FontWeights.SemiBold, FontSize = 14 });
                item.Children.Add(new TextBlock { Text = $"{t.CreatedAt:yyyy/MM/dd HH:mm}", FontSize = 11, Foreground = System.Windows.Media.Brushes.Gray });
                if (!string.IsNullOrWhiteSpace(t.Description))
                    item.Children.Add(new TextBlock { Text = t.Description, FontSize = 11, Foreground = System.Windows.Media.Brushes.Gray, TextTrimming = TextTrimming.CharacterEllipsis });
                listBox.Items.Add(new ListBoxItem { Content = item, Tag = t });
            }
            if (templates.Count > 0) listBox.SelectedIndex = 0;
            listBox.MouseDoubleClick += (_, _) => { if (listBox.SelectedIndex >= 0) { resultAction = 1; resultIndex = listBox.SelectedIndex; dlg.DialogResult = true; } };
            Grid.SetRow(listBox, 0);
            mainGrid.Children.Add(listBox);

            // Button panel - use Grid for better layout
            var btnPanel = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            btnPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            btnPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            btnPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            btnPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            btnPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Left: Delete, Rename
            var deleteBtn = new Button { Content = LocalizationService.GetString("Common.Delete"), MinWidth = 70, Height = 32, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0) };
            var renameBtn = new Button { Content = LocalizationService.GetString("Template.Rename"), MinWidth = 70, Height = 32, Padding = new Thickness(12, 4, 12, 4) };
            deleteBtn.Click += (_, _) =>
            {
                if (listBox.SelectedIndex >= 0)
                {
                    resultAction = 2;
                    resultIndex = listBox.SelectedIndex;
                    dlg.DialogResult = true;
                }
            };
            renameBtn.Click += (_, _) =>
            {
                if (listBox.SelectedIndex >= 0)
                {
                    var template = templates[listBox.SelectedIndex];
                    var newName = ShowInputDialog(LocalizationService.GetString("Template.Rename"), LocalizationService.GetString("Template.EnterNewName"), template.Name);
                    if (!string.IsNullOrWhiteSpace(newName))
                    {
                        resultAction = 3;
                        resultIndex = listBox.SelectedIndex;
                        resultName = newName;
                        dlg.DialogResult = true;
                    }
                }
            };
            Grid.SetColumn(deleteBtn, 0);
            Grid.SetColumn(renameBtn, 1);
            btnPanel.Children.Add(deleteBtn);
            btnPanel.Children.Add(renameBtn);

            // Right: Apply, Cancel
            var applyBtn = new Button { Content = LocalizationService.GetString("Template.Apply"), MinWidth = 80, Height = 32, Padding = new Thickness(16, 4, 16, 4), Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            var cancelBtn = new Button { Content = LocalizationService.GetString("Common.Cancel"), MinWidth = 80, Height = 32, Padding = new Thickness(16, 4, 16, 4), IsCancel = true };
            applyBtn.Click += (_, _) =>
            {
                if (listBox.SelectedIndex >= 0)
                {
                    resultAction = 1;
                    resultIndex = listBox.SelectedIndex;
                    dlg.DialogResult = true;
                }
            };
            Grid.SetColumn(applyBtn, 3);
            Grid.SetColumn(cancelBtn, 4);
            btnPanel.Children.Add(applyBtn);
            btnPanel.Children.Add(cancelBtn);

            Grid.SetRow(btnPanel, 1);
            mainGrid.Children.Add(btnPanel);

            dlg.Content = mainGrid;
            dlg.ShowDialog();

            return (resultAction, resultIndex, resultName);
        }

        public string? ShowInputDialog(string title, string prompt, string? defaultValue = null)
        {
            var dlg = new Window
            {
                Title = title,
                Width = 400,
                Height = 160,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = _owner,
                ResizeMode = ResizeMode.NoResize
            };

            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 8) };
            Grid.SetRow(label, 0);
            grid.Children.Add(label);

            var textBox = new TextBox { Text = defaultValue ?? "", Margin = new Thickness(0, 0, 0, 16), Padding = new Thickness(6, 4, 6, 4) };
            Grid.SetRow(textBox, 1);
            grid.Children.Add(textBox);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var okBtn = new Button { Content = "OK", Width = 80, Height = 28, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            var cancelBtn = new Button { Content = LocalizationService.GetString("Common.Cancel"), Width = 80, Height = 28, IsCancel = true };
            okBtn.Click += (_, _) => { dlg.DialogResult = true; };
            btnPanel.Children.Add(okBtn);
            btnPanel.Children.Add(cancelBtn);
            Grid.SetRow(btnPanel, 2);
            grid.Children.Add(btnPanel);

            dlg.Content = grid;
            textBox.Focus();
            textBox.SelectAll();

            return dlg.ShowDialog() == true ? textBox.Text : null;
        }
    }
}

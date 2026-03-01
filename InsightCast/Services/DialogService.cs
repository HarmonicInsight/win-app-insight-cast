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
    }
}

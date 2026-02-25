using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using InsightCast.Core;
using InsightCast.Services;
using InsightCast.Services.OpenAI;

namespace InsightCast.Views
{
    public partial class OpenAISettingsDialog : Window
    {
        private readonly Config _config;
        private readonly IOpenAIService _openAIService;

        public OpenAISettingsDialog(Config config, IOpenAIService openAIService)
        {
            InitializeComponent();
            _config = config;
            _openAIService = openAIService;
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            LoadSettings();
        }

        private void LoadSettings()
        {
            var apiKey = ApiKeyManager.GetApiKey(_config);
            if (!string.IsNullOrEmpty(apiKey))
            {
                ApiKeyBox.Password = apiKey;
                ApiKeyStatus.Text = LocalizationService.GetString("OpenAI.ApiKey.Set");
                ApiKeyStatus.Foreground = new SolidColorBrush(Colors.Green);
            }
            else
            {
                ApiKeyStatus.Text = LocalizationService.GetString("OpenAI.ApiKey.NotSet");
                ApiKeyStatus.Foreground = new SolidColorBrush(Colors.Gray);
            }

            SelectComboItem(NarrationModelCombo, _config.OpenAINarrationModel);
            SelectComboItem(ImageModelCombo, _config.OpenAIImageModel);
        }

        private async void TestButton_Click(object sender, RoutedEventArgs e)
        {
            var apiKey = ApiKeyBox.Password;
            if (string.IsNullOrEmpty(apiKey))
            {
                ConnectionStatus.Text = LocalizationService.GetString("OpenAI.ApiKey.NotSet");
                ConnectionStatus.Foreground = new SolidColorBrush(Colors.Red);
                return;
            }

            TestButton.IsEnabled = false;
            ConnectionStatus.Text = LocalizationService.GetString("OpenAI.Testing");
            ConnectionStatus.Foreground = new SolidColorBrush(Colors.Gray);

            try
            {
                var success = await _openAIService.ConfigureAsync(apiKey);
                if (success)
                {
                    ConnectionStatus.Text = LocalizationService.GetString("OpenAI.Connected");
                    ConnectionStatus.Foreground = new SolidColorBrush(Colors.Green);
                }
                else
                {
                    ConnectionStatus.Text = LocalizationService.GetString("OpenAI.ConnectionFailed");
                    ConnectionStatus.Foreground = new SolidColorBrush(Colors.Red);
                }
            }
            catch (Exception ex)
            {
                ConnectionStatus.Text = $"{LocalizationService.GetString("OpenAI.ConnectionFailed")}: {ex.Message}";
                ConnectionStatus.Foreground = new SolidColorBrush(Colors.Red);
            }
            finally
            {
                TestButton.IsEnabled = true;
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            var apiKey = ApiKeyBox.Password;
            if (!string.IsNullOrEmpty(apiKey))
            {
                ApiKeyManager.SaveApiKey(_config, apiKey);
            }

            _config.OpenAINarrationModel = GetSelectedComboText(NarrationModelCombo, "gpt-4o");
            _config.OpenAIImageModel = GetSelectedComboText(ImageModelCombo, "dall-e-3");

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private static void SelectComboItem(ComboBox combo, string value)
        {
            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (combo.Items[i] is ComboBoxItem item && item.Content?.ToString() == value)
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
            if (combo.Items.Count > 0) combo.SelectedIndex = 0;
        }

        private static string GetSelectedComboText(ComboBox combo, string defaultValue)
        {
            if (combo.SelectedItem is ComboBoxItem item)
                return item.Content?.ToString() ?? defaultValue;
            return defaultValue;
        }
    }
}

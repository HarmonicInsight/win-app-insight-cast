using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using InsightCast.Core;

namespace InsightCast.Views
{
    public partial class LicenseDialog : Window
    {
        private readonly Config _config;
        private LicenseInfo _licenseInfo;

        public LicenseDialog()
        {
            InitializeComponent();
            _config = new Config();
            _licenseInfo = new LicenseInfo { Plan = PlanCode.Free, IsValid = false };
            Loaded += LicenseDialog_Loaded;
        }

        public LicenseDialog(Config config) : this()
        {
            _config = config;
        }

        private void LicenseDialog_Loaded(object sender, RoutedEventArgs e)
        {
            SetupPlaceholder();
            LoadCurrentLicense();
        }

        // ── Placeholder Watermark ───────────────────────────────────────

        private void SetupPlaceholder()
        {
            LicenseKeyTextBox.GotFocus += (s, ev) =>
            {
                if (LicenseKeyTextBox.Foreground is SolidColorBrush brush
                    && brush.Color == Color.FromRgb(0x88, 0x88, 0x88))
                {
                    LicenseKeyTextBox.Text = "";
                    LicenseKeyTextBox.Foreground = new SolidColorBrush(Color.FromRgb(0x1C, 0x19, 0x17));
                }
            };

            LicenseKeyTextBox.LostFocus += (s, ev) =>
            {
                if (string.IsNullOrWhiteSpace(LicenseKeyTextBox.Text))
                {
                    ShowPlaceholder();
                }
            };

            ShowPlaceholder();
        }

        private void ShowPlaceholder()
        {
            LicenseKeyTextBox.Text = "INMV-BIZ-2601-XXXX-XXXX-XXXX";
            LicenseKeyTextBox.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
        }

        private string GetLicenseKeyText()
        {
            if (LicenseKeyTextBox.Foreground is SolidColorBrush brush
                && brush.Color == Color.FromRgb(0x88, 0x88, 0x88))
            {
                return string.Empty;
            }
            return LicenseKeyTextBox.Text?.Trim() ?? string.Empty;
        }

        // ── License Loading ─────────────────────────────────────────────

        private void LoadCurrentLicense()
        {
            var key = _config.LicenseKey;
            var email = _config.LicenseEmail;
            _licenseInfo = License.ValidateLicenseKey(key, email);
            UpdateUI();

            if (!string.IsNullOrEmpty(email))
            {
                EmailTextBox.Text = email;
            }

            if (!string.IsNullOrEmpty(key))
            {
                LicenseKeyTextBox.Text = key;
                LicenseKeyTextBox.Foreground = new SolidColorBrush(Color.FromRgb(0x1C, 0x19, 0x17));
            }
        }

        private void UpdateUI()
        {
            // Plan display
            PlanLabel.Text = License.GetPlanDisplayName(_licenseInfo.Plan);

            // Plan label color based on plan level
            PlanLabel.Foreground = _licenseInfo.Plan switch
            {
                PlanCode.Ent   => new SolidColorBrush(Color.FromRgb(0x7C, 0x3A, 0xED)), // Purple
                PlanCode.Biz   => new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)), // Blue
                PlanCode.Trial => new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)), // Amber
                _ => new SolidColorBrush(Color.FromRgb(0xA8, 0xA2, 0x9E))               // Stone 400
            };

            // Status label
            if (_licenseInfo.IsValid && _licenseInfo.ExpiresAt.HasValue)
            {
                StatusLabel.Text = $"有効期限: {_licenseInfo.ExpiresAt.Value:yyyy年MM月dd日}";
                StatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0));
            }
            else if (!string.IsNullOrEmpty(_licenseInfo.ErrorMessage))
            {
                StatusLabel.Text = _licenseInfo.ErrorMessage;
                StatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
            }
            else
            {
                StatusLabel.Text = "";
            }

            // Features
            UpdateFeatureLabel(FeatureSubtitle, "subtitle", _licenseInfo.Plan);
            UpdateFeatureLabel(FeatureSubtitleStyle, "subtitle_style", _licenseInfo.Plan);
            UpdateFeatureLabel(FeatureTransition, "transition", _licenseInfo.Plan);
            UpdateFeatureLabel(FeaturePptx, "pptx_import", _licenseInfo.Plan);

            // Validation message
            ValidationMessage.Text = "";
        }

        private void UpdateFeatureLabel(TextBlock label, string feature, PlanCode plan)
        {
            bool available = License.CanUseFeature(plan, feature);
            if (available)
            {
                label.Text = "\u25CB\u5229\u7528\u53EF\u80FD"; // ○利用可能
                label.Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
            }
            else
            {
                label.Text = "\u00D7TRIAL\u30FBBIZ\u4EE5\u4E0A\u304C\u5FC5\u8981"; // ×TRIAL・BIZ以上が必要
                label.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
            }
        }

        // ── Event Handlers ──────────────────────────────────────────────

        private void ActivateButton_Click(object sender, RoutedEventArgs e)
        {
            var email = EmailTextBox.Text?.Trim() ?? string.Empty;
            var key = GetLicenseKeyText();

            if (string.IsNullOrEmpty(email))
            {
                ValidationMessage.Text = "メールアドレスを入力してください。";
                ValidationMessage.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
                return;
            }

            if (string.IsNullOrEmpty(key))
            {
                ValidationMessage.Text = "ライセンスキーを入力してください。";
                ValidationMessage.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
                return;
            }

            _licenseInfo = License.ValidateLicenseKey(key, email);

            if (_licenseInfo.IsValid)
            {
                _config.BeginUpdate();
                _config.LicenseKey = key;
                _config.LicenseEmail = email;
                _config.EndUpdate();
                ValidationMessage.Text = "ライセンスが正常にアクティベートされました。";
                ValidationMessage.Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
            }
            else
            {
                ValidationMessage.Text = _licenseInfo.ErrorMessage ?? "ライセンスキーが無効です。";
                ValidationMessage.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
            }

            UpdateUI();
            // Preserve validation message after UpdateUI clears it
            if (_licenseInfo.IsValid)
            {
                ValidationMessage.Text = "ライセンスが正常にアクティベートされました。";
                ValidationMessage.Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
            }
            else
            {
                ValidationMessage.Text = _licenseInfo.ErrorMessage ?? "ライセンスキーが無効です。";
                ValidationMessage.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            _config.ClearLicense();
            _licenseInfo = new LicenseInfo { Plan = PlanCode.Free, IsValid = false };
            EmailTextBox.Text = "";
            ShowPlaceholder();
            UpdateUI();
            ValidationMessage.Text = "ライセンスがクリアされました。";
            ValidationMessage.Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0));
        }

        private void TermsLink_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://harmonic-insight.com/ja/terms") { UseShellExecute = true });
        }

        private void PrivacyLink_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://harmonic-insight.com/ja/privacy") { UseShellExecute = true });
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

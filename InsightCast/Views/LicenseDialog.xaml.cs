using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
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

        // ── TitleBar ───────────────────────────────────────────────────────

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1)
                DragMove();
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
            PlanText.Text = License.GetPlanDisplayName(_licenseInfo.Plan);

            // Plan label color based on plan level
            PlanText.Foreground = _licenseInfo.Plan switch
            {
                PlanCode.Ent   => new SolidColorBrush(Color.FromRgb(0x7C, 0x3A, 0xED)), // Purple
                PlanCode.Biz   => new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)), // Blue
                PlanCode.Trial => new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)), // Amber
                _ => (SolidColorBrush)FindResource("BrandPrimary")                      // Gold
            };

            // Expiry label
            if (_licenseInfo.IsValid && _licenseInfo.ExpiresAt.HasValue)
            {
                ExpiryText.Text = $"有効期限: {_licenseInfo.ExpiresAt.Value:yyyy年MM月dd日}";
            }
            else
            {
                ExpiryText.Text = "";
            }

            // Update feature status icons based on plan
            bool isTrial = _licenseInfo.Plan == PlanCode.Trial;
            bool isBiz = _licenseInfo.Plan == PlanCode.Biz;
            bool isEnt = _licenseInfo.Plan == PlanCode.Ent;
            bool hasTrialFeatures = isTrial || isBiz || isEnt;

            // TRIAL/BIZ features (subtitle, transition, pptx)
            Feature1Status.Text = hasTrialFeatures ? "✅" : "🔒 TRIAL+";
            Feature2Status.Text = hasTrialFeatures ? "✅" : "🔒 TRIAL+";
            Feature3Status.Text = hasTrialFeatures ? "✅" : "🔒 TRIAL+";

            // ENT features (AI assistant, VRM, 4K)
            Feature4Status.Text = isEnt ? "✅" : "🔒 ENT";
            Feature5Status.Text = isEnt ? "✅" : "🔒 ENT";
            Feature6Status.Text = isEnt ? "✅" : "🔒 ENT";

            // Status message
            StatusMessage.Text = "";
        }

        // ── Event Handlers ──────────────────────────────────────────────

        private void ActivateButton_Click(object sender, RoutedEventArgs e)
        {
            var email = EmailTextBox.Text?.Trim() ?? string.Empty;
            var key = GetLicenseKeyText();

            if (string.IsNullOrEmpty(email))
            {
                StatusMessage.Text = "メールアドレスを入力してください。";
                StatusMessage.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
                return;
            }

            if (string.IsNullOrEmpty(key))
            {
                StatusMessage.Text = "ライセンスキーを入力してください。";
                StatusMessage.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
                return;
            }

            _licenseInfo = License.ValidateLicenseKey(key, email);

            if (_licenseInfo.IsValid)
            {
                _config.BeginUpdate();
                _config.LicenseKey = key;
                _config.LicenseEmail = email;
                _config.EndUpdate();
                StatusMessage.Text = "ライセンスが正常にアクティベートされました。";
                StatusMessage.Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
            }
            else
            {
                StatusMessage.Text = _licenseInfo.ErrorMessage ?? "ライセンスキーが無効です。";
                StatusMessage.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
            }

            UpdateUI();

            // Preserve status message after UpdateUI
            if (_licenseInfo.IsValid)
            {
                StatusMessage.Text = "ライセンスが正常にアクティベートされました。";
                StatusMessage.Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
            }
            else
            {
                StatusMessage.Text = _licenseInfo.ErrorMessage ?? "ライセンスキーが無効です。";
                StatusMessage.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            _config.ClearLicense();
            _licenseInfo = new LicenseInfo { Plan = PlanCode.Free, IsValid = false };
            EmailTextBox.Text = "";
            ShowPlaceholder();
            UpdateUI();
            StatusMessage.Text = "ライセンスがクリアされました。";
            StatusMessage.Foreground = (SolidColorBrush)FindResource("TextSecondary");
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

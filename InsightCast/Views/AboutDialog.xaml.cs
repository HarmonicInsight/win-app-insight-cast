using System.Windows;
using System.Windows.Documents;
using System.Windows.Navigation;
using InsightCast.Services;

namespace InsightCast.Views;

public partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();

        var isEn = LocalizationService.CurrentLanguage == "EN";

        Title = isEn ? "About Insight Training Studio" : "Insight Training Studio について";
        DescriptionText.Text = isEn
            ? "Education & Presentation Video Generation Tool"
            : "教育・プレゼンテーション動画生成ツール";
        ThirdPartyHeader.Text = isEn ? "Third-Party Software" : "サードパーティソフトウェア";
        WebsiteButton.Content = isEn ? "Website" : "公式サイト";

        // Set version from assembly
        var version = typeof(AboutDialog).Assembly.GetName().Version;
        if (version != null)
            VersionLabel.Text = $"v{version.Major}.{version.Minor}.{version.Build}";
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            var scheme = e.Uri.Scheme.ToLowerInvariant();
            if (scheme != "http" && scheme != "https" && scheme != "mailto")
                return;

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
        }
        catch { /* Ignore browser launch failure */ }
        e.Handled = true;
    }

    private void WebsiteButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://www.insight-office.com/ja",
                UseShellExecute = true
            });
        }
        catch { /* Ignore browser launch failure */ }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}

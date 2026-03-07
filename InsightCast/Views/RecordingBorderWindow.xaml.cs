using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace InsightCast.Views
{
    /// <summary>
    /// Transparent click-through window that shows a red border around the recording region.
    /// Uses SetWindowPos with physical pixel coordinates and blocks WM_DPICHANGED
    /// to prevent WPF DPI rescaling on mixed-DPI multi-monitor setups.
    /// </summary>
    public partial class RecordingBorderWindow : Window
    {
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private static readonly IntPtr HWND_TOPMOST = new(-1);
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WM_DPICHANGED = 0x02E0;

        private readonly Rectangle _region;

        public RecordingBorderWindow(Rectangle region)
        {
            InitializeComponent();
            _region = region;
            WindowState = WindowState.Normal;
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;

            // Block WM_DPICHANGED to prevent WPF from rescaling
            if (HwndSource.FromVisual(this) is HwndSource hwndSource)
                hwndSource.AddHook(WndProc);

            // Make window click-through and hide from taskbar/alt-tab
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW);

            // Position border fully OUTSIDE the recording region.
            // BorderThickness=3 draws inward, so offset by that + 1px gap to ensure
            // the red line never enters the captured area.
            int offset = 8; // 3px border + 5px safety gap to keep red line out of capture
            SetWindowPos(hwnd, HWND_TOPMOST,
                _region.X - offset, _region.Y - offset,
                _region.Width + offset * 2, _region.Height + offset * 2,
                SWP_SHOWWINDOW);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_DPICHANGED)
            {
                handled = true;
                return IntPtr.Zero;
            }
            return IntPtr.Zero;
        }
    }
}

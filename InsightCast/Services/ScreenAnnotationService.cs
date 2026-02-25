using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Automation;

namespace InsightCast.Services
{
    public class UIElementInfo
    {
        public string Name { get; set; } = string.Empty;
        public string ControlType { get; set; } = string.Empty;
        public Rectangle Bounds { get; set; }
        public string AutomationId { get; set; } = string.Empty;
    }

    public class AnnotatedScreenshot
    {
        public string ImagePath { get; set; } = string.Empty;
        public string ElementName { get; set; } = string.Empty;
        public Rectangle HighlightBounds { get; set; }
        public string SuggestedNarration { get; set; } = string.Empty;
    }

    public enum AnnotationStyle
    {
        Circle,
        Rectangle,
        Arrow,
        Highlight
    }

    public class ScreenAnnotationService
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        private readonly string _outputDirectory;

        public ScreenAnnotationService(string? outputDirectory = null)
        {
            _outputDirectory = outputDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "InsightCast", "TutorialCaptures");
            Directory.CreateDirectory(_outputDirectory);
        }

        /// <summary>
        /// Get all UI elements from a window
        /// </summary>
        public List<UIElementInfo> GetUIElements(string windowTitle)
        {
            var elements = new List<UIElementInfo>();

            try
            {
                var window = FindWindowByTitle(windowTitle);
                if (window == null) return elements;

                var walker = TreeWalker.ControlViewWalker;
                CollectElements(window, walker, elements);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting UI elements: {ex.Message}");
            }

            return elements;
        }

        private AutomationElement? FindWindowByTitle(string title)
        {
            var desktop = AutomationElement.RootElement;
            var condition = new PropertyCondition(AutomationElement.NameProperty, title, PropertyConditionFlags.IgnoreCase);
            return desktop.FindFirst(TreeScope.Children, condition);
        }

        private void CollectElements(AutomationElement element, TreeWalker walker, List<UIElementInfo> elements, int depth = 0)
        {
            if (depth > 10) return; // Prevent infinite recursion

            try
            {
                var name = element.Current.Name;
                var controlType = element.Current.ControlType.ProgrammaticName;
                var bounds = element.Current.BoundingRectangle;
                var automationId = element.Current.AutomationId;

                // Only add interactive elements with names
                if (!string.IsNullOrEmpty(name) && bounds.Width > 0 && bounds.Height > 0)
                {
                    var interactiveTypes = new[] { "Button", "MenuItem", "ComboBox", "TextBox", "CheckBox", "RadioButton", "Tab", "ListItem" };
                    if (interactiveTypes.Any(t => controlType.Contains(t)))
                    {
                        elements.Add(new UIElementInfo
                        {
                            Name = name,
                            ControlType = controlType.Replace("ControlType.", ""),
                            Bounds = new Rectangle(
                                (int)bounds.X, (int)bounds.Y,
                                (int)bounds.Width, (int)bounds.Height),
                            AutomationId = automationId
                        });
                    }
                }

                // Recurse into children
                var child = walker.GetFirstChild(element);
                while (child != null)
                {
                    CollectElements(child, walker, elements, depth + 1);
                    child = walker.GetNextSibling(child);
                }
            }
            catch
            {
                // Skip elements that can't be accessed
            }
        }

        /// <summary>
        /// Find a UI element by name pattern
        /// </summary>
        public UIElementInfo? FindElement(string windowTitle, string namePattern)
        {
            var elements = GetUIElements(windowTitle);
            var regex = new Regex(namePattern, RegexOptions.IgnoreCase);
            return elements.FirstOrDefault(e => regex.IsMatch(e.Name));
        }

        /// <summary>
        /// Capture a screenshot of a window
        /// </summary>
        public Bitmap? CaptureWindow(string windowTitle)
        {
            try
            {
                var hwnd = FindWindow(null, windowTitle);
                if (hwnd == IntPtr.Zero)
                {
                    // Try partial match
                    hwnd = FindWindowByPartialTitle(windowTitle);
                }

                if (hwnd == IntPtr.Zero) return null;

                SetForegroundWindow(hwnd);
                System.Threading.Thread.Sleep(100); // Wait for window to come to front

                if (!GetWindowRect(hwnd, out RECT rect)) return null;

                var width = rect.Right - rect.Left;
                var height = rect.Bottom - rect.Top;

                var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(width, height));
                }

                return bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error capturing window: {ex.Message}");
                return null;
            }
        }

        private IntPtr FindWindowByPartialTitle(string partialTitle)
        {
            IntPtr result = IntPtr.Zero;
            EnumWindows((hwnd, lParam) =>
            {
                var title = GetWindowTitle(hwnd);
                if (title.Contains(partialTitle, StringComparison.OrdinalIgnoreCase))
                {
                    result = hwnd;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return result;
        }

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

        private string GetWindowTitle(IntPtr hwnd)
        {
            var sb = new System.Text.StringBuilder(256);
            GetWindowText(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        /// <summary>
        /// Draw annotation on a bitmap
        /// </summary>
        public void DrawAnnotation(Bitmap bitmap, Rectangle bounds, AnnotationStyle style, Color? color = null)
        {
            var annotationColor = color ?? Color.FromArgb(220, 255, 100, 100);

            using var graphics = Graphics.FromImage(bitmap);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            // Convert window-relative bounds if needed
            var drawBounds = bounds;

            switch (style)
            {
                case AnnotationStyle.Circle:
                    DrawCircle(graphics, drawBounds, annotationColor);
                    break;
                case AnnotationStyle.Rectangle:
                    DrawRectangle(graphics, drawBounds, annotationColor);
                    break;
                case AnnotationStyle.Arrow:
                    DrawArrow(graphics, drawBounds, annotationColor);
                    break;
                case AnnotationStyle.Highlight:
                    DrawHighlight(graphics, drawBounds, annotationColor);
                    break;
            }
        }

        private void DrawCircle(Graphics g, Rectangle bounds, Color color)
        {
            // Expand bounds for circle
            var padding = 10;
            var circleBounds = new Rectangle(
                bounds.X - padding,
                bounds.Y - padding,
                bounds.Width + padding * 2,
                bounds.Height + padding * 2);

            using var pen = new Pen(color, 4);
            g.DrawEllipse(pen, circleBounds);
        }

        private void DrawRectangle(Graphics g, Rectangle bounds, Color color)
        {
            var padding = 5;
            var rectBounds = new Rectangle(
                bounds.X - padding,
                bounds.Y - padding,
                bounds.Width + padding * 2,
                bounds.Height + padding * 2);

            using var pen = new Pen(color, 3);
            pen.DashStyle = DashStyle.Solid;
            g.DrawRectangle(pen, rectBounds);
        }

        private void DrawArrow(Graphics g, Rectangle bounds, Color color)
        {
            var arrowStart = new Point(bounds.X - 50, bounds.Y - 30);
            var arrowEnd = new Point(bounds.X - 5, bounds.Y + bounds.Height / 2);

            using var pen = new Pen(color, 4);
            pen.CustomEndCap = new AdjustableArrowCap(5, 5);
            g.DrawLine(pen, arrowStart, arrowEnd);
        }

        private void DrawHighlight(Graphics g, Rectangle bounds, Color color)
        {
            var highlightColor = Color.FromArgb(80, color.R, color.G, color.B);
            using var brush = new SolidBrush(highlightColor);
            g.FillRectangle(brush, bounds);

            using var pen = new Pen(color, 2);
            g.DrawRectangle(pen, bounds);
        }

        /// <summary>
        /// Capture and annotate a specific element
        /// </summary>
        public AnnotatedScreenshot? CaptureAndAnnotate(
            string windowTitle,
            string elementNamePattern,
            AnnotationStyle style = AnnotationStyle.Circle,
            string? customNarration = null)
        {
            var element = FindElement(windowTitle, elementNamePattern);
            if (element == null) return null;

            var screenshot = CaptureWindow(windowTitle);
            if (screenshot == null) return null;

            // Get window position to convert element bounds to window-relative
            var hwnd = FindWindow(null, windowTitle);
            if (hwnd == IntPtr.Zero) hwnd = FindWindowByPartialTitle(windowTitle);

            GetWindowRect(hwnd, out RECT windowRect);

            var relativeBounds = new Rectangle(
                element.Bounds.X - windowRect.Left,
                element.Bounds.Y - windowRect.Top,
                element.Bounds.Width,
                element.Bounds.Height);

            DrawAnnotation(screenshot, relativeBounds, style);

            // Save the image
            var fileName = $"capture_{DateTime.Now:yyyyMMdd_HHmmss}_{SanitizeFileName(element.Name)}.png";
            var filePath = Path.Combine(_outputDirectory, fileName);
            screenshot.Save(filePath, ImageFormat.Png);
            screenshot.Dispose();

            return new AnnotatedScreenshot
            {
                ImagePath = filePath,
                ElementName = element.Name,
                HighlightBounds = element.Bounds,
                SuggestedNarration = customNarration ?? GenerateNarration(element)
            };
        }

        private string GenerateNarration(UIElementInfo element)
        {
            var action = element.ControlType switch
            {
                "Button" => "をクリックします",
                "MenuItem" => "を選択します",
                "ComboBox" => "から選択します",
                "TextBox" => "に入力します",
                "CheckBox" => "をチェックします",
                "RadioButton" => "を選択します",
                "Tab" or "TabItem" => "タブを開きます",
                _ => "を操作します"
            };

            return $"「{element.Name}」{action}。";
        }

        private string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries)).Trim();
        }

        /// <summary>
        /// Capture a series of steps
        /// </summary>
        public List<AnnotatedScreenshot> CaptureSteps(string windowTitle, IEnumerable<string> elementPatterns)
        {
            var results = new List<AnnotatedScreenshot>();
            var stepNumber = 1;

            foreach (var pattern in elementPatterns)
            {
                var result = CaptureAndAnnotate(windowTitle, pattern);
                if (result != null)
                {
                    result.SuggestedNarration = $"ステップ{stepNumber}: {result.SuggestedNarration}";
                    results.Add(result);
                    stepNumber++;
                }

                System.Threading.Thread.Sleep(300); // Small delay between captures
            }

            return results;
        }

        public string OutputDirectory => _outputDirectory;
    }
}

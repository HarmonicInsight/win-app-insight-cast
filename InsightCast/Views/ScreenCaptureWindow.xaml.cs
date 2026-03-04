using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Pen = System.Drawing.Pen;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;
using Size = System.Drawing.Size;

namespace InsightCast.Views
{
    public enum CaptureDrawTool
    {
        None, Rect, Circle, Arrow, Line, Text, Mosaic, Pen
    }

    public partial class ScreenCaptureWindow : Window
    {
        // Full-screen bitmap captured before showing the window
        private Bitmap? _screenBitmap;
        private BitmapSource? _screenSource;

        // Selection state
        private bool _isSelecting;
        private bool _selectionDone;
        private Point _selStart;
        private Point _selEnd;

        // Drawing state
        private CaptureDrawTool _currentTool = CaptureDrawTool.None;
        private System.Drawing.Color _drawColor = System.Drawing.Color.Red;
        private int _drawWidth = 2;
        private bool _isDrawing;
        private Point _drawStart;
        private readonly List<Action<Graphics>> _annotations = new();
        private readonly List<Point> _penPoints = new();

        // Result
        public string? CapturedImagePath { get; private set; }
        public bool CopiedToClipboard { get; private set; }
        public bool PinnedToScene { get; private set; }

        public ScreenCaptureWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        #region Screen Capture

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            CaptureFullScreen();
            SetOverlayBackground();
        }

        private void CaptureFullScreen()
        {
            // Capture all monitors using virtual screen dimensions
            int left = (int)SystemParameters.VirtualScreenLeft;
            int top = (int)SystemParameters.VirtualScreenTop;
            int width = (int)SystemParameters.VirtualScreenWidth;
            int height = (int)SystemParameters.VirtualScreenHeight;

            _screenBitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(_screenBitmap))
            {
                g.CopyFromScreen(left, top, 0, 0, new Size(width, height));
            }

            _screenSource = BitmapToSource(_screenBitmap);
        }

        private void SetOverlayBackground()
        {
            if (_screenSource == null) return;

            var brush = new ImageBrush(_screenSource) { Stretch = Stretch.None };
            // The overlay dims the entire screen; we use the screenshot as background
            // with a dark semi-transparent overlay on top
            RootGrid.Background = brush;
        }

        #endregion

        #region Selection

        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_selectionDone && _currentTool != CaptureDrawTool.None)
            {
                StartDrawing(e.GetPosition(RootGrid));
                return;
            }

            if (_selectionDone) return;

            _isSelecting = true;
            _selStart = e.GetPosition(RootGrid);
            _selEnd = _selStart;
            SelectionRect.Visibility = Visibility.Visible;
            UpdateSelectionRect();
            Mouse.Capture(RootGrid);
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDrawing && _currentTool != CaptureDrawTool.None)
            {
                ContinueDrawing(e.GetPosition(RootGrid));
                return;
            }

            if (!_isSelecting) return;

            _selEnd = e.GetPosition(RootGrid);
            UpdateSelectionRect();
        }

        private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDrawing)
            {
                FinishDrawing(e.GetPosition(RootGrid));
                return;
            }

            if (!_isSelecting) return;

            _isSelecting = false;
            Mouse.Capture(null);

            _selEnd = e.GetPosition(RootGrid);

            var rect = GetSelectionRect();
            if (rect.Width < 5 || rect.Height < 5) return;

            _selectionDone = true;
            ShowToolbar();
            CutOverlayHole();
        }

        private void Canvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Right-click cancels
            if (_isSelecting)
            {
                _isSelecting = false;
                Mouse.Capture(null);
                SelectionRect.Visibility = Visibility.Collapsed;
            }
            else
            {
                Close();
            }
        }

        private void UpdateSelectionRect()
        {
            var rect = GetSelectionRect();
            Canvas.SetLeft(SelectionRect, rect.X);
            Canvas.SetTop(SelectionRect, rect.Y);
            SelectionRect.Width = rect.Width;
            SelectionRect.Height = rect.Height;

            // Update dimension label
            DimensionLabel.Visibility = Visibility.Visible;
            DimensionLabel.Margin = new Thickness(rect.X, rect.Y - 24, 0, 0);
            DimensionText.Text = $"{(int)rect.Width} × {(int)rect.Height}";
        }

        private Rect GetSelectionRect()
        {
            double x = Math.Min(_selStart.X, _selEnd.X);
            double y = Math.Min(_selStart.Y, _selEnd.Y);
            double w = Math.Abs(_selEnd.X - _selStart.X);
            double h = Math.Abs(_selEnd.Y - _selStart.Y);
            return new Rect(x, y, w, h);
        }

        private void CutOverlayHole()
        {
            // Make the selected region transparent (show original screenshot)
            var rect = GetSelectionRect();
            var geometry = new CombinedGeometry(
                GeometryCombineMode.Exclude,
                new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)),
                new RectangleGeometry(rect));

            OverlayCanvas.Clip = geometry;
        }

        private void ShowToolbar()
        {
            var rect = GetSelectionRect();
            double toolbarY = rect.Bottom + 8;
            if (toolbarY + 50 > ActualHeight)
                toolbarY = rect.Top - 50;

            Toolbar.Margin = new Thickness(rect.X, toolbarY, 0, 0);
            Toolbar.Visibility = Visibility.Visible;
            Cursor = Cursors.Arrow;
        }

        #endregion

        #region Drawing Tools

        private void Tool_Rect(object s, RoutedEventArgs e) => _currentTool = CaptureDrawTool.Rect;
        private void Tool_Circle(object s, RoutedEventArgs e) => _currentTool = CaptureDrawTool.Circle;
        private void Tool_Arrow(object s, RoutedEventArgs e) => _currentTool = CaptureDrawTool.Arrow;
        private void Tool_Line(object s, RoutedEventArgs e) => _currentTool = CaptureDrawTool.Line;
        private void Tool_Text(object s, RoutedEventArgs e) => _currentTool = CaptureDrawTool.Text;
        private void Tool_Mosaic(object s, RoutedEventArgs e) => _currentTool = CaptureDrawTool.Mosaic;
        private void Tool_Pen(object s, RoutedEventArgs e)
        {
            _currentTool = CaptureDrawTool.Pen;
            Cursor = Cursors.Pen;
        }

        private void Tool_Undo(object s, RoutedEventArgs e)
        {
            if (_annotations.Count > 0)
            {
                _annotations.RemoveAt(_annotations.Count - 1);
                RedrawAnnotations();
            }
        }

        private void Tool_ColorPick(object s, RoutedEventArgs e)
        {
            ColorPopup.IsOpen = !ColorPopup.IsOpen;
        }

        private void ColorPick_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string colorStr)
            {
                var wpfColor = (Color)System.Windows.Media.ColorConverter.ConvertFromString(colorStr);
                _drawColor = System.Drawing.Color.FromArgb(wpfColor.A, wpfColor.R, wpfColor.G, wpfColor.B);
                ColorIndicator.Fill = new SolidColorBrush(wpfColor);
                ColorPopup.IsOpen = false;
            }
        }

        private int GetStrokeWidth()
        {
            return StrokeWidthCombo.SelectedIndex switch
            {
                0 => 1,
                1 => 2,
                2 => 4,
                3 => 6,
                _ => 2
            };
        }

        private void StartDrawing(Point pos)
        {
            var rect = GetSelectionRect();
            if (!rect.Contains(pos)) return;

            if (_currentTool == CaptureDrawTool.Text)
            {
                ShowTextInput(pos);
                return;
            }

            _isDrawing = true;
            _drawStart = pos;
            _drawWidth = GetStrokeWidth();
            _penPoints.Clear();
            _penPoints.Add(pos);
            Mouse.Capture(RootGrid);
        }

        private void ContinueDrawing(Point pos)
        {
            if (_currentTool == CaptureDrawTool.Pen)
            {
                _penPoints.Add(pos);
                // Live preview: redraw all annotations + current pen stroke
                RedrawAnnotationsWithPreview(g =>
                {
                    DrawPenStroke(g, new List<Point>(_penPoints), _drawColor, _drawWidth);
                });
            }
        }

        private void FinishDrawing(Point pos)
        {
            _isDrawing = false;
            Mouse.Capture(null);

            var selRect = GetSelectionRect();
            var color = _drawColor;
            var width = _drawWidth;

            // Convert from screen to selection-relative coordinates
            var startRel = new System.Drawing.Point(
                (int)(_drawStart.X - selRect.X), (int)(_drawStart.Y - selRect.Y));
            var endRel = new System.Drawing.Point(
                (int)(pos.X - selRect.X), (int)(pos.Y - selRect.Y));

            switch (_currentTool)
            {
                case CaptureDrawTool.Rect:
                    _annotations.Add(g =>
                    {
                        using var pen = new Pen(color, width);
                        var r = MakeDrawRect(startRel, endRel);
                        g.DrawRectangle(pen, r);
                    });
                    break;

                case CaptureDrawTool.Circle:
                    _annotations.Add(g =>
                    {
                        using var pen = new Pen(color, width);
                        var r = MakeDrawRect(startRel, endRel);
                        g.DrawEllipse(pen, r);
                    });
                    break;

                case CaptureDrawTool.Arrow:
                    _annotations.Add(g =>
                    {
                        using var pen = new Pen(color, width);
                        pen.CustomEndCap = new AdjustableArrowCap(5, 5);
                        g.DrawLine(pen, startRel, endRel);
                    });
                    break;

                case CaptureDrawTool.Line:
                    _annotations.Add(g =>
                    {
                        using var pen = new Pen(color, width);
                        g.DrawLine(pen, startRel, endRel);
                    });
                    break;

                case CaptureDrawTool.Mosaic:
                    _annotations.Add(g =>
                    {
                        var r = MakeDrawRect(startRel, endRel);
                        ApplyMosaic(g, r);
                    });
                    break;

                case CaptureDrawTool.Pen:
                    var points = new List<Point>(_penPoints);
                    var offsetX = selRect.X;
                    var offsetY = selRect.Y;
                    _annotations.Add(g =>
                    {
                        var relPoints = new List<Point>();
                        foreach (var p in points)
                            relPoints.Add(new Point(p.X - offsetX, p.Y - offsetY));
                        DrawPenStroke(g, relPoints, color, width);
                    });
                    _penPoints.Clear();
                    break;
            }

            RedrawAnnotations();
        }

        private static System.Drawing.Rectangle MakeDrawRect(
            System.Drawing.Point a, System.Drawing.Point b)
        {
            int x = Math.Min(a.X, b.X);
            int y = Math.Min(a.Y, b.Y);
            int w = Math.Abs(b.X - a.X);
            int h = Math.Abs(b.Y - a.Y);
            return new System.Drawing.Rectangle(x, y, Math.Max(w, 1), Math.Max(h, 1));
        }

        private void DrawPenStroke(Graphics g, List<Point> points,
            System.Drawing.Color color, int width)
        {
            if (points.Count < 2) return;
            using var pen = new Pen(color, width)
            {
                LineJoin = LineJoin.Round,
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };

            var selRect = GetSelectionRect();
            for (int i = 1; i < points.Count; i++)
            {
                var p1 = new System.Drawing.Point(
                    (int)(points[i - 1].X - selRect.X), (int)(points[i - 1].Y - selRect.Y));
                var p2 = new System.Drawing.Point(
                    (int)(points[i].X - selRect.X), (int)(points[i].Y - selRect.Y));
                g.DrawLine(pen, p1, p2);
            }
        }

        private void ApplyMosaic(Graphics g, System.Drawing.Rectangle area)
        {
            if (_screenBitmap == null) return;
            var selRect = GetSelectionRect();

            int blockSize = 10;
            int srcX = (int)selRect.X + area.X;
            int srcY = (int)selRect.Y + area.Y;

            for (int y = 0; y < area.Height; y += blockSize)
            {
                for (int x = 0; x < area.Width; x += blockSize)
                {
                    int px = Math.Min(srcX + x, _screenBitmap.Width - 1);
                    int py = Math.Min(srcY + y, _screenBitmap.Height - 1);
                    var pixel = _screenBitmap.GetPixel(px, py);
                    using var brush = new SolidBrush(pixel);
                    g.FillRectangle(brush,
                        area.X + x, area.Y + y,
                        Math.Min(blockSize, area.Width - x),
                        Math.Min(blockSize, area.Height - y));
                }
            }
        }

        #endregion

        #region Text Input

        private Point _textInsertPos;

        private void ShowTextInput(Point pos)
        {
            _textInsertPos = pos;
            var selRect = GetSelectionRect();
            TextInputPanel.Margin = new Thickness(pos.X, pos.Y - 40, 0, 0);
            TextInputPanel.Visibility = Visibility.Visible;
            TextInputBox.Text = "";
            TextInputBox.Focus();
        }

        private void TextInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                CommitTextAnnotation();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                TextInputPanel.Visibility = Visibility.Collapsed;
                e.Handled = true;
            }
        }

        private void TextInput_OK(object sender, RoutedEventArgs e) => CommitTextAnnotation();

        private void CommitTextAnnotation()
        {
            var text = TextInputBox.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                TextInputPanel.Visibility = Visibility.Collapsed;
                return;
            }

            var selRect = GetSelectionRect();
            var pos = new System.Drawing.Point(
                (int)(_textInsertPos.X - selRect.X),
                (int)(_textInsertPos.Y - selRect.Y));
            var color = _drawColor;
            var fontSize = GetStrokeWidth() * 6 + 12;

            _annotations.Add(g =>
            {
                using var font = new System.Drawing.Font("Yu Gothic UI", fontSize, System.Drawing.FontStyle.Bold);
                using var brush = new SolidBrush(color);
                // Draw text shadow
                using var shadowBrush = new SolidBrush(System.Drawing.Color.FromArgb(128, 0, 0, 0));
                g.DrawString(text, font, shadowBrush,
                    new System.Drawing.Point(pos.X + 1, pos.Y + 1));
                g.DrawString(text, font, brush, pos);
            });

            TextInputPanel.Visibility = Visibility.Collapsed;
            RedrawAnnotations();
        }

        #endregion

        #region Rendering

        private Bitmap GetCroppedBitmap()
        {
            if (_screenBitmap == null) throw new InvalidOperationException();

            var selRect = GetSelectionRect();
            int x = Math.Max(0, (int)selRect.X);
            int y = Math.Max(0, (int)selRect.Y);
            int w = Math.Min((int)selRect.Width, _screenBitmap.Width - x);
            int h = Math.Min((int)selRect.Height, _screenBitmap.Height - y);

            var cropped = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(cropped))
            {
                g.DrawImage(_screenBitmap, new System.Drawing.Rectangle(0, 0, w, h),
                    new System.Drawing.Rectangle(x, y, w, h), GraphicsUnit.Pixel);

                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                foreach (var annotation in _annotations)
                    annotation(g);
            }
            return cropped;
        }

        private void RedrawAnnotations()
        {
            RedrawAnnotationsWithPreview(null);
        }

        private void RedrawAnnotationsWithPreview(Action<Graphics>? preview)
        {
            if (_screenBitmap == null) return;

            var selRect = GetSelectionRect();
            int x = (int)selRect.X;
            int y = (int)selRect.Y;
            int w = (int)selRect.Width;
            int h = (int)selRect.Height;
            if (w <= 0 || h <= 0) return;

            // Create a composited bitmap: original + annotations
            using var composed = new Bitmap(
                _screenBitmap.Width, _screenBitmap.Height,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(composed))
            {
                g.DrawImage(_screenBitmap, 0, 0);

                // Apply annotations in selection area
                g.TranslateTransform(x, y);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                foreach (var annotation in _annotations)
                    annotation(g);
                preview?.Invoke(g);
                g.ResetTransform();
            }

            var source = BitmapToSource(composed);
            RootGrid.Background = new ImageBrush(source) { Stretch = Stretch.None };
        }

        private static BitmapSource BitmapToSource(Bitmap bitmap)
        {
            var data = bitmap.LockBits(
                new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            var source = BitmapSource.Create(
                data.Width, data.Height, 96, 96,
                PixelFormats.Bgra32, null,
                data.Scan0, data.Stride * data.Height, data.Stride);
            bitmap.UnlockBits(data);
            source.Freeze();
            return source;
        }

        #endregion

        #region Actions

        private void Action_Copy(object sender, RoutedEventArgs e)
        {
            try
            {
                using var bmp = GetCroppedBitmap();
                using var ms = new MemoryStream();
                bmp.Save(ms, ImageFormat.Png);
                ms.Position = 0;

                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.StreamSource = ms;
                bi.EndInit();
                bi.Freeze();

                Clipboard.SetImage(bi);
                CopiedToClipboard = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Copy failed: {ex.Message}");
            }
            Close();
        }

        private void Action_Save(object sender, RoutedEventArgs e)
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                    "InsightCast");
                Directory.CreateDirectory(dir);

                var filename = $"capture_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                var path = Path.Combine(dir, filename);

                using var bmp = GetCroppedBitmap();
                bmp.Save(path, ImageFormat.Png);
                CapturedImagePath = path;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Save failed: {ex.Message}");
            }
            Close();
        }

        private void Action_PinToScene(object sender, RoutedEventArgs e)
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "InsightCast", "Captures");
                Directory.CreateDirectory(dir);

                var filename = $"capture_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                var path = Path.Combine(dir, filename);

                using var bmp = GetCroppedBitmap();
                bmp.Save(path, ImageFormat.Png);
                CapturedImagePath = path;
                PinnedToScene = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Pin failed: {ex.Message}");
            }
            Close();
        }

        private void Action_Cancel(object sender, RoutedEventArgs e) => Close();

        #endregion

        #region Keyboard

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    if (TextInputPanel.Visibility == Visibility.Visible)
                        TextInputPanel.Visibility = Visibility.Collapsed;
                    else
                        Close();
                    e.Handled = true;
                    break;

                case Key.Z when Keyboard.Modifiers == ModifierKeys.Control:
                    Tool_Undo(sender, e);
                    e.Handled = true;
                    break;

                // Tool shortcuts
                case Key.R when _selectionDone:
                    _currentTool = CaptureDrawTool.Rect;
                    Cursor = Cursors.Cross;
                    e.Handled = true;
                    break;
                case Key.C when _selectionDone && Keyboard.Modifiers != ModifierKeys.Control:
                    _currentTool = CaptureDrawTool.Circle;
                    Cursor = Cursors.Cross;
                    e.Handled = true;
                    break;
                case Key.A when _selectionDone:
                    _currentTool = CaptureDrawTool.Arrow;
                    Cursor = Cursors.Cross;
                    e.Handled = true;
                    break;
                case Key.L when _selectionDone:
                    _currentTool = CaptureDrawTool.Line;
                    Cursor = Cursors.Cross;
                    e.Handled = true;
                    break;
                case Key.T when _selectionDone:
                    _currentTool = CaptureDrawTool.Text;
                    Cursor = Cursors.IBeam;
                    e.Handled = true;
                    break;
                case Key.M when _selectionDone:
                    _currentTool = CaptureDrawTool.Mosaic;
                    Cursor = Cursors.Cross;
                    e.Handled = true;
                    break;
                case Key.P when _selectionDone:
                    _currentTool = CaptureDrawTool.Pen;
                    Cursor = Cursors.Pen;
                    e.Handled = true;
                    break;

                // Ctrl+C to copy selection
                case Key.C when _selectionDone && Keyboard.Modifiers == ModifierKeys.Control:
                    Action_Copy(sender, e);
                    e.Handled = true;
                    break;

                // Ctrl+S to save
                case Key.S when _selectionDone && Keyboard.Modifiers == ModifierKeys.Control:
                    Action_Save(sender, e);
                    e.Handled = true;
                    break;

                // Enter to pin to scene
                case Key.Enter when _selectionDone:
                    Action_PinToScene(sender, e);
                    e.Handled = true;
                    break;
            }
        }

        #endregion

        protected override void OnClosed(EventArgs e)
        {
            _screenBitmap?.Dispose();
            base.OnClosed(e);
        }
    }
}

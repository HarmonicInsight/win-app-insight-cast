using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using InsightCast.Models;
using DashStyle = System.Drawing.Drawing2D.DashStyle;
using Color = System.Windows.Media.Color;
using Pen = System.Drawing.Pen;
using Point = System.Windows.Point;
using Size = System.Drawing.Size;

namespace InsightCast.Views
{
    /// <summary>
    /// 画面キャプチャウィンドウ
    /// 製品品質: DPI対応、パフォーマンス最適化、Undo/Redo対応
    /// </summary>
    public partial class ScreenCaptureWindow : Window
    {
        #region Constants

        private const int MinSelectionSize = 5;
        private const int ToolbarOffset = 8;
        private const int MosaicBlockSize = 5;

        #endregion

        #region Fields

        // DPI
        private double _dpiScaleX = 1.0;
        private double _dpiScaleY = 1.0;

        // Screen capture
        private Bitmap? _screenBitmap;
        private BitmapSource? _screenSource;

        // Selection state
        private bool _isSelecting;
        private bool _selectionDone;
        private Point _selStart;
        private Point _selEnd;

        // Selection resize/move
        private bool _isMovingSelection;
        private bool _isResizingSelection;
        private ResizeHandle _selectionResizeHandle;
        private Point _selectionDragStart;

        // Drawing state
        private CaptureDrawTool _currentTool = CaptureDrawTool.None;
        private System.Drawing.Color _drawColor = System.Drawing.Color.Red;
        private int _drawWidth = 2;
        private bool _isDrawing;
        private Point _drawStart;
        private readonly List<CaptureAnnotation> _annotations = new();
        private readonly List<Point> _penPoints = new();
        private int _nextNumberMarker = 1;

        // Annotation selection/movement
        private CaptureAnnotation? _selectedAnnotation;
        private bool _isDraggingAnnotation;
        private bool _isResizingAnnotation;
        private ResizeHandle _annotationResizeHandle;
        private System.Drawing.Point _dragOffset;
        private System.Drawing.Point _lastDragPos;

        // History (Undo/Redo)
        private readonly CaptureHistory _history = new();

        // Tool buttons
        private readonly List<Button> _toolButtons = new();

        // Result
        public string? CapturedImagePath { get; private set; }
        public bool CopiedToClipboard { get; private set; }
        public bool PinnedToScene { get; private set; }
        public bool RecordingRequested { get; private set; }
        public System.Drawing.Rectangle RecordingRegion { get; private set; }

        #endregion

        #region Constructor

        public ScreenCaptureWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        #endregion

        #region Screen Capture & DPI

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

        [DllImport("user32.dll")]
        private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        private static readonly IntPtr DPI_AWARENESS_CONTEXT_SYSTEM_AWARE = new(-2);
        private static readonly IntPtr HWND_TOPMOST = new(-1);
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const uint MONITOR_DEFAULTTONEAREST = 2;

        private const int LOGPIXELSX = 88;
        private const int LOGPIXELSY = 90;

        private const int WM_DPICHANGED = 0x02E0;

        // Virtual screen bounds in physical pixels
        private int _vsLeft, _vsTop, _vsWidth, _vsHeight;

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Block WM_DPICHANGED to prevent WPF from rescaling when cursor crosses monitors
            if (HwndSource.FromVisual(this) is HwndSource hwndSource)
                hwndSource.AddHook(WndProc);

            CaptureVirtualScreen();
            PositionWindowOverVirtualScreen();
            SetOverlayBackground();

            // Calculate scale from WPF DIPs to image pixels AFTER window is positioned
            // This ensures all coordinate conversions work correctly with Stretch.Fill
            Dispatcher.InvokeAsync(() =>
            {
                if (ActualWidth > 0 && ActualHeight > 0)
                {
                    _dpiScaleX = _vsWidth / ActualWidth;
                    _dpiScaleY = _vsHeight / ActualHeight;
                }
            }, System.Windows.Threading.DispatcherPriority.Loaded);
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

        private void PositionWindowOverVirtualScreen()
        {
            // Cover all monitors using SetWindowPos with physical pixel coordinates
            // This avoids WindowState=Maximized which triggers DPI context changes
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowPos(hwnd, HWND_TOPMOST, _vsLeft, _vsTop, _vsWidth, _vsHeight, SWP_SHOWWINDOW);
        }

        private void CaptureVirtualScreen()
        {
            try
            {
                // Get virtual screen bounds via Win32 (physical pixels, DPI-independent)
                GetCursorPos(out _); // ensure user32 is loaded
                _vsLeft = GetSystemMetrics(SM_XVIRTUALSCREEN);
                _vsTop = GetSystemMetrics(SM_YVIRTUALSCREEN);
                _vsWidth = GetSystemMetrics(SM_CXVIRTUALSCREEN);
                _vsHeight = GetSystemMetrics(SM_CYVIRTUALSCREEN);

                _screenBitmap = new Bitmap(_vsWidth, _vsHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(_screenBitmap))
                {
                    g.CopyFromScreen(_vsLeft, _vsTop, 0, 0, new Size(_vsWidth, _vsHeight));
                }

                _screenSource = BitmapToSource(_screenBitmap);
            }
            catch (Exception ex)
            {
                ShowError("画面のキャプチャに失敗しました", ex);
                Close();
            }
        }

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);
        private const int SM_XVIRTUALSCREEN = 76;
        private const int SM_YVIRTUALSCREEN = 77;
        private const int SM_CXVIRTUALSCREEN = 78;
        private const int SM_CYVIRTUALSCREEN = 79;

        private void SetOverlayBackground()
        {
            if (_screenSource == null) return;

            // Use Stretch.Fill to fill the window regardless of DPI differences between monitors
            var brush = new ImageBrush(_screenSource) { Stretch = Stretch.Fill };
            RootGrid.Background = brush;
        }

        #endregion

        #region Selection

        private void Toolbar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Stop mouse events from propagating to the capture canvas
            e.Handled = true;
        }

        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Ignore clicks on the toolbar (prevents selection restart when clicking tools)
            if (Toolbar.IsVisible && Toolbar.IsMouseOver) return;

            var pos = e.GetPosition(RootGrid);
            var scaledPos = ScaleToScreen(pos);

            // 選択領域のリサイズハンドルチェック
            if (_selectionDone && !_isDrawing)
            {
                var handle = HitTestSelectionHandle(scaledPos);
                if (handle != ResizeHandle.None)
                {
                    _isResizingSelection = true;
                    _selectionResizeHandle = handle;
                    _selectionDragStart = pos;
                    Mouse.Capture(RootGrid);
                    UpdateCursorForHandle(handle);
                    return;
                }

                // 選択領域内クリック → 移動開始
                var selRect = GetSelectionRect();
                if (selRect.Contains(pos) && _currentTool == CaptureDrawTool.None)
                {
                    _isMovingSelection = true;
                    _selectionDragStart = pos;
                    Mouse.Capture(RootGrid);
                    Cursor = Cursors.SizeAll;
                    return;
                }
            }

            // 選択モード: アノテーション選択/ドラッグ
            if (_selectionDone && _currentTool == CaptureDrawTool.Select)
            {
                HandleAnnotationSelection(pos);
                return;
            }

            // 描画開始
            if (_selectionDone && _currentTool != CaptureDrawTool.None && _currentTool != CaptureDrawTool.Select)
            {
                StartDrawing(pos);
                return;
            }

            // 選択開始
            if (!_selectionDone)
            {
                _isSelecting = true;
                _selStart = pos;
                _selEnd = _selStart;
                SelectionRect.Visibility = Visibility.Visible;
                UpdateSelectionRect();
                Mouse.Capture(RootGrid);
            }
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            var pos = e.GetPosition(RootGrid);

            // 選択領域リサイズ中
            if (_isResizingSelection)
            {
                ResizeSelection(pos);
                return;
            }

            // 選択領域移動中
            if (_isMovingSelection)
            {
                MoveSelection(pos);
                return;
            }

            // アノテーションリサイズ中
            if (_isResizingAnnotation && _selectedAnnotation != null)
            {
                ResizeAnnotation(pos);
                return;
            }

            // アノテーションドラッグ中
            if (_isDraggingAnnotation && _selectedAnnotation != null)
            {
                DragAnnotation(pos);
                return;
            }

            // 描画中
            if (_isDrawing && _currentTool != CaptureDrawTool.None)
            {
                ContinueDrawing(pos);
                return;
            }

            // 選択中
            if (_isSelecting)
            {
                _selEnd = pos;
                UpdateSelectionRect();
                return;
            }

            // カーソル更新（選択領域のリサイズハンドル上）
            if (_selectionDone && !_isDrawing)
            {
                var scaledPos = ScaleToScreen(pos);
                var handle = HitTestSelectionHandle(scaledPos);
                if (handle != ResizeHandle.None)
                {
                    UpdateCursorForHandle(handle);
                    return;
                }

                // ツールに応じたカーソル
                UpdateCursorForTool();
            }
        }

        private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // 選択領域リサイズ終了
            if (_isResizingSelection)
            {
                _isResizingSelection = false;
                Mouse.Capture(null);
                UpdateCursorForTool();
                CutOverlayHole();
                ShowToolbar();
                return;
            }

            // 選択領域移動終了
            if (_isMovingSelection)
            {
                _isMovingSelection = false;
                Mouse.Capture(null);
                UpdateCursorForTool();
                CutOverlayHole();
                ShowToolbar();
                return;
            }

            // アノテーションリサイズ終了
            if (_isResizingAnnotation)
            {
                _isResizingAnnotation = false;
                Mouse.Capture(null);
                UpdateCursorForTool();
                return;
            }

            // アノテーションドラッグ終了
            if (_isDraggingAnnotation)
            {
                _isDraggingAnnotation = false;
                Mouse.Capture(null);
                UpdateCursorForTool();
                return;
            }

            // 描画終了
            if (_isDrawing)
            {
                FinishDrawing(e.GetPosition(RootGrid));
                return;
            }

            // 選択終了
            if (_isSelecting)
            {
                _isSelecting = false;
                Mouse.Capture(null);
                _selEnd = e.GetPosition(RootGrid);

                var rect = GetSelectionRect();
                if (rect.Width < MinSelectionSize || rect.Height < MinSelectionSize)
                {
                    SelectionRect.Visibility = Visibility.Collapsed;
                    DimensionLabel.Visibility = Visibility.Collapsed;
                    return;
                }

                _selectionDone = true;
                ShowToolbar();
                CutOverlayHole();
            }
        }

        private void Canvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 操作キャンセル
            if (_isSelecting)
            {
                _isSelecting = false;
                Mouse.Capture(null);
                SelectionRect.Visibility = Visibility.Collapsed;
                DimensionLabel.Visibility = Visibility.Collapsed;
            }
            else if (_isDrawing)
            {
                _isDrawing = false;
                Mouse.Capture(null);
                _penPoints.Clear();
                RedrawAnnotations();
            }
            else if (_selectionDone)
            {
                // 選択解除してやり直し
                _selectionDone = false;
                _annotations.Clear();
                _history.Clear();
                SelectionRect.Visibility = Visibility.Collapsed;
                DimensionLabel.Visibility = Visibility.Collapsed;
                Toolbar.Visibility = Visibility.Collapsed;
                OverlayCanvas.Clip = null;
                OverlayCanvas.Visibility = Visibility.Collapsed;
                SetOverlayBackground();
                Cursor = Cursors.Cross;
            }
            else
            {
                Close();
            }
        }

        private ResizeHandle HitTestSelectionHandle(System.Drawing.Point pt)
        {
            var rect = GetSelectionRect();
            var bounds = new System.Drawing.Rectangle(
                (int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height);

            var handles = CaptureAnnotation.GetHandleRects(bounds);

            foreach (var kv in handles)
            {
                if (kv.Value.Contains(pt))
                    return kv.Key;
            }

            return ResizeHandle.None;
        }

        private void ResizeSelection(Point pos)
        {
            double dx = pos.X - _selectionDragStart.X;
            double dy = pos.Y - _selectionDragStart.Y;

            switch (_selectionResizeHandle)
            {
                case ResizeHandle.TopLeft:
                    _selStart = new Point(_selStart.X + dx, _selStart.Y + dy);
                    break;
                case ResizeHandle.Top:
                    _selStart = new Point(_selStart.X, _selStart.Y + dy);
                    break;
                case ResizeHandle.TopRight:
                    _selStart = new Point(_selStart.X, _selStart.Y + dy);
                    _selEnd = new Point(_selEnd.X + dx, _selEnd.Y);
                    break;
                case ResizeHandle.Right:
                    _selEnd = new Point(_selEnd.X + dx, _selEnd.Y);
                    break;
                case ResizeHandle.BottomRight:
                    _selEnd = new Point(_selEnd.X + dx, _selEnd.Y + dy);
                    break;
                case ResizeHandle.Bottom:
                    _selEnd = new Point(_selEnd.X, _selEnd.Y + dy);
                    break;
                case ResizeHandle.BottomLeft:
                    _selStart = new Point(_selStart.X + dx, _selStart.Y);
                    _selEnd = new Point(_selEnd.X, _selEnd.Y + dy);
                    break;
                case ResizeHandle.Left:
                    _selStart = new Point(_selStart.X + dx, _selStart.Y);
                    break;
            }

            _selectionDragStart = pos;
            UpdateSelectionRect();
        }

        private void MoveSelection(Point pos)
        {
            double dx = pos.X - _selectionDragStart.X;
            double dy = pos.Y - _selectionDragStart.Y;

            _selStart = new Point(_selStart.X + dx, _selStart.Y + dy);
            _selEnd = new Point(_selEnd.X + dx, _selEnd.Y + dy);
            _selectionDragStart = pos;

            UpdateSelectionRect();
        }

        private void UpdateSelectionRect()
        {
            var rect = GetSelectionRect();
            SelectionRect.Margin = new Thickness(rect.X, rect.Y, 0, 0);
            SelectionRect.Width = rect.Width;
            SelectionRect.Height = rect.Height;

            // 寸法ラベル
            DimensionLabel.Visibility = Visibility.Visible;
            DimensionLabel.Margin = new Thickness(rect.X, Math.Max(0, rect.Y - 24), 0, 0);

            int pixelWidth = (int)(rect.Width * _dpiScaleX);
            int pixelHeight = (int)(rect.Height * _dpiScaleY);
            DimensionText.Text = $"{pixelWidth} × {pixelHeight}  |  Enter で確定";
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
            var rect = GetSelectionRect();
            var geometry = new CombinedGeometry(
                GeometryCombineMode.Exclude,
                new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)),
                new RectangleGeometry(rect));

            OverlayCanvas.Visibility = Visibility.Visible;
            OverlayCanvas.Clip = geometry;
        }

        private void ShowToolbar()
        {
            var rect = GetSelectionRect();
            double toolbarY = rect.Bottom + ToolbarOffset;

            // ツールバーが画面外に出る場合は上に配置
            Toolbar.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            double toolbarHeight = Toolbar.DesiredSize.Height;

            if (toolbarY + toolbarHeight > ActualHeight)
                toolbarY = rect.Top - toolbarHeight - ToolbarOffset;

            // 左端制限
            double toolbarX = Math.Max(0, rect.X);
            if (toolbarX + Toolbar.DesiredSize.Width > ActualWidth)
                toolbarX = ActualWidth - Toolbar.DesiredSize.Width;

            Toolbar.Margin = new Thickness(toolbarX, toolbarY, 0, 0);
            Toolbar.Visibility = Visibility.Visible;
            Cursor = Cursors.Arrow;
        }

        #endregion

        #region Annotation Selection & Movement

        private void HandleAnnotationSelection(Point pos)
        {
            var selRect = GetSelectionRect();
            var relPos = new System.Drawing.Point(
                (int)((pos.X - selRect.X) * _dpiScaleX),
                (int)((pos.Y - selRect.Y) * _dpiScaleY));

            // リサイズハンドルチェック
            if (_selectedAnnotation != null)
            {
                var handle = _selectedAnnotation.HitTestHandle(relPos);
                if (handle != ResizeHandle.None)
                {
                    _isResizingAnnotation = true;
                    _annotationResizeHandle = handle;
                    _lastDragPos = relPos;
                    Mouse.Capture(RootGrid);
                    UpdateCursorForHandle(handle);
                    return;
                }
            }

            // アノテーションヒットテスト（後ろから順に）
            CaptureAnnotation? clicked = null;
            for (int i = _annotations.Count - 1; i >= 0; i--)
            {
                if (_annotations[i].HitTest(relPos))
                {
                    clicked = _annotations[i];
                    break;
                }
            }

            // 選択解除
            foreach (var a in _annotations) a.IsSelected = false;
            _selectedAnnotation = null;

            if (clicked != null)
            {
                _history.SaveState(_annotations);
                clicked.IsSelected = true;
                _selectedAnnotation = clicked;
                _isDraggingAnnotation = true;
                _dragOffset = new System.Drawing.Point(
                    relPos.X - clicked.Start.X,
                    relPos.Y - clicked.Start.Y);
                _lastDragPos = relPos;
                Mouse.Capture(RootGrid);
                Cursor = Cursors.SizeAll;
            }

            RedrawAnnotations();
        }

        private void HandleTextAnnotationEdit(Point pos)
        {
            // テキストツールでクリック → 常に新規作成（ダブルクリックで編集）
            PlaceTextAtPosition(pos);
        }

        private void DragAnnotation(Point pos)
        {
            if (_selectedAnnotation == null) return;

            var selRect = GetSelectionRect();
            var relPos = new System.Drawing.Point(
                (int)((pos.X - selRect.X) * _dpiScaleX),
                (int)((pos.Y - selRect.Y) * _dpiScaleY));

            int dx = relPos.X - _lastDragPos.X;
            int dy = relPos.Y - _lastDragPos.Y;
            _lastDragPos = relPos;

            _selectedAnnotation.Move(dx, dy);
            RedrawAnnotations();
        }

        private void ResizeAnnotation(Point pos)
        {
            if (_selectedAnnotation == null) return;

            var selRect = GetSelectionRect();
            var relPos = new System.Drawing.Point(
                (int)((pos.X - selRect.X) * _dpiScaleX),
                (int)((pos.Y - selRect.Y) * _dpiScaleY));

            int dx = relPos.X - _lastDragPos.X;
            int dy = relPos.Y - _lastDragPos.Y;
            _lastDragPos = relPos;

            _selectedAnnotation.Resize(_annotationResizeHandle, dx, dy);
            RedrawAnnotations();
        }

        #endregion

        #region Drawing Tools

        private void Tool_Select(object s, RoutedEventArgs e)
        {
            SelectTool(CaptureDrawTool.Select, BtnSelect);
            Cursor = Cursors.Arrow;
        }

        private void Tool_Rect(object s, RoutedEventArgs e)
        {
            SelectTool(CaptureDrawTool.Rect, BtnRect);
            Cursor = Cursors.Cross;
        }

        private void Tool_Circle(object s, RoutedEventArgs e)
        {
            SelectTool(CaptureDrawTool.Circle, BtnCircle);
            Cursor = Cursors.Cross;
        }

        private void Tool_Arrow(object s, RoutedEventArgs e)
        {
            SelectTool(CaptureDrawTool.Arrow, BtnArrow);
            Cursor = Cursors.Cross;
        }

        private void Tool_Line(object s, RoutedEventArgs e)
        {
            SelectTool(CaptureDrawTool.Line, BtnLine);
            Cursor = Cursors.Cross;
        }

        private void Tool_Text(object s, RoutedEventArgs e)
        {
            SelectTool(CaptureDrawTool.Text, BtnText);
            Cursor = Cursors.IBeam;
        }

        private void Tool_Mosaic(object s, RoutedEventArgs e)
        {
            SelectTool(CaptureDrawTool.Mosaic, BtnMosaic);
            Cursor = Cursors.Cross;
        }

        private void Tool_Pen(object s, RoutedEventArgs e)
        {
            SelectTool(CaptureDrawTool.Pen, BtnPen);
            Cursor = Cursors.Pen;
        }

        private void Tool_Highlighter(object s, RoutedEventArgs e)
        {
            SelectTool(CaptureDrawTool.Highlighter, BtnHighlighter);
            Cursor = Cursors.Pen;
        }

        private void Tool_Number(object s, RoutedEventArgs e)
        {
            SelectTool(CaptureDrawTool.NumberMarker, BtnNumber);
            Cursor = Cursors.Cross;
        }

        private void SelectTool(CaptureDrawTool tool, Button selectedBtn)
        {
            _currentTool = tool;

            // ツールボタンリスト初期化
            if (_toolButtons.Count == 0)
            {
                _toolButtons.AddRange(new[] { BtnSelect, BtnRect, BtnCircle, BtnArrow, BtnLine, BtnText, BtnMosaic, BtnPen, BtnHighlighter, BtnNumber });
            }

            // ボタンスタイルリセット
            foreach (var btn in _toolButtons)
            {
                btn.Background = new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString("#EEEEEE"));
                btn.BorderBrush = new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString("#CCC"));
            }

            // 選択中ボタンをハイライト
            selectedBtn.Background = new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString("#0078D4"));
            selectedBtn.BorderBrush = new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString("#0078D4"));

            // アノテーション選択解除
            foreach (var a in _annotations) a.IsSelected = false;
            _selectedAnnotation = null;
            RedrawAnnotations();
        }

        private void Tool_Undo(object s, RoutedEventArgs e)
        {
            if (_history.CanUndo)
            {
                var previous = _history.Undo(_annotations);
                if (previous != null)
                {
                    _annotations.Clear();
                    _annotations.AddRange(previous);
                    _selectedAnnotation = null;
                    RedrawAnnotations();
                }
            }
        }

        private void Tool_Redo(object s, RoutedEventArgs e)
        {
            if (_history.CanRedo)
            {
                var next = _history.Redo(_annotations);
                if (next != null)
                {
                    _annotations.Clear();
                    _annotations.AddRange(next);
                    _selectedAnnotation = null;
                    RedrawAnnotations();
                }
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
                NumberPreviewCircle.Fill = new SolidColorBrush(wpfColor);
                ColorPopup.IsOpen = false;
            }
        }

        private void NumberInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (NumberInput == null || NumberPreviewText == null) return;
            if (int.TryParse(NumberInput.Text, out int num) && num >= 1 && num <= 999)
            {
                _nextNumberMarker = num;
                NumberPreviewText.Text = num.ToString();
            }
        }

        private int GetTextSize()
        {
            if (TextSizeCombo == null) return 18;
            return TextSizeCombo.SelectedIndex switch
            {
                0 => 12,
                1 => 18,
                2 => 24,
                3 => 36,
                4 => 48,
                _ => 18
            };
        }

        private int GetStrokeWidth()
        {
            if (StrokeWidthCombo == null) return 2;
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

            // テキストは特別処理（既存テキストの編集または新規作成）
            if (_currentTool == CaptureDrawTool.Text)
            {
                HandleTextAnnotationEdit(pos);
                return;
            }

            if (_currentTool == CaptureDrawTool.NumberMarker)
            {
                AddNumberMarker(pos);
                return;
            }

            _history.SaveState(_annotations);
            _isDrawing = true;
            _drawStart = pos;
            _drawWidth = GetStrokeWidth();
            _penPoints.Clear();
            _penPoints.Add(pos);
            Mouse.Capture(RootGrid);
        }

        private void ContinueDrawing(Point pos)
        {
            var selRect = GetSelectionRect();
            var color = _drawColor;
            var width = _drawWidth;

            // 選択領域相対座標（DPIスケール適用）
            var startRel = new System.Drawing.Point(
                (int)((_drawStart.X - selRect.X) * _dpiScaleX),
                (int)((_drawStart.Y - selRect.Y) * _dpiScaleY));
            var endRel = new System.Drawing.Point(
                (int)((pos.X - selRect.X) * _dpiScaleX),
                (int)((pos.Y - selRect.Y) * _dpiScaleY));

            switch (_currentTool)
            {
                case CaptureDrawTool.Rect:
                case CaptureDrawTool.Circle:
                case CaptureDrawTool.Arrow:
                case CaptureDrawTool.Line:
                case CaptureDrawTool.Mosaic:
                    RedrawAnnotationsWithPreview(g =>
                    {
                        DrawPreviewShape(g, _currentTool, startRel, endRel, color, width);
                    });
                    break;

                case CaptureDrawTool.Pen:
                case CaptureDrawTool.Highlighter:
                    _penPoints.Add(pos);
                    RedrawAnnotationsWithPreview(g =>
                    {
                        DrawPreviewPenStroke(g, _currentTool, color, width);
                    });
                    break;
            }
        }

        private void FinishDrawing(Point pos)
        {
            _isDrawing = false;
            Mouse.Capture(null);

            var selRect = GetSelectionRect();
            var color = _drawColor;
            var width = _drawWidth;

            var startRel = new System.Drawing.Point(
                (int)((_drawStart.X - selRect.X) * _dpiScaleX),
                (int)((_drawStart.Y - selRect.Y) * _dpiScaleY));
            var endRel = new System.Drawing.Point(
                (int)((pos.X - selRect.X) * _dpiScaleX),
                (int)((pos.Y - selRect.Y) * _dpiScaleY));

            switch (_currentTool)
            {
                case CaptureDrawTool.Rect:
                case CaptureDrawTool.Circle:
                case CaptureDrawTool.Arrow:
                case CaptureDrawTool.Line:
                case CaptureDrawTool.Mosaic:
                    _annotations.Add(new CaptureAnnotation
                    {
                        Type = _currentTool,
                        Start = startRel,
                        End = endRel,
                        Color = color,
                        StrokeWidth = width
                    });
                    break;

                case CaptureDrawTool.Pen:
                case CaptureDrawTool.Highlighter:
                    var relPoints = new List<System.Drawing.Point>();
                    foreach (var p in _penPoints)
                    {
                        relPoints.Add(new System.Drawing.Point(
                            (int)((p.X - selRect.X) * _dpiScaleX),
                            (int)((p.Y - selRect.Y) * _dpiScaleY)));
                    }

                    _annotations.Add(new CaptureAnnotation
                    {
                        Type = _currentTool,
                        Color = color,
                        StrokeWidth = width,
                        PenPoints = relPoints
                    });
                    _penPoints.Clear();
                    break;
            }

            // 新規アノテーションを選択状態に
            foreach (var a in _annotations) a.IsSelected = false;
            if (_annotations.Count > 0)
            {
                _selectedAnnotation = _annotations[^1];
                _selectedAnnotation.IsSelected = true;
            }

            RedrawAnnotations();
        }

        private void DrawPreviewShape(Graphics g, CaptureDrawTool tool,
            System.Drawing.Point start, System.Drawing.Point end,
            System.Drawing.Color color, int width)
        {
            var rect = MakeDrawRect(start, end);

            switch (tool)
            {
                case CaptureDrawTool.Rect:
                    using (var pen = new Pen(color, width))
                        g.DrawRectangle(pen, rect);
                    break;

                case CaptureDrawTool.Circle:
                    using (var pen = new Pen(color, width))
                        g.DrawEllipse(pen, rect);
                    break;

                case CaptureDrawTool.Arrow:
                    using (var pen = new Pen(color, width))
                    {
                        pen.CustomEndCap = new AdjustableArrowCap(5, 5);
                        g.DrawLine(pen, start, end);
                    }
                    break;

                case CaptureDrawTool.Line:
                    using (var pen = new Pen(color, width))
                        g.DrawLine(pen, start, end);
                    break;

                case CaptureDrawTool.Mosaic:
                    using (var brush = new SolidBrush(System.Drawing.Color.FromArgb(100, 128, 128, 128)))
                        g.FillRectangle(brush, rect);
                    break;
            }
        }

        private void DrawPreviewPenStroke(Graphics g, CaptureDrawTool tool,
            System.Drawing.Color color, int width)
        {
            if (_penPoints.Count < 2) return;

            var selRect = GetSelectionRect();
            int alpha = tool == CaptureDrawTool.Highlighter ? 100 : 255;
            int strokeWidth = tool == CaptureDrawTool.Highlighter ? width * 4 : width;

            using var pen = new Pen(System.Drawing.Color.FromArgb(alpha, color), strokeWidth)
            {
                LineJoin = LineJoin.Round,
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };

            for (int i = 1; i < _penPoints.Count; i++)
            {
                var p1 = new System.Drawing.Point(
                    (int)((_penPoints[i - 1].X - selRect.X) * _dpiScaleX),
                    (int)((_penPoints[i - 1].Y - selRect.Y) * _dpiScaleY));
                var p2 = new System.Drawing.Point(
                    (int)((_penPoints[i].X - selRect.X) * _dpiScaleX),
                    (int)((_penPoints[i].Y - selRect.Y) * _dpiScaleY));
                g.DrawLine(pen, p1, p2);
            }
        }

        private void AddNumberMarker(Point pos)
        {
            _history.SaveState(_annotations);

            var selRect = GetSelectionRect();
            var relPos = new System.Drawing.Point(
                (int)((pos.X - selRect.X) * _dpiScaleX),
                (int)((pos.Y - selRect.Y) * _dpiScaleY));

            _annotations.Add(new CaptureAnnotation
            {
                Type = CaptureDrawTool.NumberMarker,
                Start = relPos,
                Color = _drawColor,
                StrokeWidth = _drawWidth,
                NumberValue = _nextNumberMarker++
            });

            // プレビューとインプットを更新
            if (NumberPreviewText != null)
                NumberPreviewText.Text = _nextNumberMarker.ToString();
            if (NumberInput != null)
                NumberInput.Text = _nextNumberMarker.ToString();

            RedrawAnnotations();
        }

        private static System.Drawing.Rectangle MakeDrawRect(
            System.Drawing.Point a, System.Drawing.Point b)
        {
            int x = Math.Min(a.X, b.X);
            int y = Math.Min(a.Y, b.Y);
            int w = Math.Max(Math.Abs(b.X - a.X), 1);
            int h = Math.Max(Math.Abs(b.Y - a.Y), 1);
            return new System.Drawing.Rectangle(x, y, w, h);
        }

        private void ApplyMosaic(Graphics g, System.Drawing.Rectangle area)
        {
            if (_screenBitmap == null) return;

            var selRect = GetSelectionRect();
            int srcX = (int)(selRect.X * _dpiScaleX) + area.X;
            int srcY = (int)(selRect.Y * _dpiScaleY) + area.Y;

            // LockBitsを使用した高速モザイク処理
            var srcRect = new System.Drawing.Rectangle(
                Math.Max(0, srcX),
                Math.Max(0, srcY),
                Math.Min(area.Width, _screenBitmap.Width - srcX),
                Math.Min(area.Height, _screenBitmap.Height - srcY));

            if (srcRect.Width <= 0 || srcRect.Height <= 0) return;

            var data = _screenBitmap.LockBits(srcRect, ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            try
            {
                unsafe
                {
                    byte* ptr = (byte*)data.Scan0;
                    int stride = data.Stride;

                    for (int y = 0; y < srcRect.Height; y += MosaicBlockSize)
                    {
                        for (int x = 0; x < srcRect.Width; x += MosaicBlockSize)
                        {
                            int px = Math.Min(x, srcRect.Width - 1);
                            int py = Math.Min(y, srcRect.Height - 1);

                            byte* pixel = ptr + py * stride + px * 4;
                            var color = System.Drawing.Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);

                            using var brush = new SolidBrush(color);
                            g.FillRectangle(brush,
                                area.X + x, area.Y + y,
                                Math.Min(MosaicBlockSize, srcRect.Width - x),
                                Math.Min(MosaicBlockSize, srcRect.Height - y));
                        }
                    }
                }
            }
            finally
            {
                _screenBitmap.UnlockBits(data);
            }
        }

        #endregion

        #region Text Input (Excel-style Inline)

        private Point _inlineTextPosition;
        private bool _isInlineTextActive;
        private CaptureAnnotation? _editingTextAnnotation;

        private void PlaceTextAtPosition(Point pos)
        {
            // インラインテキストボックスを表示（新規作成）
            ShowInlineTextBox(pos, null);
        }

        private void EditTextAnnotation(CaptureAnnotation annotation)
        {
            // 既存テキストの編集（アノテーションの位置に合わせる）
            var selRect = GetSelectionRect();
            var screenPos = new Point(
                selRect.X + annotation.Start.X / _dpiScaleX,
                selRect.Y + annotation.Start.Y / _dpiScaleY);
            ShowInlineTextBox(screenPos, annotation);
        }

        private void ShowInlineTextBox(Point pos, CaptureAnnotation? existingAnnotation)
        {
            _inlineTextPosition = pos;
            _isInlineTextActive = true;
            _editingTextAnnotation = existingAnnotation;

            // フォントサイズを取得（編集時は既存のサイズを使用）
            var fontSize = existingAnnotation?.StrokeWidth ?? GetTextSize();

            // テキストボックスを配置
            InlineTextBox.FontSize = fontSize;
            InlineTextBox.Text = existingAnnotation?.Text ?? "";

            // 編集時は既存テキストを非表示にして、その位置にTextBoxを表示
            if (existingAnnotation != null)
            {
                // 選択色をテキストボックスの文字色に
                var c = existingAnnotation.Color;
                InlineTextBox.Foreground = new SolidColorBrush(
                    Color.FromArgb(c.A, c.R, c.G, c.B));
            }
            else
            {
                var c = _drawColor;
                InlineTextBox.Foreground = new SolidColorBrush(
                    Color.FromArgb(c.A, c.R, c.G, c.B));
            }

            InlineTextBorder.Margin = new Thickness(pos.X - 2, pos.Y - 2, 0, 0);
            InlineTextBorder.Visibility = Visibility.Visible;
            InlineTextBox.Focus();
            InlineTextBox.SelectAll();
        }

        private void CommitInlineText()
        {
            if (!_isInlineTextActive) return;
            _isInlineTextActive = false;

            var text = InlineTextBox.Text;
            InlineTextBorder.Visibility = Visibility.Collapsed;

            // 編集モードの場合
            if (_editingTextAnnotation != null)
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    // 空の場合は削除
                    _history.SaveState(_annotations);
                    _annotations.Remove(_editingTextAnnotation);
                    _selectedAnnotation = null;
                }
                else
                {
                    // テキストを更新
                    _history.SaveState(_annotations);
                    _editingTextAnnotation.Text = text;

                    // テキストサイズを再計測
                    using (var bmp = new Bitmap(1, 1))
                    using (var g = Graphics.FromImage(bmp))
                    {
                        _editingTextAnnotation.MeasureTextBounds(g);
                    }
                }
                _editingTextAnnotation = null;
                RedrawAnnotations();
                return;
            }

            // 新規作成モード
            if (string.IsNullOrWhiteSpace(text)) return;

            _history.SaveState(_annotations);

            var selRect = GetSelectionRect();
            var relPos = new System.Drawing.Point(
                (int)((_inlineTextPosition.X - selRect.X) * _dpiScaleX),
                (int)((_inlineTextPosition.Y - selRect.Y) * _dpiScaleY));
            var color = _drawColor;
            var fontSize = GetTextSize();

            var annotation = new CaptureAnnotation
            {
                Type = CaptureDrawTool.Text,
                Start = relPos,
                Color = color,
                StrokeWidth = fontSize,
                Text = text
            };

            // テキストサイズを計測
            using (var bmp = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(bmp))
            {
                annotation.MeasureTextBounds(g);
            }

            _annotations.Add(annotation);

            // 選択状態に
            foreach (var a in _annotations) a.IsSelected = false;
            _selectedAnnotation = annotation;
            annotation.IsSelected = true;

            RedrawAnnotations();
        }

        private void CancelInlineText()
        {
            _isInlineTextActive = false;
            _editingTextAnnotation = null;
            InlineTextBox.Text = "";
            InlineTextBorder.Visibility = Visibility.Collapsed;
        }

        private void InlineTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                CommitInlineText();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CancelInlineText();
                e.Handled = true;
            }
        }

        private void InlineTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            // フォーカスが外れたらテキストを確定
            if (_isInlineTextActive)
            {
                CommitInlineText();
            }
        }

        private void Canvas_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (!_selectionDone) return;

            var pos = e.GetPosition(RootGrid);
            var selRect = GetSelectionRect();
            var relPos = new System.Drawing.Point(
                (int)((pos.X - selRect.X) * _dpiScaleX),
                (int)((pos.Y - selRect.Y) * _dpiScaleY));

            // テキストアノテーションをダブルクリックで編集
            for (int i = _annotations.Count - 1; i >= 0; i--)
            {
                var annotation = _annotations[i];
                if (annotation.Type == CaptureDrawTool.Text && annotation.HitTest(relPos))
                {
                    EditTextAnnotation(annotation);
                    e.Handled = true;
                    return;
                }
            }
        }

        #endregion

        #region Rendering

        private Bitmap GetCroppedBitmap()
        {
            if (_screenBitmap == null) throw new InvalidOperationException("スクリーンショットがありません");

            var selRect = GetSelectionRect();
            int x = Math.Max(0, (int)(selRect.X * _dpiScaleX));
            int y = Math.Max(0, (int)(selRect.Y * _dpiScaleY));
            int w = Math.Min((int)(selRect.Width * _dpiScaleX), _screenBitmap.Width - x);
            int h = Math.Min((int)(selRect.Height * _dpiScaleY), _screenBitmap.Height - y);

            if (w <= 0 || h <= 0)
                throw new InvalidOperationException("選択領域が無効です");

            var cropped = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(cropped))
            {
                g.DrawImage(_screenBitmap, new System.Drawing.Rectangle(0, 0, w, h),
                    new System.Drawing.Rectangle(x, y, w, h), GraphicsUnit.Pixel);

                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

                foreach (var annotation in _annotations)
                {
                    if (annotation.Type == CaptureDrawTool.Mosaic)
                    {
                        var rect = MakeDrawRect(annotation.Start, annotation.End);
                        ApplyMosaic(g, rect);
                    }
                    else
                    {
                        annotation.Draw(g, false);
                    }
                }
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
            int x = (int)(selRect.X * _dpiScaleX);
            int y = (int)(selRect.Y * _dpiScaleY);
            int w = (int)(selRect.Width * _dpiScaleX);
            int h = (int)(selRect.Height * _dpiScaleY);
            if (w <= 0 || h <= 0) return;

            // 選択領域のみを描画（パフォーマンス最適化）
            using var regionBitmap = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(regionBitmap))
            {
                // 元画像の該当領域をコピー
                g.DrawImage(_screenBitmap,
                    new System.Drawing.Rectangle(0, 0, w, h),
                    new System.Drawing.Rectangle(x, y, w, h),
                    GraphicsUnit.Pixel);

                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

                // アノテーション描画
                foreach (var annotation in _annotations)
                {
                    if (annotation.Type == CaptureDrawTool.Mosaic)
                    {
                        var rect = MakeDrawRect(annotation.Start, annotation.End);
                        ApplyMosaic(g, rect);
                    }
                    else
                    {
                        annotation.Draw(g);
                    }
                }

                preview?.Invoke(g);
            }

            // 合成画像を作成
            using var composed = new Bitmap(_screenBitmap.Width, _screenBitmap.Height,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(composed))
            {
                g.DrawImage(_screenBitmap, 0, 0);
                g.DrawImage(regionBitmap, x, y);
            }

            var source = BitmapToSource(composed);
            RootGrid.Background = new ImageBrush(source) { Stretch = Stretch.Fill };
        }

        private static BitmapSource BitmapToSource(Bitmap bitmap)
        {
            var data = bitmap.LockBits(
                new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            try
            {
                var source = BitmapSource.Create(
                    data.Width, data.Height, 96, 96,
                    PixelFormats.Bgra32, null,
                    data.Scan0, data.Stride * data.Height, data.Stride);
                source.Freeze();
                return source;
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
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
                ShowSuccess("クリップボードにコピーしました");
            }
            catch (Exception ex)
            {
                ShowError("コピーに失敗しました", ex);
                return;
            }
            Close();
        }

        private void Action_Save(object sender, RoutedEventArgs e)
        {
            try
            {
                var defaultDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                    "InsightCast");
                Directory.CreateDirectory(defaultDir);

                var defaultFilename = $"capture_{DateTime.Now:yyyyMMdd_HHmmss}.png";

                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "画像を保存",
                    Filter = "PNG画像 (*.png)|*.png|JPEG画像 (*.jpg)|*.jpg|すべてのファイル (*.*)|*.*",
                    DefaultExt = ".png",
                    FileName = defaultFilename,
                    InitialDirectory = defaultDir
                };

                if (dialog.ShowDialog() == true)
                {
                    using var bmp = GetCroppedBitmap();

                    // 拡張子に応じて保存形式を変更
                    var ext = Path.GetExtension(dialog.FileName).ToLowerInvariant();
                    var format = ext == ".jpg" || ext == ".jpeg" ? ImageFormat.Jpeg : ImageFormat.Png;

                    bmp.Save(dialog.FileName, format);
                    CapturedImagePath = dialog.FileName;
                    ShowSuccess($"保存しました: {dialog.FileName}");
                    Close();
                }
            }
            catch (Exception ex)
            {
                ShowError("保存に失敗しました", ex);
            }
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
                ShowError("シーンへの追加に失敗しました", ex);
                return;
            }
            Close();
        }

        private void Action_Record(object sender, RoutedEventArgs e)
        {
            try
            {
                var selRect = GetSelectionRect();
                // Convert WPF DIPs to physical pixels
                int x = _vsLeft + Math.Max(0, (int)(selRect.X * _dpiScaleX));
                int y = _vsTop + Math.Max(0, (int)(selRect.Y * _dpiScaleY));
                int w = (int)(selRect.Width * _dpiScaleX);
                int h = (int)(selRect.Height * _dpiScaleY);

                // Ensure even dimensions (required by libx264)
                w = w / 2 * 2;
                h = h / 2 * 2;

                if (w < 10 || h < 10) return;

                RecordingRegion = new System.Drawing.Rectangle(x, y, w, h);
                RecordingRequested = true;
            }
            catch { return; }
            Close();
        }

        private void Action_Cancel(object sender, RoutedEventArgs e) => Close();

        #endregion

        #region Keyboard

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            // TextBoxにフォーカスがある場合はショートカットを無効化（Escapeと修飾キー付きは除く）
            bool textBoxFocused = Keyboard.FocusedElement is TextBox;

            switch (e.Key)
            {
                case Key.Escape:
                    if (_isInlineTextActive)
                    {
                        CancelInlineText();
                    }
                    else if (_isDrawing)
                    {
                        _isDrawing = false;
                        Mouse.Capture(null);
                        _penPoints.Clear();
                        RedrawAnnotations();
                    }
                    else
                        Close();
                    e.Handled = true;
                    break;

                case Key.Z when Keyboard.Modifiers == ModifierKeys.Control:
                    Tool_Undo(sender, e);
                    e.Handled = true;
                    break;

                case Key.Y when Keyboard.Modifiers == ModifierKeys.Control:
                    Tool_Redo(sender, e);
                    e.Handled = true;
                    break;

                // ツールショートカット（TextBoxにフォーカスがある場合は無効）
                case Key.V when _selectionDone && !textBoxFocused:
                    Tool_Select(sender, e);
                    e.Handled = true;
                    break;
                case Key.R when _selectionDone && !textBoxFocused:
                    Tool_Rect(sender, e);
                    e.Handled = true;
                    break;
                case Key.C when _selectionDone && !textBoxFocused && Keyboard.Modifiers != ModifierKeys.Control:
                    Tool_Circle(sender, e);
                    e.Handled = true;
                    break;
                case Key.A when _selectionDone && !textBoxFocused:
                    Tool_Arrow(sender, e);
                    e.Handled = true;
                    break;
                case Key.L when _selectionDone && !textBoxFocused:
                    Tool_Line(sender, e);
                    e.Handled = true;
                    break;
                case Key.T when _selectionDone && !textBoxFocused:
                    Tool_Text(sender, e);
                    e.Handled = true;
                    break;
                case Key.M when _selectionDone && !textBoxFocused:
                    Tool_Mosaic(sender, e);
                    e.Handled = true;
                    break;
                case Key.P when _selectionDone && !textBoxFocused:
                    Tool_Pen(sender, e);
                    e.Handled = true;
                    break;
                case Key.H when _selectionDone && !textBoxFocused:
                    Tool_Highlighter(sender, e);
                    e.Handled = true;
                    break;
                case Key.N when _selectionDone && !textBoxFocused:
                    Tool_Number(sender, e);
                    e.Handled = true;
                    break;

                // アノテーション削除（TextBoxにフォーカスがある場合は無効）
                case Key.Delete when _selectionDone && _selectedAnnotation != null && !textBoxFocused:
                    _history.SaveState(_annotations);
                    _annotations.Remove(_selectedAnnotation);
                    _selectedAnnotation = null;
                    RedrawAnnotations();
                    e.Handled = true;
                    break;

                // コピー
                case Key.C when _selectionDone && Keyboard.Modifiers == ModifierKeys.Control:
                    Action_Copy(sender, e);
                    e.Handled = true;
                    break;

                // 保存
                case Key.S when _selectionDone && Keyboard.Modifiers == ModifierKeys.Control:
                    Action_Save(sender, e);
                    e.Handled = true;
                    break;

                // シーンに追加（TextBoxにフォーカスがある場合は無効）
                case Key.Enter when _selectionDone && !textBoxFocused:
                    Action_PinToScene(sender, e);
                    e.Handled = true;
                    break;
            }
        }

        #endregion

        #region Utilities

        private System.Drawing.Point ScaleToScreen(Point wpfPoint)
        {
            return new System.Drawing.Point(
                (int)(wpfPoint.X * _dpiScaleX),
                (int)(wpfPoint.Y * _dpiScaleY));
        }

        private void UpdateCursorForHandle(ResizeHandle handle)
        {
            Cursor = handle switch
            {
                ResizeHandle.TopLeft or ResizeHandle.BottomRight => Cursors.SizeNWSE,
                ResizeHandle.TopRight or ResizeHandle.BottomLeft => Cursors.SizeNESW,
                ResizeHandle.Top or ResizeHandle.Bottom => Cursors.SizeNS,
                ResizeHandle.Left or ResizeHandle.Right => Cursors.SizeWE,
                _ => Cursors.Arrow
            };
        }

        private void UpdateCursorForTool()
        {
            Cursor = _currentTool switch
            {
                CaptureDrawTool.Select => Cursors.Arrow,
                CaptureDrawTool.Text => Cursors.IBeam,
                CaptureDrawTool.Pen => Cursors.Pen,
                CaptureDrawTool.None => Cursors.Arrow,
                _ => Cursors.Cross
            };
        }

        private void ShowError(string message, Exception? ex = null)
        {
            var detail = ex != null ? $"\n\n詳細: {ex.Message}" : "";
            MessageBox.Show(this, message + detail, "エラー",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void ShowSuccess(string message)
        {
            // 短時間表示のトースト通知（将来的に実装）
            // 現時点ではログ出力のみ
            System.Diagnostics.Trace.TraceInformation($"[Success] {message}");
        }

        #endregion

        #region Cleanup

        protected override void OnClosed(EventArgs e)
        {
            _screenBitmap?.Dispose();
            base.OnClosed(e);
        }

        #endregion
    }
}

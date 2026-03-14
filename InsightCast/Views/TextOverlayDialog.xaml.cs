using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using InsightCast.Models;
using InsightCast.Services;

namespace InsightCast.Views
{
    public partial class TextOverlayDialog : Window
    {
        private List<TextOverlay> _overlays;
        private bool _isLoading;
        private string? _mediaPath;
        private int _defaultFontSize = 64;

        private static readonly (byte R, byte G, byte B, string Name)[] SwatchColors =
        {
            (255, 255, 255, "White"),
            (0, 0, 0, "Black"),
            (255, 0, 0, "Red"),
            (0, 0, 255, "Blue"),
            (255, 255, 0, "Yellow"),
            (212, 175, 55, "Gold"),
            (255, 105, 180, "Pink"),
            (0, 191, 255, "LightBlue"),
        };

        private static readonly string[] AvailableFonts =
        {
            "Yu Gothic UI",
            "Meiryo UI",
            "MS Gothic",
            "HGP創英角ﾎﾟｯﾌﾟ体",
            "HGS創英角ｺﾞｼｯｸUB",
            "HG丸ｺﾞｼｯｸM-PRO",
            "BIZ UDPGothic",
            "BIZ UDPMincho",
            "Arial",
            "Impact",
            "Segoe UI",
            "Consolas",
        };

        private static readonly string[] PositionKeys =
        {
            "top-left", "top-center", "top-right",
            "middle-left", "center", "middle-right",
            "bottom-left", "bottom-center", "bottom-right"
        };

        private static readonly (double X, double Y)[] PositionValues =
        {
            (15, 15), (50, 15), (85, 15),
            (15, 50), (50, 50), (85, 50),
            (15, 85), (50, 85), (85, 85)
        };

        public List<TextOverlay> ResultOverlays => _overlays;

        public TextOverlayDialog(List<TextOverlay> overlays, string? mediaPath, int defaultFontSize = 64)
        {
            InitializeComponent();
            _overlays = overlays.Select(CloneOverlay).ToList();
            _mediaPath = mediaPath;
            _defaultFontSize = defaultFontSize;
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            BuildFontCombo();
            BuildPositionGrid();
            BuildColorSwatches();
            BuildAlignmentCombo();
            LoadPreviewImage();
            RefreshList();
        }

        // ── Font ─────────────────────────────────────────────────────────

        private void BuildFontCombo()
        {
            FontFamilyCombo.Items.Clear();
            foreach (var font in AvailableFonts)
                FontFamilyCombo.Items.Add(font);
            FontFamilyCombo.SelectedIndex = 0;
        }

        // ── List ─────────────────────────────────────────────────────────

        private void RefreshList()
        {
            var items = new ObservableCollection<OverlayItem>();
            for (int i = 0; i < _overlays.Count; i++)
                items.Add(new OverlayItem(_overlays[i], i));

            OverlayList.ItemsSource = items;
            if (_overlays.Count > 0 && OverlayList.SelectedIndex < 0)
                OverlayList.SelectedIndex = 0;

            RefreshPreview();
        }

        private void OverlayList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            LoadSelectedOverlay();
        }

        private TextOverlay? GetSelected()
        {
            var idx = OverlayList.SelectedIndex;
            if (idx < 0 || idx >= _overlays.Count) return null;
            return _overlays[idx];
        }

        private void LoadSelectedOverlay()
        {
            var overlay = GetSelected();
            if (overlay == null) return;

            _isLoading = true;
            OverlayTextBox.Text = overlay.Text;
            XSlider.Value = overlay.XPercent;
            YSlider.Value = overlay.YPercent;
            XLabel.Text = $"{overlay.XPercent:F0}%";
            YLabel.Text = $"{overlay.YPercent:F0}%";
            FontSizeSlider.Value = overlay.FontSize;
            FontSizeLabel.Text = $"{overlay.FontSize}pt";
            OpacitySlider.Value = overlay.Opacity * 100;
            OpacityLabel.Text = $"{(int)(overlay.Opacity * 100)}%";

            // Font family
            var fontIdx = Array.IndexOf(AvailableFonts, overlay.FontFamily);
            FontFamilyCombo.SelectedIndex = fontIdx >= 0 ? fontIdx : 0;

            // Bold
            BoldToggle.IsChecked = overlay.FontBold;

            // Stroke
            StrokeSlider.Value = overlay.StrokeWidth;
            StrokeLabel.Text = overlay.StrokeWidth.ToString();

            // Alignment
            AlignmentCombo.SelectedIndex = overlay.Alignment switch
            {
                Models.TextAlignment.Left => 1,
                Models.TextAlignment.Right => 2,
                _ => 0
            };

            // Color highlight
            UpdateColorHighlight(overlay.TextColor);
            _isLoading = false;
        }

        // ── Add / Remove / Reorder ───────────────────────────────────────

        private void AddOverlay_Click(object sender, RoutedEventArgs e)
        {
            _overlays.Add(new TextOverlay { Text = LocalizationService.GetString("Overlay.DefaultText"), FontSize = _defaultFontSize });
            RefreshList();
            OverlayList.SelectedIndex = _overlays.Count - 1;
        }

        private void RemoveOverlay_Click(object sender, RoutedEventArgs e)
        {
            var idx = OverlayList.SelectedIndex;
            if (idx < 0 || idx >= _overlays.Count) return;
            _overlays.RemoveAt(idx);
            RefreshList();
            if (_overlays.Count > 0)
                OverlayList.SelectedIndex = Math.Min(idx, _overlays.Count - 1);
        }

        private void MoveUp_Click(object sender, RoutedEventArgs e)
        {
            var idx = OverlayList.SelectedIndex;
            if (idx <= 0) return;
            (_overlays[idx - 1], _overlays[idx]) = (_overlays[idx], _overlays[idx - 1]);
            RefreshList();
            OverlayList.SelectedIndex = idx - 1;
        }

        private void MoveDown_Click(object sender, RoutedEventArgs e)
        {
            var idx = OverlayList.SelectedIndex;
            if (idx < 0 || idx >= _overlays.Count - 1) return;
            (_overlays[idx], _overlays[idx + 1]) = (_overlays[idx + 1], _overlays[idx]);
            RefreshList();
            OverlayList.SelectedIndex = idx + 1;
        }

        // ── Editor events ────────────────────────────────────────────────

        private void OverlayText_Changed(object sender, TextChangedEventArgs e)
        {
            if (_isLoading) return;
            var overlay = GetSelected();
            if (overlay == null) return;
            overlay.Text = OverlayTextBox.Text;
            UpdateListLabel();
            RefreshPreview();
        }

        private void XSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var overlay = GetSelected();
            if (overlay == null) return;
            overlay.XPercent = Math.Round(XSlider.Value, 1);
            XLabel.Text = $"{overlay.XPercent:F0}%";
            RefreshPreview();
        }

        private void YSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var overlay = GetSelected();
            if (overlay == null) return;
            overlay.YPercent = Math.Round(YSlider.Value, 1);
            YLabel.Text = $"{overlay.YPercent:F0}%";
            RefreshPreview();
        }

        private void FontSize_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var overlay = GetSelected();
            if (overlay == null) return;
            overlay.FontSize = (int)FontSizeSlider.Value;
            FontSizeLabel.Text = $"{overlay.FontSize}pt";
            RefreshPreview();
        }

        private void SizePreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string s && int.TryParse(s, out var size))
            {
                FontSizeSlider.Value = size;
            }
        }

        private void FontFamily_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            var overlay = GetSelected();
            if (overlay == null) return;
            var idx = FontFamilyCombo.SelectedIndex;
            if (idx >= 0 && idx < AvailableFonts.Length)
            {
                overlay.FontFamily = AvailableFonts[idx];
                RefreshPreview();
            }
        }

        private void Bold_Click(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var overlay = GetSelected();
            if (overlay == null) return;
            overlay.FontBold = BoldToggle.IsChecked == true;
            RefreshPreview();
        }

        private void Stroke_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var overlay = GetSelected();
            if (overlay == null) return;
            overlay.StrokeWidth = (int)StrokeSlider.Value;
            StrokeLabel.Text = overlay.StrokeWidth.ToString();
            RefreshPreview();
        }

        private void Alignment_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            var overlay = GetSelected();
            if (overlay == null) return;
            overlay.Alignment = AlignmentCombo.SelectedIndex switch
            {
                1 => Models.TextAlignment.Left,
                2 => Models.TextAlignment.Right,
                _ => Models.TextAlignment.Center
            };
            RefreshPreview();
        }

        private void Opacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var overlay = GetSelected();
            if (overlay == null) return;
            overlay.Opacity = OpacitySlider.Value / 100.0;
            OpacityLabel.Text = $"{(int)OpacitySlider.Value}%";
            RefreshPreview();
        }

        // ── Position grid ────────────────────────────────────────────────

        private void BuildPositionGrid()
        {
            PositionGrid.Children.Clear();
            for (int i = 0; i < 9; i++)
            {
                var pos = PositionValues[i];
                var isCenter = i == 4;
                var btn = new Button();
                btn.Tag = i;
                btn.Template = CreatePositionButtonTemplate(isCenter);
                btn.ToolTip = $"X:{pos.X:F0}%  Y:{pos.Y:F0}%";
                btn.Click += PositionButton_Click;
                PositionGrid.Children.Add(btn);
            }
        }

        private ControlTemplate CreatePositionButtonTemplate(bool isCenter)
        {
            var template = new ControlTemplate(typeof(Button));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.Name = "Bd";
            borderFactory.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            borderFactory.SetValue(Border.CursorProperty, Cursors.Hand);

            var ellipseFactory = new FrameworkElementFactory(typeof(Ellipse));
            ellipseFactory.SetValue(Ellipse.WidthProperty, isCenter ? 12.0 : 9.0);
            ellipseFactory.SetValue(Ellipse.HeightProperty, isCenter ? 12.0 : 9.0);
            ellipseFactory.SetValue(Ellipse.FillProperty, isCenter
                ? new SolidColorBrush(Color.FromRgb(0xB8, 0x94, 0x2F))
                : new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF)));
            ellipseFactory.SetValue(Ellipse.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            ellipseFactory.SetValue(Ellipse.VerticalAlignmentProperty, VerticalAlignment.Center);

            borderFactory.AppendChild(ellipseFactory);
            template.VisualTree = borderFactory;

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty,
                new SolidColorBrush(Color.FromArgb(0x30, 0xB8, 0x94, 0x2F)), "Bd"));
            template.Triggers.Add(hoverTrigger);

            return template;
        }

        private void PositionButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int idx)
            {
                var overlay = GetSelected();
                if (overlay == null) return;

                var pos = PositionValues[idx];
                _isLoading = true;
                overlay.XPercent = pos.X;
                overlay.YPercent = pos.Y;
                XSlider.Value = pos.X;
                YSlider.Value = pos.Y;
                XLabel.Text = $"{pos.X:F0}%";
                YLabel.Text = $"{pos.Y:F0}%";
                _isLoading = false;
                RefreshPreview();
            }
        }

        // ── Color swatches ───────────────────────────────────────────────

        private void BuildColorSwatches()
        {
            ColorSwatchPanel.Children.Clear();
            for (int i = 0; i < SwatchColors.Length; i++)
            {
                var (r, g, b, name) = SwatchColors[i];
                var idx = i;
                var swatch = new Border
                {
                    Width = 24,
                    Height = 24,
                    CornerRadius = new CornerRadius(12),
                    Margin = new Thickness(2),
                    Cursor = Cursors.Hand,
                    Background = new SolidColorBrush(Color.FromRgb(r, g, b)),
                    BorderThickness = new Thickness(2),
                    BorderBrush = (r == 255 && g == 255 && b == 255)
                        ? new SolidColorBrush(Color.FromRgb(180, 180, 180))
                        : new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                    ToolTip = name
                };
                swatch.MouseLeftButtonDown += (s, e) =>
                {
                    var overlay = GetSelected();
                    if (overlay == null) return;
                    overlay.TextColor = new[] { (int)r, (int)g, (int)b };
                    UpdateColorHighlight(overlay.TextColor);
                    UpdateListLabel();
                    RefreshPreview();
                };
                ColorSwatchPanel.Children.Add(swatch);
            }
        }

        private void UpdateColorHighlight(int[] selectedColor)
        {
            for (int i = 0; i < ColorSwatchPanel.Children.Count; i++)
            {
                if (ColorSwatchPanel.Children[i] is Border border)
                {
                    var (r, g, b, _) = SwatchColors[i];
                    bool isSelected = selectedColor[0] == r && selectedColor[1] == g && selectedColor[2] == b;
                    border.BorderBrush = isSelected
                        ? new SolidColorBrush(Color.FromRgb(0xB8, 0x94, 0x2F))
                        : (r == 255 && g == 255 && b == 255)
                            ? new SolidColorBrush(Color.FromRgb(180, 180, 180))
                            : new SolidColorBrush(Color.FromRgb(200, 200, 200));
                    border.BorderThickness = isSelected ? new Thickness(3) : new Thickness(2);
                }
            }
        }

        // ── Alignment combo ──────────────────────────────────────────────

        private void BuildAlignmentCombo()
        {
            AlignmentCombo.Items.Clear();
            AlignmentCombo.Items.Add(LocalizationService.GetString("Align.Center"));
            AlignmentCombo.Items.Add(LocalizationService.GetString("Align.Left"));
            AlignmentCombo.Items.Add(LocalizationService.GetString("Align.Right"));
            AlignmentCombo.SelectedIndex = 0;
        }

        // ── Preview ──────────────────────────────────────────────────────

        private void LoadPreviewImage()
        {
            if (!string.IsNullOrEmpty(_mediaPath) && System.IO.File.Exists(_mediaPath))
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(_mediaPath);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    PreviewImage.Source = bitmap;
                }
                catch { }
            }
        }

        private void RefreshPreview()
        {
            PreviewCanvas.Children.Clear();
            var canvasWidth = PreviewCanvas.ActualWidth > 0 ? PreviewCanvas.ActualWidth : 480;
            var canvasHeight = PreviewCanvas.ActualHeight > 0 ? PreviewCanvas.ActualHeight : 160;

            foreach (var overlay in _overlays)
            {
                if (!overlay.HasText) continue;

                var scale = canvasHeight / 1080.0;
                var tb = new TextBlock
                {
                    Text = overlay.Text,
                    FontFamily = new FontFamily(overlay.FontFamily),
                    FontSize = Math.Max(8, overlay.FontSize * scale),
                    FontWeight = overlay.FontBold ? FontWeights.Bold : FontWeights.Normal,
                    Foreground = new SolidColorBrush(Color.FromRgb(
                        (byte)overlay.TextColor[0],
                        (byte)overlay.TextColor[1],
                        (byte)overlay.TextColor[2])),
                    Opacity = overlay.Opacity,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = canvasWidth * 0.8,
                    Effect = new DropShadowEffect
                    {
                        Color = Color.FromRgb(
                            (byte)overlay.StrokeColor[0],
                            (byte)overlay.StrokeColor[1],
                            (byte)overlay.StrokeColor[2]),
                        BlurRadius = overlay.StrokeWidth * 2,
                        ShadowDepth = overlay.ShadowEnabled ? 2 : 0,
                        Opacity = 0.9
                    }
                };

                tb.Measure(new Size(canvasWidth * 0.8, double.PositiveInfinity));
                var textWidth = tb.DesiredSize.Width;
                var textHeight = tb.DesiredSize.Height;

                var x = (overlay.XPercent / 100.0) * canvasWidth - textWidth / 2;
                var y = (overlay.YPercent / 100.0) * canvasHeight - textHeight / 2;

                Canvas.SetLeft(tb, Math.Max(0, x));
                Canvas.SetTop(tb, Math.Max(0, y));
                PreviewCanvas.Children.Add(tb);
            }
        }

        private void UpdateListLabel()
        {
            var idx = OverlayList.SelectedIndex;
            if (idx < 0 || idx >= _overlays.Count) return;
            var items = OverlayList.ItemsSource as ObservableCollection<OverlayItem>;
            if (items != null && idx < items.Count)
            {
                _isLoading = true;
                items[idx] = new OverlayItem(_overlays[idx], idx);
                OverlayList.SelectedIndex = idx;
                _isLoading = false;
            }
        }

        // ── OK / Cancel ──────────────────────────────────────────────────

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private static TextOverlay CloneOverlay(TextOverlay source)
        {
            return new TextOverlay
            {
                Id = source.Id,
                Text = source.Text,
                XPercent = source.XPercent,
                YPercent = source.YPercent,
                FontFamily = source.FontFamily,
                FontSize = source.FontSize,
                FontBold = source.FontBold,
                TextColor = (int[])source.TextColor.Clone(),
                StrokeColor = (int[])source.StrokeColor.Clone(),
                StrokeWidth = source.StrokeWidth,
                Alignment = source.Alignment,
                ShadowEnabled = source.ShadowEnabled,
                ShadowColor = (int[])source.ShadowColor.Clone(),
                ShadowOffset = (int[])source.ShadowOffset.Clone(),
                Opacity = source.Opacity
            };
        }

        // ── Inner types ──────────────────────────────────────────────────

        public class OverlayItem
        {
            public string Label { get; }
            public Color ColorValue { get; }

            public OverlayItem(TextOverlay overlay, int index)
            {
                var text = string.IsNullOrWhiteSpace(overlay.Text)
                    ? LocalizationService.GetString("Overlay.Empty")
                    : overlay.Text;
                if (text.Length > 20) text = text[..20] + "...";
                Label = $"[{index + 1}] {text}";
                ColorValue = overlay.TextColor is { Length: >= 3 }
                    ? Color.FromRgb((byte)overlay.TextColor[0], (byte)overlay.TextColor[1], (byte)overlay.TextColor[2])
                    : Colors.White;
            }
        }
    }

    // Value converter: null → Collapsed
    public class NullToCollapsedConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => value != null ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotSupportedException();
    }
}

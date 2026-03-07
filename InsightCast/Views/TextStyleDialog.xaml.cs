using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using InsightCast.Models;

namespace InsightCast.Views
{
    public partial class TextStyleDialog : Window
    {
        private TextStyle _currentStyle;
        private int _selectedPresetIndex = -1;
        private bool _isUpdating;

        /// <summary>
        /// The current text style being edited.
        /// </summary>
        public TextStyle CurrentStyle
        {
            get => _currentStyle;
            set
            {
                _currentStyle = value;
                LoadStyleIntoControls();
                UpdatePreview();
            }
        }

        public TextStyleDialog()
        {
            InitializeComponent();
            _currentStyle = CloneStyle(TextStyle.PRESET_STYLES[0]);
            Loaded += TextStyleDialog_Loaded;
        }

        public TextStyleDialog(TextStyle? initialStyle) : this()
        {
            if (initialStyle != null)
            {
                _currentStyle = CloneStyle(initialStyle);
            }
        }

        private void TextStyleDialog_Loaded(object sender, RoutedEventArgs e)
        {
            InitializeFontComboBox();
            InitializeColorPalettes();
            BuildPresetGrid();
            LoadStyleIntoControls();
            UpdatePreview();
        }

        /// <summary>
        /// Gets the selected text style (convenience property).
        /// </summary>
        public TextStyle SelectedStyle => CloneStyle(_currentStyle);

        /// <summary>
        /// Returns the selected text style.
        /// </summary>
        public TextStyle GetSelectedStyle()
        {
            return CloneStyle(_currentStyle);
        }

        // ── Initialization ──────────────────────────────────────────────

        private void InitializeFontComboBox()
        {
            _isUpdating = true;
            FontComboBox.Items.Clear();
            foreach (var (fontName, displayName) in TextStyle.AVAILABLE_FONTS)
            {
                var item = new ComboBoxItem
                {
                    Content = $"{displayName} ({fontName})",
                    Tag = fontName
                };
                FontComboBox.Items.Add(item);
            }
            _isUpdating = false;
        }

        private void InitializeColorPalettes()
        {
            TextColorPanel.Children.Clear();
            foreach (var (name, color) in TextStyle.PRESET_TEXT_COLORS)
            {
                var border = CreateColorButton(color, name);
                border.MouseLeftButtonDown += (s, e) =>
                {
                    _currentStyle.TextColor = (int[])color.Clone();
                    HighlightSelectedColor(TextColorPanel, color);
                    UpdatePreview();
                };
                TextColorPanel.Children.Add(border);
            }

            StrokeColorPanel.Children.Clear();
            foreach (var (name, color) in TextStyle.PRESET_STROKE_COLORS)
            {
                var border = CreateColorButton(color, name);
                border.MouseLeftButtonDown += (s, e) =>
                {
                    _currentStyle.StrokeColor = (int[])color.Clone();
                    HighlightSelectedColor(StrokeColorPanel, color);
                    UpdatePreview();
                };
                StrokeColorPanel.Children.Add(border);
            }
        }

        private Border CreateColorButton(int[] color, string tooltip)
        {
            var brush = new SolidColorBrush(Color.FromRgb(
                (byte)color[0], (byte)color[1], (byte)color[2]));

            var border = new Border
            {
                Width = 28,
                Height = 28,
                Margin = new Thickness(2),
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(2),
                BorderBrush = (System.Windows.Media.Brush)FindResource("BorderDefault"),
                Background = brush,
                Cursor = Cursors.Hand,
                ToolTip = tooltip,
                Tag = color
            };

            return border;
        }

        private void HighlightSelectedColor(WrapPanel panel, int[] selectedColor)
        {
            foreach (var child in panel.Children)
            {
                if (child is Border border && border.Tag is int[] c)
                {
                    bool isSelected = c[0] == selectedColor[0]
                                      && c[1] == selectedColor[1]
                                      && c[2] == selectedColor[2];
                    border.BorderBrush = isSelected
                        ? new SolidColorBrush(Color.FromRgb(0x00, 0x9E, 0xFF))
                        : new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
                    border.BorderThickness = isSelected
                        ? new Thickness(3)
                        : new Thickness(2);
                }
            }
        }

        private void BuildPresetGrid()
        {
            PresetGrid.Items.Clear();
            for (int i = 0; i < TextStyle.PRESET_STYLES.Count; i++)
            {
                var preset = TextStyle.PRESET_STYLES[i];
                var index = i;

                var textBrush = new SolidColorBrush(Color.FromRgb(
                    (byte)preset.TextColor[0], (byte)preset.TextColor[1], (byte)preset.TextColor[2]));
                var strokeBrush = new SolidColorBrush(Color.FromRgb(
                    (byte)preset.StrokeColor[0], (byte)preset.StrokeColor[1], (byte)preset.StrokeColor[2]));

                var previewLabel = new TextBlock
                {
                    Text = "Aa",
                    FontSize = 22,
                    FontWeight = preset.FontBold ? FontWeights.Bold : FontWeights.Normal,
                    FontFamily = new FontFamily(preset.FontFamily),
                    Foreground = textBrush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Effect = new DropShadowEffect
                    {
                        Color = Color.FromRgb(
                            (byte)preset.StrokeColor[0],
                            (byte)preset.StrokeColor[1],
                            (byte)preset.StrokeColor[2]),
                        ShadowDepth = 1,
                        BlurRadius = 3,
                        Opacity = 0.9
                    }
                };

                var nameLabel = new TextBlock
                {
                    Text = preset.Name,
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 2, 0, 0)
                };

                var stack = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                stack.Children.Add(previewLabel);
                stack.Children.Add(nameLabel);

                var border = new Border
                {
                    Width = 100,
                    Height = 72,
                    Margin = new Thickness(4),
                    CornerRadius = new CornerRadius(4),
                    BorderThickness = new Thickness(2),
                    BorderBrush = (System.Windows.Media.Brush)FindResource("BorderDefault"),
                    Background = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)),
                    Cursor = Cursors.Hand,
                    Child = stack,
                    Tag = index
                };

                border.MouseLeftButtonDown += PresetBorder_Click;
                PresetGrid.Items.Add(border);
            }
        }

        // ── Event Handlers ──────────────────────────────────────────────

        private void PresetBorder_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is int index)
            {
                _selectedPresetIndex = index;
                var currentFontSize = _currentStyle.FontSize;
                _currentStyle = CloneStyle(TextStyle.PRESET_STYLES[index]);
                _currentStyle.FontSize = currentFontSize;

                // Highlight selected preset
                foreach (var item in PresetGrid.Items)
                {
                    if (item is Border b)
                    {
                        var isSelected = b.Tag is int idx && idx == index;
                        b.BorderBrush = isSelected
                            ? new SolidColorBrush(Color.FromRgb(0x00, 0x9E, 0xFF))
                            : new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
                        b.BorderThickness = isSelected
                            ? new Thickness(3)
                            : new Thickness(2);
                    }
                }

                LoadStyleIntoControls();
                UpdatePreview();
            }
        }

        private void FontComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdating) return;
            if (FontComboBox.SelectedItem is ComboBoxItem item && item.Tag is string fontName)
            {
                _currentStyle.FontFamily = fontName;
                UpdatePreview();
            }
        }

        private void SizeDown_Click(object sender, RoutedEventArgs e)
        {
            var newSize = Math.Max(24, _currentStyle.FontSize - 2);
            _currentStyle.FontSize = newSize;
            _isUpdating = true;
            SizeTextBox.Text = newSize.ToString();
            _isUpdating = false;
            UpdatePreview();
        }

        private void SizeUp_Click(object sender, RoutedEventArgs e)
        {
            var newSize = Math.Min(96, _currentStyle.FontSize + 2);
            _currentStyle.FontSize = newSize;
            _isUpdating = true;
            SizeTextBox.Text = newSize.ToString();
            _isUpdating = false;
            UpdatePreview();
        }

        private void SizeTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdating) return;
            if (int.TryParse(SizeTextBox.Text, out int size))
            {
                size = Math.Clamp(size, 24, 96);
                _currentStyle.FontSize = size;
                UpdatePreview();
            }
        }

        private void StrokeWidthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdating) return;
            var value = (int)StrokeWidthSlider.Value;
            _currentStyle.StrokeWidth = value;
            if (StrokeWidthLabel != null)
                StrokeWidthLabel.Text = value.ToString();
            UpdatePreview();
        }

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

        // ── Preview & Loading ───────────────────────────────────────────

        private void LoadStyleIntoControls()
        {
            _isUpdating = true;
            try
            {
                // Font combo
                for (int i = 0; i < FontComboBox.Items.Count; i++)
                {
                    if (FontComboBox.Items[i] is ComboBoxItem item
                        && item.Tag is string fontName
                        && fontName == _currentStyle.FontFamily)
                    {
                        FontComboBox.SelectedIndex = i;
                        break;
                    }
                }

                // Size
                SizeTextBox.Text = _currentStyle.FontSize.ToString();

                // Stroke width
                StrokeWidthSlider.Value = _currentStyle.StrokeWidth;
                StrokeWidthLabel.Text = _currentStyle.StrokeWidth.ToString();

                // Highlight matching text color
                HighlightSelectedColor(TextColorPanel, _currentStyle.TextColor);
                HighlightSelectedColor(StrokeColorPanel, _currentStyle.StrokeColor);
            }
            finally
            {
                _isUpdating = false;
            }
        }

        private void UpdatePreview()
        {
            if (PreviewText == null) return;

            PreviewText.FontFamily = new FontFamily(_currentStyle.FontFamily);
            PreviewText.FontSize = _currentStyle.FontSize;
            PreviewText.FontWeight = _currentStyle.FontBold ? FontWeights.Bold : FontWeights.Normal;

            PreviewTextBrush.Color = Color.FromRgb(
                (byte)_currentStyle.TextColor[0],
                (byte)_currentStyle.TextColor[1],
                (byte)_currentStyle.TextColor[2]);

            if (_currentStyle.ShadowEnabled && PreviewShadow != null)
            {
                PreviewShadow.Color = Color.FromRgb(
                    (byte)_currentStyle.StrokeColor[0],
                    (byte)_currentStyle.StrokeColor[1],
                    (byte)_currentStyle.StrokeColor[2]);
                PreviewShadow.ShadowDepth = _currentStyle.StrokeWidth;
                PreviewShadow.BlurRadius = _currentStyle.StrokeWidth + 2;
                PreviewShadow.Opacity = 1.0;
            }
            else if (PreviewShadow != null)
            {
                // Use stroke color as shadow even when shadow is disabled, to simulate outline
                PreviewShadow.Color = Color.FromRgb(
                    (byte)_currentStyle.StrokeColor[0],
                    (byte)_currentStyle.StrokeColor[1],
                    (byte)_currentStyle.StrokeColor[2]);
                PreviewShadow.ShadowDepth = 0;
                PreviewShadow.BlurRadius = _currentStyle.StrokeWidth * 2;
                PreviewShadow.Opacity = _currentStyle.StrokeWidth > 0 ? 1.0 : 0.0;
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────

        private static TextStyle CloneStyle(TextStyle source)
        {
            return new TextStyle
            {
                Id = source.Id,
                Name = source.Name,
                FontFamily = source.FontFamily,
                FontSize = source.FontSize,
                FontBold = source.FontBold,
                TextColor = (int[])source.TextColor.Clone(),
                StrokeColor = (int[])source.StrokeColor.Clone(),
                StrokeWidth = source.StrokeWidth,
                BackgroundColor = (int[])source.BackgroundColor.Clone(),
                BackgroundOpacity = source.BackgroundOpacity,
                ShadowEnabled = source.ShadowEnabled,
                ShadowColor = (int[])source.ShadowColor.Clone(),
                ShadowOffset = (int[])source.ShadowOffset.Clone()
            };
        }
    }
}

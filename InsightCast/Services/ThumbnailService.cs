using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;

namespace InsightCast.Services
{
    /// <summary>
    /// YouTube thumbnail pattern types
    /// </summary>
    public enum ThumbnailPattern
    {
        /// <summary>人物の感情強調型 - Emotional face emphasis</summary>
        EmotionalFace,
        /// <summary>パワーワード中央配置型 - Power word centered</summary>
        PowerWord,
        /// <summary>ビフォーアフター比較型 - Before/After comparison</summary>
        BeforeAfter,
        /// <summary>ブラックボックス型 - Black box/hidden</summary>
        BlackBox,
        /// <summary>4分割グリッド型 - 4-split grid</summary>
        Grid4,
        /// <summary>数字・実績特化型 - Numbers/achievements</summary>
        NumbersFocus,
        /// <summary>メイン＆サブ2段構成型 - Main & Sub 2-tier</summary>
        MainSub,
        /// <summary>3行インパクト型 - 3-line impact (small/medium/large)</summary>
        ThreeLine,
        /// <summary>集中線＆ターゲット強調型 - Focus lines emphasis</summary>
        FocusLines,
        /// <summary>疑問・問いかけ型 - Question/inquiry</summary>
        Question,
        /// <summary>ジャンル特化エステティック型 - Genre aesthetic</summary>
        GenreAesthetic
    }

    /// <summary>
    /// Thumbnail pattern definition
    /// </summary>
    public class ThumbnailPatternInfo
    {
        public ThumbnailPattern Pattern { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Effect { get; set; } = string.Empty;
        public string Usage { get; set; } = string.Empty;
        public string ColorHex { get; set; } = "#FFD700"; // Default gold

        /// <summary>WPF Brush for data binding</summary>
        public System.Windows.Media.Brush ColorBrush
        {
            get
            {
                try
                {
                    var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(ColorHex);
                    return new System.Windows.Media.SolidColorBrush(color);
                }
                catch
                {
                    return System.Windows.Media.Brushes.Gold;
                }
            }
        }
    }

    /// <summary>
    /// Professional style presets
    /// </summary>
    public enum ThumbnailStyle
    {
        /// <summary>カスタム（手動設定）</summary>
        Custom,
        /// <summary>ビジネス・インパクト - 黄色×黒の3重フチ、集中線</summary>
        BusinessImpact,
        /// <summary>信頼・マニュアル - 青い座布団、高可読性</summary>
        TrustManual,
        /// <summary>衝撃・暴露 - 赤い斜め文字、ダーク背景</summary>
        ShockReveal,
        /// <summary>エレガント - グラデーション、上品</summary>
        Elegant,
        /// <summary>ポップ - カラフル、明るい</summary>
        Pop
    }

    /// <summary>
    /// Thumbnail generation settings
    /// </summary>
    public class ThumbnailSettings
    {
        public ThumbnailPattern Pattern { get; set; } = ThumbnailPattern.PowerWord;
        public ThumbnailStyle Style { get; set; } = ThumbnailStyle.Custom;
        public string MainText { get; set; } = string.Empty;
        public string SubText { get; set; } = string.Empty;
        public string SubSubText { get; set; } = string.Empty;
        public string? BackgroundImagePath { get; set; }
        public Color BackgroundColor { get; set; } = Color.FromArgb(30, 30, 30);
        public Color MainTextColor { get; set; } = Color.Yellow;
        public Color SubTextColor { get; set; } = Color.White;
        public Color SubSubTextColor { get; set; } = Color.LightGray;
        public Color StrokeColor { get; set; } = Color.Black;
        public int StrokeWidth { get; set; } = 8;
        public string? LogoPath { get; set; }
        public string FontFamily { get; set; } = "Yu Gothic UI";

        // Per-line font families (null = use default FontFamily)
        public string? MainFontFamily { get; set; }
        public string? SubFontFamily { get; set; }
        public string? SubSubFontFamily { get; set; }

        // Per-line font sizes (0 = auto size)
        public int MainFontSize { get; set; } = 0;
        public int SubFontSize { get; set; } = 0;
        public int SubSubFontSize { get; set; } = 0;

        // Professional effects
        public bool UseTripleStroke { get; set; } = true;
        public Color OuterStrokeColor { get; set; } = Color.Red;
        public bool UseTextCushion { get; set; } = false;
        public Color CushionColor { get; set; } = Color.FromArgb(200, 0, 50, 100);
        public bool UseGradientText { get; set; } = false;
        public bool UseSlantedText { get; set; } = false;
        public float SlantAngle { get; set; } = -5f;
        public bool UseFocusLines { get; set; } = false;
        public bool UseDarkOverlay { get; set; } = false;
    }

    /// <summary>
    /// Style preset info
    /// </summary>
    public class StylePresetInfo
    {
        public ThumbnailStyle Style { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ColorHex { get; set; } = "#FFD700";

        public System.Windows.Media.Brush ColorBrush
        {
            get
            {
                try
                {
                    var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(ColorHex);
                    return new System.Windows.Media.SolidColorBrush(color);
                }
                catch
                {
                    return System.Windows.Media.Brushes.Gold;
                }
            }
        }
    }

    /// <summary>
    /// YouTube thumbnail generator service
    /// </summary>
    public class ThumbnailService
    {
        private const int WIDTH = 1280;
        private const int HEIGHT = 720;

        public static List<StylePresetInfo> GetStylePresets()
        {
            return new List<StylePresetInfo>
            {
                new StylePresetInfo
                {
                    Style = ThumbnailStyle.Custom,
                    Name = "カスタム",
                    Description = "手動で全ての設定を調整",
                    ColorHex = "#808080"
                },
                new StylePresetInfo
                {
                    Style = ThumbnailStyle.BusinessImpact,
                    Name = "ビジネス・インパクト",
                    Description = "黄色×黒の3重フチ、集中線で注目を集める",
                    ColorHex = "#FFD700"
                },
                new StylePresetInfo
                {
                    Style = ThumbnailStyle.TrustManual,
                    Name = "信頼・マニュアル",
                    Description = "青い座布団で可読性と信頼感を両立",
                    ColorHex = "#4169E1"
                },
                new StylePresetInfo
                {
                    Style = ThumbnailStyle.ShockReveal,
                    Name = "衝撃・暴露",
                    Description = "赤い斜め文字、ダーク背景で緊迫感",
                    ColorHex = "#DC143C"
                },
                new StylePresetInfo
                {
                    Style = ThumbnailStyle.Elegant,
                    Name = "エレガント",
                    Description = "グラデーションと上品な配色",
                    ColorHex = "#9370DB"
                },
                new StylePresetInfo
                {
                    Style = ThumbnailStyle.Pop,
                    Name = "ポップ",
                    Description = "カラフルで明るい、親しみやすい",
                    ColorHex = "#FF69B4"
                }
            };
        }

        public static ThumbnailSettings ApplyStylePreset(ThumbnailSettings settings, ThumbnailStyle style)
        {
            settings.Style = style;

            switch (style)
            {
                case ThumbnailStyle.BusinessImpact:
                    settings.MainTextColor = Color.Yellow;
                    settings.StrokeColor = Color.Black;
                    settings.OuterStrokeColor = Color.FromArgb(200, 0, 0);
                    settings.UseTripleStroke = true;
                    settings.UseFocusLines = true;
                    settings.UseTextCushion = false;
                    settings.UseSlantedText = false;
                    settings.StrokeWidth = 12;
                    break;

                case ThumbnailStyle.TrustManual:
                    settings.MainTextColor = Color.White;
                    settings.StrokeColor = Color.FromArgb(0, 50, 100);
                    settings.UseTripleStroke = false;
                    settings.UseTextCushion = true;
                    settings.CushionColor = Color.FromArgb(220, 0, 80, 160);
                    settings.UseFocusLines = false;
                    settings.UseSlantedText = false;
                    settings.StrokeWidth = 6;
                    break;

                case ThumbnailStyle.ShockReveal:
                    settings.MainTextColor = Color.Red;
                    settings.StrokeColor = Color.White;
                    settings.OuterStrokeColor = Color.Black;
                    settings.UseTripleStroke = true;
                    settings.UseSlantedText = true;
                    settings.SlantAngle = -8f;
                    settings.UseDarkOverlay = true;
                    settings.UseFocusLines = false;
                    settings.StrokeWidth = 10;
                    break;

                case ThumbnailStyle.Elegant:
                    settings.MainTextColor = Color.White;
                    settings.StrokeColor = Color.FromArgb(80, 60, 120);
                    settings.UseTripleStroke = false;
                    settings.UseGradientText = true;
                    settings.UseTextCushion = false;
                    settings.UseFocusLines = false;
                    settings.StrokeWidth = 4;
                    break;

                case ThumbnailStyle.Pop:
                    settings.MainTextColor = Color.HotPink;
                    settings.StrokeColor = Color.White;
                    settings.OuterStrokeColor = Color.DeepPink;
                    settings.UseTripleStroke = true;
                    settings.UseFocusLines = false;
                    settings.UseSlantedText = false;
                    settings.StrokeWidth = 8;
                    break;

                default: // Custom
                    break;
            }

            return settings;
        }

        public static List<ThumbnailPatternInfo> GetPatterns()
        {
            // 現代YouTubeスタイル - シンプルで大胆
            return new List<ThumbnailPatternInfo>
            {
                new ThumbnailPatternInfo
                {
                    Pattern = ThumbnailPattern.PowerWord,
                    Name = "デカ文字",
                    Description = "超大きな文字で一発インパクト",
                    Effect = "スクロール中でも目に飛び込む",
                    Usage = "ニュース、暴露、衝撃系",
                    ColorHex = "#FFD93D"
                },
                new ThumbnailPatternInfo
                {
                    Pattern = ThumbnailPattern.MainSub,
                    Name = "上下2段",
                    Description = "メイン＋補足の2段構成",
                    Effect = "情報を整理して伝える",
                    Usage = "解説、レビュー、比較",
                    ColorHex = "#5C7AEA"
                },
                new ThumbnailPatternInfo
                {
                    Pattern = ThumbnailPattern.ThreeLine,
                    Name = "3行インパクト",
                    Description = "上段小・中段中・下段大の3行",
                    Effect = "ニュース風の階層構造",
                    Usage = "ニュース、暴露、衝撃系",
                    ColorHex = "#FF4444"
                }
            };
        }

        public string GenerateThumbnail(ThumbnailSettings settings, string outputPath)
        {
            using var bitmap = new Bitmap(WIDTH, HEIGHT);
            using var graphics = Graphics.FromImage(bitmap);

            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;

            // Draw background
            DrawBackground(graphics, settings);

            // Draw pattern-specific elements
            switch (settings.Pattern)
            {
                case ThumbnailPattern.PowerWord:
                    DrawPowerWordPattern(graphics, settings);
                    break;
                case ThumbnailPattern.BeforeAfter:
                    DrawBeforeAfterPattern(graphics, settings);
                    break;
                case ThumbnailPattern.BlackBox:
                    DrawBlackBoxPattern(graphics, settings);
                    break;
                case ThumbnailPattern.Grid4:
                    DrawGrid4Pattern(graphics, settings);
                    break;
                case ThumbnailPattern.NumbersFocus:
                    DrawNumbersFocusPattern(graphics, settings);
                    break;
                case ThumbnailPattern.MainSub:
                    DrawMainSubPattern(graphics, settings);
                    break;
                case ThumbnailPattern.ThreeLine:
                    DrawThreeLinePattern(graphics, settings);
                    break;
                case ThumbnailPattern.FocusLines:
                    DrawFocusLinesPattern(graphics, settings);
                    break;
                case ThumbnailPattern.Question:
                    DrawQuestionPattern(graphics, settings);
                    break;
                case ThumbnailPattern.GenreAesthetic:
                    DrawGenreAestheticPattern(graphics, settings);
                    break;
                case ThumbnailPattern.EmotionalFace:
                default:
                    DrawEmotionalFacePattern(graphics, settings);
                    break;
            }

            // Draw logo if provided
            if (!string.IsNullOrEmpty(settings.LogoPath) && File.Exists(settings.LogoPath))
            {
                DrawLogo(graphics, settings.LogoPath);
            }

            // Save
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
            bitmap.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
            return outputPath;
        }

        private void DrawBackground(Graphics g, ThumbnailSettings settings)
        {
            if (!string.IsNullOrEmpty(settings.BackgroundImagePath) && File.Exists(settings.BackgroundImagePath))
            {
                using var bgImage = Image.FromFile(settings.BackgroundImagePath);
                g.DrawImage(bgImage, 0, 0, WIDTH, HEIGHT);

                // Add dark overlay for text visibility
                var overlayAlpha = settings.UseDarkOverlay ? 150 : 80;
                using var overlay = new SolidBrush(Color.FromArgb(overlayAlpha, 0, 0, 0));
                g.FillRectangle(overlay, 0, 0, WIDTH, HEIGHT);
            }
            else
            {
                using var brush = new SolidBrush(settings.BackgroundColor);
                g.FillRectangle(brush, 0, 0, WIDTH, HEIGHT);

                // Dark overlay on solid color if requested
                if (settings.UseDarkOverlay)
                {
                    using var overlay = new SolidBrush(Color.FromArgb(100, 0, 0, 0));
                    g.FillRectangle(overlay, 0, 0, WIDTH, HEIGHT);
                }
            }

            // Draw focus lines if enabled
            if (settings.UseFocusLines)
            {
                DrawFocusLinesBackground(g);
            }
        }

        private void DrawFocusLinesBackground(Graphics g)
        {
            var centerX = WIDTH / 2;
            var centerY = HEIGHT / 2;
            var lineCount = 32;

            using var linePen = new Pen(Color.FromArgb(40, 255, 255, 255), 4);
            for (int i = 0; i < lineCount; i++)
            {
                var angle = (Math.PI * 2 / lineCount) * i;
                var endX = centerX + (int)(Math.Cos(angle) * 1000);
                var endY = centerY + (int)(Math.Sin(angle) * 1000);
                g.DrawLine(linePen, centerX, centerY, endX, endY);
            }
        }

        private void DrawPowerWordPattern(Graphics g, ThumbnailSettings settings)
        {
            const int STROKE_PAD = 20;
            const int MARGIN = 80;

            // 各行のフォントファミリーを取得
            var mainFontFamily = settings.MainFontFamily ?? settings.FontFamily;
            var subFontFamily = settings.SubFontFamily ?? settings.FontFamily;
            var subSubFontFamily = settings.SubSubFontFamily ?? settings.FontFamily;

            var state = g.Save();

            // Apply slant if enabled
            if (settings.UseSlantedText)
            {
                g.TranslateTransform(WIDTH / 2, HEIGHT / 2);
                g.RotateTransform(settings.SlantAngle);
                g.TranslateTransform(-WIDTH / 2, -HEIGHT / 2);
            }

            // Main text - 超デカ文字 (文字数に応じてサイズ調整、指定があれば固定)
            var textLen = settings.MainText?.Length ?? 0;
            int fontSize = settings.MainFontSize > 0
                ? settings.MainFontSize
                : (textLen <= 3 ? 180 : textLen <= 5 ? 140 : textLen <= 8 ? 110 : 80);
            var mainFont = GetFont(mainFontFamily, fontSize, FontStyle.Bold);
            var mainSize = g.MeasureString(settings.MainText, mainFont);

            // 幅に収まるように調整 (ストローク幅を考慮) - 手動サイズ指定時も調整する
            while (mainSize.Width > WIDTH - MARGIN * 2 - STROKE_PAD * 2 && fontSize > 50)
            {
                fontSize -= 8;
                mainFont = GetFont(mainFontFamily, fontSize, FontStyle.Bold);
                mainSize = g.MeasureString(settings.MainText, mainFont);
            }

            var mainX = (WIDTH - mainSize.Width) / 2;
            var mainY = (HEIGHT - mainSize.Height) / 2 - 50;

            // Draw text cushion if enabled
            if (settings.UseTextCushion)
            {
                DrawTextCushion(g, mainX - 30, mainY - 10, mainSize.Width + 60, mainSize.Height + 20, settings);
            }

            // Draw text with appropriate stroke style
            var mainText = settings.MainText ?? "";
            if (settings.UseGradientText)
            {
                // Elegant gradient from light to main color
                var topColor = Color.FromArgb(
                    Math.Min(255, settings.MainTextColor.R + 80),
                    Math.Min(255, settings.MainTextColor.G + 80),
                    Math.Min(255, settings.MainTextColor.B + 80));
                DrawTextWithGradient(g, mainText, mainFont, topColor, settings.MainTextColor,
                    settings.StrokeColor, settings.StrokeWidth, mainX, mainY);
            }
            else if (settings.UseTripleStroke)
            {
                DrawTextWithTripleStroke(g, mainText, mainFont, settings.MainTextColor,
                    settings.StrokeColor, settings.OuterStrokeColor, settings.StrokeWidth, mainX, mainY);
            }
            else
            {
                DrawTextWithStroke(g, mainText, mainFont, settings.MainTextColor,
                    settings.StrokeColor, settings.StrokeWidth, mainX, mainY);
            }

            g.Restore(state);

            // Calculate Y positions based on how many text lines we have
            var hasSubSub = !string.IsNullOrEmpty(settings.SubSubText);
            var hasSub = !string.IsNullOrEmpty(settings.SubText);

            // Sub text - サブ1（大きめ）
            if (hasSub)
            {
                int subFontSize = settings.SubFontSize > 0 ? settings.SubFontSize : 50;
                var subFont = GetFont(subFontFamily, subFontSize, FontStyle.Bold);
                var subSize = g.MeasureString(settings.SubText, subFont);

                // Shrink if too wide
                while (subSize.Width > WIDTH - MARGIN * 2 - STROKE_PAD * 2 && subFontSize > 28)
                {
                    subFontSize -= 4;
                    subFont = GetFont(subFontFamily, subFontSize, FontStyle.Bold);
                    subSize = g.MeasureString(settings.SubText, subFont);
                }

                var subX = (WIDTH - subSize.Width) / 2;
                var subY = hasSubSub ? HEIGHT - 200 : HEIGHT - 130;

                if (settings.UseTextCushion)
                {
                    DrawTextCushion(g, subX - 20, subY - 5, subSize.Width + 40, subSize.Height + 10, settings);
                }

                DrawTextWithStroke(g, settings.SubText, subFont, settings.SubTextColor,
                    settings.StrokeColor, 5, subX, subY);
            }

            // Sub-sub text - サブ2（やや大きめ）
            if (hasSubSub)
            {
                int subSubFontSize = settings.SubSubFontSize > 0 ? settings.SubSubFontSize : 40;
                var subSubFont = GetFont(subSubFontFamily, subSubFontSize, FontStyle.Bold);
                var subSubSize = g.MeasureString(settings.SubSubText, subSubFont);

                // Shrink if too wide
                while (subSubSize.Width > WIDTH - MARGIN * 2 - STROKE_PAD * 2 && subSubFontSize > 24)
                {
                    subSubFontSize -= 4;
                    subSubFont = GetFont(subSubFontFamily, subSubFontSize, FontStyle.Bold);
                    subSubSize = g.MeasureString(settings.SubSubText, subSubFont);
                }

                var subSubX = (WIDTH - subSubSize.Width) / 2;
                var subSubY = HEIGHT - 100;

                DrawTextWithStroke(g, settings.SubSubText, subSubFont, settings.SubSubTextColor,
                    settings.StrokeColor, 4, subSubX, subSubY);
            }
        }

        private void DrawTextCushion(Graphics g, float x, float y, float width, float height, ThumbnailSettings settings)
        {
            // Draw slanted cushion (parallelogram effect)
            var skew = 15;
            var points = new PointF[]
            {
                new PointF(x + skew, y),
                new PointF(x + width + skew, y),
                new PointF(x + width - skew, y + height),
                new PointF(x - skew, y + height)
            };

            using var brush = new SolidBrush(settings.CushionColor);
            g.FillPolygon(brush, points);

            // Add subtle border
            using var pen = new Pen(Color.FromArgb(100, 255, 255, 255), 2);
            g.DrawPolygon(pen, points);
        }

        private void DrawTextWithTripleStroke(Graphics g, string text, Font font, Color fillColor,
            Color innerStrokeColor, Color outerStrokeColor, int strokeWidth, float x, float y)
        {
            using var path = new GraphicsPath();
            path.AddString(text, font.FontFamily, (int)font.Style, font.Size,
                new PointF(x, y), StringFormat.GenericDefault);

            // Layer 3: Outer stroke (red/blue accent)
            using var outerPen = new Pen(outerStrokeColor, strokeWidth + 8)
            {
                LineJoin = LineJoin.Round
            };
            g.DrawPath(outerPen, path);

            // Layer 2: Inner stroke (black)
            using var innerPen = new Pen(innerStrokeColor, strokeWidth + 2)
            {
                LineJoin = LineJoin.Round
            };
            g.DrawPath(innerPen, path);

            // Layer 1: Fill with optional gradient
            using var fillBrush = new SolidBrush(fillColor);
            g.FillPath(fillBrush, path);

            // Add highlight for 3D effect
            using var highlightPath = new GraphicsPath();
            highlightPath.AddString(text, font.FontFamily, (int)font.Style, font.Size,
                new PointF(x, y - 2), StringFormat.GenericDefault);
            using var highlightBrush = new SolidBrush(Color.FromArgb(60, 255, 255, 255));
            // Clip to top half for highlight
            var clipRect = new RectangleF(x - 50, y - 10, path.GetBounds().Width + 100, font.Size / 2);
            var oldClip = g.Clip;
            g.SetClip(clipRect);
            g.FillPath(highlightBrush, path);
            g.Clip = oldClip;
        }

        private void DrawTextWithGradient(Graphics g, string text, Font font, Color topColor, Color bottomColor,
            Color strokeColor, int strokeWidth, float x, float y)
        {
            using var path = new GraphicsPath();
            path.AddString(text, font.FontFamily, (int)font.Style, font.Size,
                new PointF(x, y), StringFormat.GenericDefault);

            var bounds = path.GetBounds();

            // Stroke
            using var strokePen = new Pen(strokeColor, strokeWidth)
            {
                LineJoin = LineJoin.Round
            };
            g.DrawPath(strokePen, path);

            // Gradient fill
            using var gradientBrush = new LinearGradientBrush(
                new RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height + 10),
                topColor,
                bottomColor,
                LinearGradientMode.Vertical);
            g.FillPath(gradientBrush, path);

            // Subtle inner glow
            using var glowBrush = new SolidBrush(Color.FromArgb(40, 255, 255, 255));
            var oldClip = g.Clip;
            g.SetClip(new RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height / 3));
            g.FillPath(glowBrush, path);
            g.Clip = oldClip;
        }

        private void DrawBeforeAfterPattern(Graphics g, ThumbnailSettings settings)
        {
            // Draw center divider
            using var dividerPen = new Pen(Color.White, 6);
            g.DrawLine(dividerPen, WIDTH / 2, 0, WIDTH / 2, HEIGHT);

            // Before label
            var labelFont = GetFont(settings.FontFamily, 48, FontStyle.Bold);
            DrawTextWithStroke(g, "BEFORE", labelFont, Color.Red, Color.White, 4, 80, 50);
            DrawTextWithStroke(g, "AFTER", labelFont, Color.LimeGreen, Color.White, 4, WIDTH / 2 + 80, 50);

            // Main text at bottom
            var mainFont = GetFont(settings.FontFamily, 72, FontStyle.Bold);
            var mainSize = g.MeasureString(settings.MainText, mainFont);
            var mainX = (WIDTH - mainSize.Width) / 2;
            DrawTextWithStroke(g, settings.MainText, mainFont, settings.MainTextColor,
                settings.StrokeColor, settings.StrokeWidth, mainX, HEIGHT - 150);
        }

        private void DrawBlackBoxPattern(Graphics g, ThumbnailSettings settings)
        {
            // Main text with hidden part
            var mainFont = GetFont(settings.FontFamily, 90, FontStyle.Bold);
            var mainSize = g.MeasureString(settings.MainText, mainFont);
            var mainX = (WIDTH - mainSize.Width) / 2;
            var mainY = HEIGHT / 2 - 60;

            DrawTextWithStroke(g, settings.MainText, mainFont, settings.MainTextColor,
                settings.StrokeColor, settings.StrokeWidth, mainX, mainY);

            // Draw black box overlay on part of text
            if (!string.IsNullOrEmpty(settings.SubText))
            {
                var boxWidth = 200;
                var boxHeight = 80;
                var boxX = WIDTH / 2 - boxWidth / 2;
                var boxY = mainY + mainSize.Height + 20;

                using var boxBrush = new SolidBrush(Color.Black);
                g.FillRectangle(boxBrush, boxX, boxY, boxWidth, boxHeight);

                using var borderPen = new Pen(Color.Red, 4);
                g.DrawRectangle(borderPen, boxX, boxY, boxWidth, boxHeight);

                var hintFont = GetFont(settings.FontFamily, 36, FontStyle.Bold);
                DrawTextWithStroke(g, "???", hintFont, Color.Red, Color.White, 3,
                    boxX + 60, boxY + 15);
            }
        }

        private void DrawGrid4Pattern(Graphics g, ThumbnailSettings settings)
        {
            // 3等分リスト - 1, 2, 3 を横に並べる
            var sectionWidth = WIDTH / 3;
            var texts = new[] { settings.MainText, settings.SubText ?? "", settings.SubSubText ?? "" };
            var colors = new[] { settings.MainTextColor, settings.SubTextColor, settings.SubSubTextColor };

            for (int i = 0; i < 3; i++)
            {
                var centerX = sectionWidth * i + sectionWidth / 2;

                // 番号（超デカ）
                var numFont = GetFont(settings.FontFamily, 140, FontStyle.Bold);
                var numText = (i + 1).ToString();
                var numSize = g.MeasureString(numText, numFont);
                DrawTextWithStroke(g, numText, numFont, colors[i],
                    settings.StrokeColor, 10, centerX - numSize.Width / 2, 60);

                // テキスト
                if (!string.IsNullOrEmpty(texts[i]))
                {
                    var textFont = GetFont(settings.FontFamily, 36, FontStyle.Bold);
                    var textSize = g.MeasureString(texts[i], textFont);

                    // 長いテキストは縮小
                    if (textSize.Width > sectionWidth - 20)
                    {
                        textFont = GetFont(settings.FontFamily, 28, FontStyle.Bold);
                        textSize = g.MeasureString(texts[i], textFont);
                    }

                    DrawTextWithStroke(g, texts[i], textFont, Color.White,
                        settings.StrokeColor, 4, centerX - textSize.Width / 2, HEIGHT - 200);
                }
            }

            // 区切り線（薄く）
            using var dividerPen = new Pen(Color.FromArgb(100, 255, 255, 255), 3);
            g.DrawLine(dividerPen, sectionWidth, 50, sectionWidth, HEIGHT - 50);
            g.DrawLine(dividerPen, sectionWidth * 2, 50, sectionWidth * 2, HEIGHT - 50);
        }

        private void DrawNumbersFocusPattern(Graphics g, ThumbnailSettings settings)
        {
            // Extract number from main text or use default
            var numberFont = GetFont(settings.FontFamily, 180, FontStyle.Bold);
            var numberText = settings.MainText.Length <= 6 ? settings.MainText : "100";
            var numberSize = g.MeasureString(numberText, numberFont);
            var numberX = (WIDTH - numberSize.Width) / 2;
            var numberY = HEIGHT / 2 - numberSize.Height / 2 - 30;

            // Draw number with gradient effect
            DrawTextWithStroke(g, numberText, numberFont, Color.Gold,
                settings.StrokeColor, 12, numberX, numberY);

            // Sub text below
            if (!string.IsNullOrEmpty(settings.SubText))
            {
                var subFont = GetFont(settings.FontFamily, 48, FontStyle.Bold);
                var subSize = g.MeasureString(settings.SubText, subFont);
                var subX = (WIDTH - subSize.Width) / 2;
                DrawTextWithStroke(g, settings.SubText, subFont, settings.SubTextColor,
                    settings.StrokeColor, 5, subX, HEIGHT - 130);
            }
        }

        private void DrawMainSubPattern(Graphics g, ThumbnailSettings settings)
        {
            // Stroke padding for text measurement
            const int STROKE_PAD = 16;
            const int MARGIN = 80;
            const int MAX_WIDTH = WIDTH - MARGIN * 2 - STROKE_PAD * 2;

            // 各行のフォントファミリーを取得
            var mainFontFamily = settings.MainFontFamily ?? settings.FontFamily;
            var subFontFamily = settings.SubFontFamily ?? settings.FontFamily;
            var subSubFontFamily = settings.SubSubFontFamily ?? settings.FontFamily;

            // Main text - upper area with dynamic sizing
            var mainText = settings.MainText ?? "";
            int mainFontSize = settings.MainFontSize > 0 ? settings.MainFontSize : 80;
            var mainFont = GetFont(mainFontFamily, mainFontSize, FontStyle.Bold);
            var mainSize = g.MeasureString(mainText, mainFont);

            // Shrink font if text is too wide
            while (mainSize.Width > MAX_WIDTH && mainFontSize > 36)
            {
                mainFontSize -= 4;
                mainFont = GetFont(mainFontFamily, mainFontSize, FontStyle.Bold);
                mainSize = g.MeasureString(mainText, mainFont);
            }

            var mainX = (WIDTH - mainSize.Width) / 2;
            var mainY = MARGIN + STROKE_PAD;

            DrawTextWithStroke(g, mainText, mainFont, settings.MainTextColor,
                settings.StrokeColor, settings.StrokeWidth, mainX, mainY);

            // Divider line - positioned below main text with padding
            var dividerY = mainY + mainSize.Height + STROKE_PAD + 20;
            using var dividerPen = new Pen(Color.Gold, 4);
            g.DrawLine(dividerPen, MARGIN, dividerY, WIDTH - MARGIN, dividerY);

            // Sub text (サブ1) - below divider
            var subY = dividerY + 30;
            if (!string.IsNullOrEmpty(settings.SubText))
            {
                int subFontSize = settings.SubFontSize > 0 ? settings.SubFontSize : 44;
                var subFont = GetFont(subFontFamily, subFontSize, FontStyle.Bold);
                var subSize = g.MeasureString(settings.SubText, subFont);

                // Shrink if too wide
                while (subSize.Width > MAX_WIDTH && subFontSize > 24)
                {
                    subFontSize -= 4;
                    subFont = GetFont(subFontFamily, subFontSize, FontStyle.Bold);
                    subSize = g.MeasureString(settings.SubText, subFont);
                }

                var subX = (WIDTH - subSize.Width) / 2;
                DrawTextWithStroke(g, settings.SubText, subFont, settings.SubTextColor,
                    settings.StrokeColor, 5, subX, subY);

                subY += subSize.Height + STROKE_PAD;
            }

            // SubSub text (サブ2) - below sub
            if (!string.IsNullOrEmpty(settings.SubSubText))
            {
                int subSubFontSize = settings.SubSubFontSize > 0 ? settings.SubSubFontSize : 32;
                var subSubFont = GetFont(subSubFontFamily, subSubFontSize, FontStyle.Bold);
                var subSubSize = g.MeasureString(settings.SubSubText, subSubFont);

                // Shrink if too wide
                while (subSubSize.Width > MAX_WIDTH && subSubFontSize > 20)
                {
                    subSubFontSize -= 4;
                    subSubFont = GetFont(subSubFontFamily, subSubFontSize, FontStyle.Bold);
                    subSubSize = g.MeasureString(settings.SubSubText, subSubFont);
                }

                var subSubX = (WIDTH - subSubSize.Width) / 2;
                DrawTextWithStroke(g, settings.SubSubText, subSubFont, settings.SubSubTextColor,
                    settings.StrokeColor, 4, subSubX, subY);
            }
        }

        /// <summary>
        /// 3行インパクトパターン: 上段小・中段中・下段大
        /// 例: 史上初!? / 2.6兆円の上場企業 / 不正会計で崩壊か
        /// </summary>
        private void DrawThreeLinePattern(Graphics g, ThumbnailSettings settings)
        {
            const int STROKE_PAD = 20;
            const int MARGIN = 60;
            const int MAX_WIDTH = WIDTH - MARGIN * 2 - STROKE_PAD * 2;
            const int BASE_FONT_SIZE = 72; // 全行同じ大きさ

            // 各行のフォントファミリーを取得
            var mainFontFamily = settings.MainFontFamily ?? settings.FontFamily;
            var subFontFamily = settings.SubFontFamily ?? settings.FontFamily;
            var subSubFontFamily = settings.SubSubFontFamily ?? settings.FontFamily;

            // 3行の合計高さを計算して中央配置
            var mainText = settings.MainText ?? "";
            var subText = settings.SubText ?? "";
            var subSubText = settings.SubSubText ?? "";

            // Line 1 (top) - メイン
            int line1FontSize = settings.MainFontSize > 0 ? settings.MainFontSize : BASE_FONT_SIZE;
            var line1Font = GetFont(mainFontFamily, line1FontSize, FontStyle.Bold);
            var line1Size = g.MeasureString(mainText, line1Font);
            while (line1Size.Width > MAX_WIDTH && line1FontSize > 36)
            {
                line1FontSize -= 4;
                line1Font = GetFont(mainFontFamily, line1FontSize, FontStyle.Bold);
                line1Size = g.MeasureString(mainText, line1Font);
            }

            // Line 2 (middle) - サブ1
            int line2FontSize = settings.SubFontSize > 0 ? settings.SubFontSize : BASE_FONT_SIZE;
            var line2Font = GetFont(subFontFamily, line2FontSize, FontStyle.Bold);
            var line2Size = g.MeasureString(subText, line2Font);
            while (line2Size.Width > MAX_WIDTH && line2FontSize > 36)
            {
                line2FontSize -= 4;
                line2Font = GetFont(subFontFamily, line2FontSize, FontStyle.Bold);
                line2Size = g.MeasureString(subText, line2Font);
            }

            // Line 3 (bottom) - サブ2
            int line3FontSize = settings.SubSubFontSize > 0 ? settings.SubSubFontSize : BASE_FONT_SIZE;
            var line3Font = GetFont(subSubFontFamily, line3FontSize, FontStyle.Bold);
            var line3Size = g.MeasureString(subSubText, line3Font);
            while (line3Size.Width > MAX_WIDTH && line3FontSize > 36)
            {
                line3FontSize -= 4;
                line3Font = GetFont(subSubFontFamily, line3FontSize, FontStyle.Bold);
                line3Size = g.MeasureString(subSubText, line3Font);
            }

            // 全体の高さを計算
            float lineSpacing = 10;
            float totalHeight = line1Size.Height + line2Size.Height + line3Size.Height + lineSpacing * 2;

            // 開始Y位置（中央揃え）
            float startY = (HEIGHT - totalHeight) / 2;

            // Line 1 描画
            if (!string.IsNullOrEmpty(mainText))
            {
                var x = (WIDTH - line1Size.Width) / 2;
                DrawTextWithStroke(g, mainText, line1Font, settings.MainTextColor,
                    settings.StrokeColor, 7, x, startY);
            }

            // Line 2 描画
            if (!string.IsNullOrEmpty(subText))
            {
                var y = startY + line1Size.Height + lineSpacing;
                var x = (WIDTH - line2Size.Width) / 2;
                DrawTextWithStroke(g, subText, line2Font, settings.SubTextColor,
                    settings.StrokeColor, 7, x, y);
            }

            // Line 3 描画
            if (!string.IsNullOrEmpty(subSubText))
            {
                var y = startY + line1Size.Height + line2Size.Height + lineSpacing * 2;
                var x = (WIDTH - line3Size.Width) / 2;
                DrawTextWithStroke(g, subSubText, line3Font, settings.SubSubTextColor,
                    settings.StrokeColor, 7, x, y);
            }
        }

        private void DrawFocusLinesPattern(Graphics g, ThumbnailSettings settings)
        {
            // Draw focus lines from center
            var centerX = WIDTH / 2;
            var centerY = HEIGHT / 2;
            var lineCount = 24;

            using var linePen = new Pen(Color.FromArgb(150, 255, 0, 0), 3);
            for (int i = 0; i < lineCount; i++)
            {
                var angle = (Math.PI * 2 / lineCount) * i;
                var endX = centerX + (int)(Math.Cos(angle) * 800);
                var endY = centerY + (int)(Math.Sin(angle) * 800);
                var startX = centerX + (int)(Math.Cos(angle) * 200);
                var startY = centerY + (int)(Math.Sin(angle) * 200);
                g.DrawLine(linePen, startX, startY, endX, endY);
            }

            // Main text in center
            var mainFont = GetFont(settings.FontFamily, 80, FontStyle.Bold);
            var mainSize = g.MeasureString(settings.MainText, mainFont);
            var mainX = (WIDTH - mainSize.Width) / 2;
            var mainY = (HEIGHT - mainSize.Height) / 2;

            // White background for text
            using var textBg = new SolidBrush(Color.FromArgb(220, 255, 255, 255));
            g.FillEllipse(textBg, centerX - 250, centerY - 80, 500, 160);

            DrawTextWithStroke(g, settings.MainText, mainFont, Color.Red,
                Color.White, 4, mainX, mainY);
        }

        private void DrawQuestionPattern(Graphics g, ThumbnailSettings settings)
        {
            // Large question mark in background
            var bgFont = GetFont(settings.FontFamily, 400, FontStyle.Bold);
            using var bgBrush = new SolidBrush(Color.FromArgb(30, 255, 255, 255));
            g.DrawString("?", bgFont, bgBrush, WIDTH / 2 - 150, HEIGHT / 2 - 250);

            // Main text - slightly rotated for impact
            var state = g.Save();
            g.TranslateTransform(WIDTH / 2, HEIGHT / 2);
            g.RotateTransform(-5);
            g.TranslateTransform(-WIDTH / 2, -HEIGHT / 2);

            var mainFont = GetFont(settings.FontFamily, 72, FontStyle.Bold);
            var mainSize = g.MeasureString(settings.MainText, mainFont);
            var mainX = (WIDTH - mainSize.Width) / 2;
            var mainY = (HEIGHT - mainSize.Height) / 2;

            DrawTextWithStroke(g, settings.MainText, mainFont, settings.MainTextColor,
                settings.StrokeColor, settings.StrokeWidth, mainX, mainY);

            g.Restore(state);

            // Sub text at bottom
            if (!string.IsNullOrEmpty(settings.SubText))
            {
                var subFont = GetFont(settings.FontFamily, 36, FontStyle.Bold);
                var subSize = g.MeasureString(settings.SubText, subFont);
                var subX = (WIDTH - subSize.Width) / 2;
                DrawTextWithStroke(g, settings.SubText, subFont, settings.SubTextColor,
                    settings.StrokeColor, 4, subX, HEIGHT - 100);
            }
        }

        private void DrawGenreAestheticPattern(Graphics g, ThumbnailSettings settings)
        {
            // Gradient overlay for aesthetic feel
            using var gradientBrush = new LinearGradientBrush(
                new Rectangle(0, 0, WIDTH, HEIGHT),
                Color.FromArgb(100, 0, 0, 50),
                Color.FromArgb(100, 50, 0, 50),
                LinearGradientMode.ForwardDiagonal);
            g.FillRectangle(gradientBrush, 0, 0, WIDTH, HEIGHT);

            // Elegant text placement (lower third)
            var mainFont = GetFont(settings.FontFamily, 64, FontStyle.Bold);
            var mainSize = g.MeasureString(settings.MainText, mainFont);
            var mainX = (WIDTH - mainSize.Width) / 2;
            var mainY = HEIGHT - 200;

            // Semi-transparent background bar
            using var barBrush = new SolidBrush(Color.FromArgb(150, 0, 0, 0));
            g.FillRectangle(barBrush, 0, mainY - 20, WIDTH, mainSize.Height + 60);

            DrawTextWithStroke(g, settings.MainText, mainFont, Color.White,
                Color.FromArgb(100, 0, 0, 0), 3, mainX, mainY);

            // Sub text
            if (!string.IsNullOrEmpty(settings.SubText))
            {
                var subFont = GetFont(settings.FontFamily, 28, FontStyle.Regular);
                var subSize = g.MeasureString(settings.SubText, subFont);
                var subX = (WIDTH - subSize.Width) / 2;
                using var subBrush = new SolidBrush(Color.FromArgb(200, 255, 255, 255));
                g.DrawString(settings.SubText, subFont, subBrush, subX, mainY + mainSize.Height + 5);
            }
        }

        private void DrawEmotionalFacePattern(Graphics g, ThumbnailSettings settings)
        {
            // Space for face on left, text on right
            // Main text - right side
            var mainFont = GetFont(settings.FontFamily, 72, FontStyle.Bold);
            var mainSize = g.MeasureString(settings.MainText, mainFont);
            var mainX = WIDTH / 2 + 50;
            var mainY = HEIGHT / 2 - mainSize.Height;

            DrawTextWithStroke(g, settings.MainText, mainFont, settings.MainTextColor,
                settings.StrokeColor, settings.StrokeWidth, mainX, mainY);

            // Sub text below main
            if (!string.IsNullOrEmpty(settings.SubText))
            {
                var subFont = GetFont(settings.FontFamily, 36, FontStyle.Bold);
                DrawTextWithStroke(g, settings.SubText, subFont, settings.SubTextColor,
                    settings.StrokeColor, 4, mainX, mainY + mainSize.Height + 10);
            }

            // Placeholder for face area
            using var faceBrush = new SolidBrush(Color.FromArgb(50, 255, 255, 255));
            g.FillEllipse(faceBrush, 50, HEIGHT / 2 - 200, 400, 400);

            var hintFont = GetFont(settings.FontFamily, 24, FontStyle.Regular);
            using var hintBrush = new SolidBrush(Color.FromArgb(150, 255, 255, 255));
            g.DrawString("顔写真を\nここに配置", hintFont, hintBrush, 170, HEIGHT / 2 - 30);
        }

        private void DrawLogo(Graphics g, string logoPath)
        {
            try
            {
                using var logo = Image.FromFile(logoPath);
                var logoHeight = 60;
                var logoWidth = (int)(logo.Width * ((float)logoHeight / logo.Height));
                g.DrawImage(logo, WIDTH - logoWidth - 20, HEIGHT - logoHeight - 20, logoWidth, logoHeight);
            }
            catch
            {
                // Ignore logo errors
            }
        }

        private void DrawTextWithStroke(Graphics g, string text, Font font, Color fillColor,
            Color strokeColor, int strokeWidth, float x, float y)
        {
            using var path = new GraphicsPath();
            path.AddString(text, font.FontFamily, (int)font.Style, font.Size,
                new PointF(x, y), StringFormat.GenericDefault);

            using var strokePen = new Pen(strokeColor, strokeWidth)
            {
                LineJoin = LineJoin.Round
            };
            g.DrawPath(strokePen, path);

            using var fillBrush = new SolidBrush(fillColor);
            g.FillPath(fillBrush, path);
        }

        private Font GetFont(string familyName, int size, FontStyle style)
        {
            try
            {
                return new Font(familyName, size, style, GraphicsUnit.Pixel);
            }
            catch
            {
                // Fallback to default font
                return new Font("Arial", size, style, GraphicsUnit.Pixel);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;

namespace InsightCast.Models
{
    /// <summary>
    /// 描画ツールの種類
    /// </summary>
    public enum CaptureDrawTool
    {
        None,
        Select,
        Rect,
        Circle,
        Arrow,
        Line,
        Text,
        Mosaic,
        Pen,
        Highlighter,
        NumberMarker
    }

    /// <summary>
    /// リサイズハンドルの位置
    /// </summary>
    public enum ResizeHandle
    {
        None,
        TopLeft,
        Top,
        TopRight,
        Right,
        BottomRight,
        Bottom,
        BottomLeft,
        Left
    }

    /// <summary>
    /// アノテーション（注釈）を表すクラス
    /// 画面キャプチャ上に描画される図形やテキストを管理
    /// </summary>
    public class CaptureAnnotation : ICloneable
    {
        private const int HandleHitMargin = 8;
        private const int BoundsMargin = 5;

        public CaptureDrawTool Type { get; set; }
        public Point Start { get; set; }
        public Point End { get; set; }
        public Color Color { get; set; }
        public int StrokeWidth { get; set; }
        public string? Text { get; set; }
        public List<Point>? PenPoints { get; set; }
        public bool IsSelected { get; set; }
        public int NumberValue { get; set; } // 番号マーカー用

        /// <summary>
        /// アノテーションの境界矩形を取得
        /// </summary>
        public Rectangle Bounds
        {
            get
            {
                if (Type == CaptureDrawTool.Pen || Type == CaptureDrawTool.Highlighter)
                {
                    if (PenPoints == null || PenPoints.Count == 0)
                        return Rectangle.Empty;

                    int minX = PenPoints.Min(p => p.X);
                    int minY = PenPoints.Min(p => p.Y);
                    int maxX = PenPoints.Max(p => p.X);
                    int maxY = PenPoints.Max(p => p.Y);
                    return new Rectangle(
                        minX - BoundsMargin,
                        minY - BoundsMargin,
                        maxX - minX + BoundsMargin * 2,
                        maxY - minY + BoundsMargin * 2);
                }

                if (Type == CaptureDrawTool.Text && !string.IsNullOrEmpty(Text))
                {
                    // テキストの実際のサイズを使用（Endに保存済み）
                    return new Rectangle(
                        Start.X - BoundsMargin,
                        Start.Y - BoundsMargin,
                        End.X - Start.X + BoundsMargin * 2,
                        End.Y - Start.Y + BoundsMargin * 2);
                }

                if (Type == CaptureDrawTool.NumberMarker)
                {
                    int size = StrokeWidth * 2 + 24;
                    return new Rectangle(
                        Start.X - size / 2 - BoundsMargin,
                        Start.Y - size / 2 - BoundsMargin,
                        size + BoundsMargin * 2,
                        size + BoundsMargin * 2);
                }

                int x = Math.Min(Start.X, End.X);
                int y = Math.Min(Start.Y, End.Y);
                int w = Math.Max(Math.Abs(End.X - Start.X), 1);
                int h = Math.Max(Math.Abs(End.Y - Start.Y), 1);
                return new Rectangle(
                    x - BoundsMargin,
                    y - BoundsMargin,
                    w + BoundsMargin * 2,
                    h + BoundsMargin * 2);
            }
        }

        /// <summary>
        /// 指定座標がアノテーション上にあるかテスト
        /// </summary>
        public bool HitTest(Point pt)
        {
            return Bounds.Contains(pt);
        }

        /// <summary>
        /// 指定座標がどのリサイズハンドル上にあるかを返す
        /// </summary>
        public ResizeHandle HitTestHandle(Point pt)
        {
            if (!IsSelected) return ResizeHandle.None;

            var bounds = Bounds;
            var handles = GetHandleRects(bounds);

            if (handles.TryGetValue(ResizeHandle.TopLeft, out var r) && r.Contains(pt))
                return ResizeHandle.TopLeft;
            if (handles.TryGetValue(ResizeHandle.Top, out r) && r.Contains(pt))
                return ResizeHandle.Top;
            if (handles.TryGetValue(ResizeHandle.TopRight, out r) && r.Contains(pt))
                return ResizeHandle.TopRight;
            if (handles.TryGetValue(ResizeHandle.Right, out r) && r.Contains(pt))
                return ResizeHandle.Right;
            if (handles.TryGetValue(ResizeHandle.BottomRight, out r) && r.Contains(pt))
                return ResizeHandle.BottomRight;
            if (handles.TryGetValue(ResizeHandle.Bottom, out r) && r.Contains(pt))
                return ResizeHandle.Bottom;
            if (handles.TryGetValue(ResizeHandle.BottomLeft, out r) && r.Contains(pt))
                return ResizeHandle.BottomLeft;
            if (handles.TryGetValue(ResizeHandle.Left, out r) && r.Contains(pt))
                return ResizeHandle.Left;

            return ResizeHandle.None;
        }

        /// <summary>
        /// 各リサイズハンドルの矩形を取得
        /// </summary>
        public static Dictionary<ResizeHandle, Rectangle> GetHandleRects(Rectangle bounds)
        {
            int size = HandleHitMargin;
            int half = size / 2;

            return new Dictionary<ResizeHandle, Rectangle>
            {
                [ResizeHandle.TopLeft] = new Rectangle(bounds.X - half, bounds.Y - half, size, size),
                [ResizeHandle.Top] = new Rectangle(bounds.X + bounds.Width / 2 - half, bounds.Y - half, size, size),
                [ResizeHandle.TopRight] = new Rectangle(bounds.Right - half, bounds.Y - half, size, size),
                [ResizeHandle.Right] = new Rectangle(bounds.Right - half, bounds.Y + bounds.Height / 2 - half, size, size),
                [ResizeHandle.BottomRight] = new Rectangle(bounds.Right - half, bounds.Bottom - half, size, size),
                [ResizeHandle.Bottom] = new Rectangle(bounds.X + bounds.Width / 2 - half, bounds.Bottom - half, size, size),
                [ResizeHandle.BottomLeft] = new Rectangle(bounds.X - half, bounds.Bottom - half, size, size),
                [ResizeHandle.Left] = new Rectangle(bounds.X - half, bounds.Y + bounds.Height / 2 - half, size, size),
            };
        }

        /// <summary>
        /// アノテーションを移動
        /// </summary>
        public void Move(int dx, int dy)
        {
            Start = new Point(Start.X + dx, Start.Y + dy);
            End = new Point(End.X + dx, End.Y + dy);

            if (PenPoints != null)
            {
                for (int i = 0; i < PenPoints.Count; i++)
                {
                    PenPoints[i] = new Point(PenPoints[i].X + dx, PenPoints[i].Y + dy);
                }
            }
        }

        /// <summary>
        /// リサイズハンドルに応じてアノテーションをリサイズ
        /// </summary>
        public void Resize(ResizeHandle handle, int dx, int dy)
        {
            // Pen/Highlighterはリサイズ不可
            if (Type == CaptureDrawTool.Pen || Type == CaptureDrawTool.Highlighter)
                return;

            switch (handle)
            {
                case ResizeHandle.TopLeft:
                    Start = new Point(Start.X + dx, Start.Y + dy);
                    break;
                case ResizeHandle.Top:
                    Start = new Point(Start.X, Start.Y + dy);
                    break;
                case ResizeHandle.TopRight:
                    Start = new Point(Start.X, Start.Y + dy);
                    End = new Point(End.X + dx, End.Y);
                    break;
                case ResizeHandle.Right:
                    End = new Point(End.X + dx, End.Y);
                    break;
                case ResizeHandle.BottomRight:
                    End = new Point(End.X + dx, End.Y + dy);
                    break;
                case ResizeHandle.Bottom:
                    End = new Point(End.X, End.Y + dy);
                    break;
                case ResizeHandle.BottomLeft:
                    Start = new Point(Start.X + dx, Start.Y);
                    End = new Point(End.X, End.Y + dy);
                    break;
                case ResizeHandle.Left:
                    Start = new Point(Start.X + dx, Start.Y);
                    break;
            }
        }

        /// <summary>
        /// アノテーションを描画
        /// </summary>
        public void Draw(Graphics g, bool drawHandles = true)
        {
            switch (Type)
            {
                case CaptureDrawTool.Rect:
                    DrawRect(g);
                    break;
                case CaptureDrawTool.Circle:
                    DrawCircle(g);
                    break;
                case CaptureDrawTool.Arrow:
                    DrawArrow(g);
                    break;
                case CaptureDrawTool.Line:
                    DrawLine(g);
                    break;
                case CaptureDrawTool.Mosaic:
                    // Mosaicは特殊処理が必要なため呼び出し側で実装
                    break;
                case CaptureDrawTool.Pen:
                    DrawPenStroke(g);
                    break;
                case CaptureDrawTool.Highlighter:
                    DrawHighlighter(g);
                    break;
                case CaptureDrawTool.Text:
                    DrawText(g);
                    break;
                case CaptureDrawTool.NumberMarker:
                    DrawNumberMarker(g);
                    break;
            }

            if (drawHandles && IsSelected)
            {
                DrawSelectionHandles(g);
            }
        }

        private void DrawRect(Graphics g)
        {
            using var pen = new Pen(Color, StrokeWidth);
            var rect = GetDrawRect();
            g.DrawRectangle(pen, rect);
        }

        private void DrawCircle(Graphics g)
        {
            using var pen = new Pen(Color, StrokeWidth);
            var rect = GetDrawRect();
            g.DrawEllipse(pen, rect);
        }

        private void DrawArrow(Graphics g)
        {
            using var pen = new Pen(Color, StrokeWidth);
            pen.CustomEndCap = new AdjustableArrowCap(5, 5);
            g.DrawLine(pen, Start, End);
        }

        private void DrawLine(Graphics g)
        {
            using var pen = new Pen(Color, StrokeWidth);
            g.DrawLine(pen, Start, End);
        }

        private void DrawPenStroke(Graphics g)
        {
            if (PenPoints == null || PenPoints.Count < 2) return;

            using var pen = new Pen(Color, StrokeWidth)
            {
                LineJoin = LineJoin.Round,
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };

            for (int i = 1; i < PenPoints.Count; i++)
            {
                g.DrawLine(pen, PenPoints[i - 1], PenPoints[i]);
            }
        }

        private void DrawHighlighter(Graphics g)
        {
            if (PenPoints == null || PenPoints.Count < 2) return;

            // 半透明の太いペン
            using var pen = new Pen(System.Drawing.Color.FromArgb(100, Color), StrokeWidth * 4)
            {
                LineJoin = LineJoin.Round,
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };

            for (int i = 1; i < PenPoints.Count; i++)
            {
                g.DrawLine(pen, PenPoints[i - 1], PenPoints[i]);
            }
        }

        private void DrawText(Graphics g)
        {
            if (string.IsNullOrEmpty(Text)) return;

            using var font = new Font("Yu Gothic UI", StrokeWidth, FontStyle.Bold);
            using var brush = new SolidBrush(Color);
            using var shadowBrush = new SolidBrush(System.Drawing.Color.FromArgb(128, 0, 0, 0));

            // 影
            g.DrawString(Text, font, shadowBrush, new Point(Start.X + 1, Start.Y + 1));
            // 本体
            g.DrawString(Text, font, brush, Start);
        }

        private void DrawNumberMarker(Graphics g)
        {
            int size = StrokeWidth * 2 + 24;
            var rect = new Rectangle(Start.X - size / 2, Start.Y - size / 2, size, size);

            // 円を塗りつぶし
            using var brush = new SolidBrush(Color);
            g.FillEllipse(brush, rect);

            // 白い縁取り
            using var pen = new Pen(System.Drawing.Color.White, 2);
            g.DrawEllipse(pen, rect);

            // 番号
            using var font = new Font("Arial", size / 2, FontStyle.Bold);
            using var textBrush = new SolidBrush(System.Drawing.Color.White);
            var textSize = g.MeasureString(NumberValue.ToString(), font);
            g.DrawString(
                NumberValue.ToString(),
                font,
                textBrush,
                Start.X - textSize.Width / 2,
                Start.Y - textSize.Height / 2);
        }

        private void DrawSelectionHandles(Graphics g)
        {
            var bounds = Bounds;

            // 選択枠
            using var dashPen = new Pen(System.Drawing.Color.FromArgb(200, 0, 120, 212), 1);
            dashPen.DashStyle = DashStyle.Dash;
            g.DrawRectangle(dashPen, bounds);

            // ハンドル
            using var handleBrush = new SolidBrush(System.Drawing.Color.White);
            using var handlePen = new Pen(System.Drawing.Color.FromArgb(200, 0, 120, 212), 1);

            var handles = GetHandleRects(bounds);
            foreach (var kv in handles)
            {
                var r = kv.Value;
                r.Inflate(-1, -1);
                g.FillRectangle(handleBrush, r);
                g.DrawRectangle(handlePen, r);
            }
        }

        private Rectangle GetDrawRect()
        {
            int x = Math.Min(Start.X, End.X);
            int y = Math.Min(Start.Y, End.Y);
            int w = Math.Max(Math.Abs(End.X - Start.X), 1);
            int h = Math.Max(Math.Abs(End.Y - Start.Y), 1);
            return new Rectangle(x, y, w, h);
        }

        /// <summary>
        /// テキストの実際のサイズを計測してEndを更新
        /// </summary>
        public void MeasureTextBounds(Graphics g)
        {
            if (Type != CaptureDrawTool.Text || string.IsNullOrEmpty(Text)) return;

            using var font = new Font("Yu Gothic UI", StrokeWidth, FontStyle.Bold);
            var size = g.MeasureString(Text, font);
            End = new Point(Start.X + (int)size.Width, Start.Y + (int)size.Height);
        }

        public object Clone()
        {
            return new CaptureAnnotation
            {
                Type = Type,
                Start = Start,
                End = End,
                Color = Color,
                StrokeWidth = StrokeWidth,
                Text = Text,
                PenPoints = PenPoints?.ToList(),
                IsSelected = IsSelected,
                NumberValue = NumberValue
            };
        }
    }
}

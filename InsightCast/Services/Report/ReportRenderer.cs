using System;
using System.Collections.Generic;
using System.IO;
using InsightCast.Models.Report;
using Syncfusion.DocIO;
using Syncfusion.DocIO.DLS;

namespace InsightCast.Services.Report;

/// <summary>
/// INMV 固有: ReportStructure JSON → Syncfusion DocIO で .docx にレンダリング。
/// IOSH の XlsIO ベースとほぼ同じ構造。違いは video-script テーブルスタイルのみ。
/// </summary>
public class ReportRenderer
{
    // Ivory & Gold デザイン標準
    private static readonly System.Drawing.Color GoldColor = System.Drawing.Color.FromArgb(0xB8, 0x94, 0x2F);
    private static readonly System.Drawing.Color IvoryBg = System.Drawing.Color.FromArgb(0xFA, 0xF8, 0xF5);
    private static readonly System.Drawing.Color HeaderBg = System.Drawing.Color.FromArgb(0xF5, 0xF5, 0xF0);
    private static readonly System.Drawing.Color WhiteColor = System.Drawing.Color.White;
    private static readonly System.Drawing.Color LightBorder = System.Drawing.Color.FromArgb(0xE0, 0xD0, 0xB0);

    /// <summary>
    /// 新規 WordDocument をレンダリング（export_file / new_project 用）
    /// </summary>
    public WordDocument RenderToDocument(ReportStructure report)
    {
        var document = new WordDocument();
        var section = document.AddSection() as WSection;
        if (section == null) return document;

        // ページ設定
        section.PageSetup.Margins.All = 72; // 1 inch
        if (report.Metadata?.Orientation == "landscape")
            section.PageSetup.Orientation = PageOrientation.Landscape;

        // ヘッダー/フッター
        if (!string.IsNullOrEmpty(report.Metadata?.HeaderText))
        {
            var headerPara = section.HeadersFooters.Header.AddParagraph() as WParagraph;
            headerPara?.AppendText(report.Metadata.HeaderText);
        }
        if (report.Metadata?.ShowPageNumbers == true)
        {
            var footerPara = section.HeadersFooters.Footer.AddParagraph() as WParagraph;
            if (footerPara != null)
            {
                footerPara.ParagraphFormat.HorizontalAlignment = HorizontalAlignment.Center;
                footerPara.AppendField("Page", FieldType.FieldPage);
                footerPara.AppendText(" / ");
                footerPara.AppendField("NumPages", FieldType.FieldNumPages);
            }
        }

        // セクション描画
        foreach (var s in report.Sections)
        {
            RenderSection(section.Body, s);
            // セクション間の空行
            var spacer = section.Body.AddParagraph() as WParagraph;
            spacer?.AppendText("");
        }

        return document;
    }

    private void RenderSection(WTextBody body, ReportSection section)
    {
        switch (section.Type)
        {
            case "title": RenderTitle(body, section); break;
            case "heading": RenderHeading(body, section); break;
            case "text":
            case "summary":
            case "appendix": RenderText(body, section); break;
            case "bullet_list": RenderBulletList(body, section); break;
            case "recommendation": RenderRecommendation(body, section); break;
            case "table":
            case "comparison": RenderTable(body, section); break;
            case "key_metrics": RenderKeyMetrics(body, section); break;
        }
    }

    /// <summary>
    /// title: Gold 背景、白文字、中央揃え
    /// </summary>
    private void RenderTitle(WTextBody body, ReportSection section)
    {
        var para = body.AddParagraph() as WParagraph;
        if (para == null) return;

        para.ParagraphFormat.HorizontalAlignment = HorizontalAlignment.Center;
        para.ParagraphFormat.BackColor = GoldColor;
        para.ParagraphFormat.AfterSpacing = 6;

        var run = para.AppendText(section.Title ?? "");
        run.CharacterFormat.FontSize = 18;
        run.CharacterFormat.Bold = true;
        run.CharacterFormat.TextColor = WhiteColor;

        // サブタイトル
        if (!string.IsNullOrEmpty(section.Content))
        {
            var subPara = body.AddParagraph() as WParagraph;
            if (subPara == null) return;
            subPara.ParagraphFormat.HorizontalAlignment = HorizontalAlignment.Center;
            var subRun = subPara.AppendText(section.Content);
            subRun.CharacterFormat.FontSize = 11;
            subRun.CharacterFormat.TextColor = System.Drawing.Color.Gray;
        }
    }

    /// <summary>
    /// heading: レベルに応じたスタイル + Gold 下線
    /// </summary>
    private void RenderHeading(WTextBody body, ReportSection section)
    {
        var para = body.AddParagraph() as WParagraph;
        if (para == null) return;

        var level = section.Level ?? 2;
        var run = para.AppendText(section.Title ?? "");
        run.CharacterFormat.Bold = true;
        run.CharacterFormat.FontSize = level switch
        {
            1 => 16,
            2 => 13,
            _ => 11,
        };

        if (level <= 2)
        {
            para.ParagraphFormat.Borders.Bottom.BorderType = Syncfusion.DocIO.DLS.BorderStyle.Single;
            para.ParagraphFormat.Borders.Bottom.Color = GoldColor;
            para.ParagraphFormat.Borders.Bottom.LineWidth = 1.5f;
        }
        para.ParagraphFormat.AfterSpacing = 6;
    }

    /// <summary>
    /// text / summary: テキスト段落
    /// </summary>
    private void RenderText(WTextBody body, ReportSection section)
    {
        if (!string.IsNullOrEmpty(section.Title))
        {
            var titlePara = body.AddParagraph() as WParagraph;
            if (titlePara != null)
            {
                var titleRun = titlePara.AppendText(section.Title);
                titleRun.CharacterFormat.Bold = true;
                titleRun.CharacterFormat.FontSize = 11;
                titlePara.ParagraphFormat.AfterSpacing = 4;
            }
        }

        if (!string.IsNullOrEmpty(section.Content))
        {
            var contentPara = body.AddParagraph() as WParagraph;
            if (contentPara != null)
            {
                var contentRun = contentPara.AppendText(section.Content);
                contentRun.CharacterFormat.FontSize = 10.5f;
                contentPara.ParagraphFormat.AfterSpacing = 6;
            }
        }
    }

    /// <summary>
    /// bullet_list: 箇条書き
    /// </summary>
    private void RenderBulletList(WTextBody body, ReportSection section)
    {
        if (!string.IsNullOrEmpty(section.Title))
        {
            var titlePara = body.AddParagraph() as WParagraph;
            if (titlePara != null)
            {
                var titleRun = titlePara.AppendText(section.Title);
                titleRun.CharacterFormat.Bold = true;
                titlePara.ParagraphFormat.AfterSpacing = 4;
            }
        }

        if (section.Items != null)
        {
            foreach (var item in section.Items)
            {
                var itemPara = body.AddParagraph() as WParagraph;
                if (itemPara == null) continue;
                itemPara.ParagraphFormat.LeftIndent = 18;
                var bullet = itemPara.AppendText("• ");
                bullet.CharacterFormat.FontSize = 10.5f;
                var text = itemPara.AppendText(item);
                text.CharacterFormat.FontSize = 10.5f;
                itemPara.ParagraphFormat.AfterSpacing = 2;
            }
        }
    }

    /// <summary>
    /// recommendation: Gold ヘッダー + Ivory 背景テキスト
    /// </summary>
    private void RenderRecommendation(WTextBody body, ReportSection section)
    {
        // ヘッダー行
        var headerPara = body.AddParagraph() as WParagraph;
        if (headerPara != null)
        {
            headerPara.ParagraphFormat.BackColor = GoldColor;
            var headerRun = headerPara.AppendText(section.Title ?? "提言・推奨事項");
            headerRun.CharacterFormat.Bold = true;
            headerRun.CharacterFormat.FontSize = 12;
            headerRun.CharacterFormat.TextColor = WhiteColor;
            headerPara.ParagraphFormat.AfterSpacing = 4;
        }

        // テキスト
        if (!string.IsNullOrEmpty(section.Content))
        {
            var contentPara = body.AddParagraph() as WParagraph;
            if (contentPara != null)
            {
                contentPara.ParagraphFormat.BackColor = IvoryBg;
                var contentRun = contentPara.AppendText(section.Content);
                contentRun.CharacterFormat.FontSize = 10.5f;
                contentPara.ParagraphFormat.AfterSpacing = 4;
            }
        }

        // 箇条書き
        if (section.Items != null)
        {
            foreach (var item in section.Items)
            {
                var itemPara = body.AddParagraph() as WParagraph;
                if (itemPara == null) continue;
                itemPara.ParagraphFormat.BackColor = IvoryBg;
                itemPara.ParagraphFormat.LeftIndent = 14;
                var arrow = itemPara.AppendText("▶ ");
                arrow.CharacterFormat.FontSize = 10;
                var text = itemPara.AppendText(item);
                text.CharacterFormat.FontSize = 10.5f;
                itemPara.ParagraphFormat.AfterSpacing = 2;
            }
        }
    }

    /// <summary>
    /// table / comparison: ヘッダー + データ行。
    /// video-script テンプレートの場合は列幅を調整。
    /// </summary>
    private void RenderTable(WTextBody body, ReportSection section)
    {
        if (section.TableData == null) return;

        // タイトル
        if (!string.IsNullOrEmpty(section.Title))
        {
            var titlePara = body.AddParagraph() as WParagraph;
            if (titlePara != null)
            {
                var titleRun = titlePara.AppendText(section.Title);
                titleRun.CharacterFormat.Bold = true;
                titleRun.CharacterFormat.FontSize = 11;
                titlePara.ParagraphFormat.AfterSpacing = 4;
            }
        }

        var headers = section.TableData.Headers;
        var rows = section.TableData.Rows;
        var table = body.AddTable() as WTable;
        if (table == null) return;

        table.ResetCells(rows.Length + 1, headers.Length);
        table.TableFormat.Borders.BorderType = Syncfusion.DocIO.DLS.BorderStyle.Single;
        table.TableFormat.Borders.Color = LightBorder;
        table.TableFormat.Borders.LineWidth = 0.5f;

        // ヘッダー行
        for (int c = 0; c < headers.Length; c++)
        {
            var cell = table.Rows[0].Cells[c];
            cell.CellFormat.BackColor = GoldColor;
            var para = cell.AddParagraph() as WParagraph;
            if (para == null) continue;
            var run = para.AppendText(headers[c]);
            run.CharacterFormat.Bold = true;
            run.CharacterFormat.FontSize = 10;
            run.CharacterFormat.TextColor = WhiteColor;
        }

        // データ行
        for (int r = 0; r < rows.Length; r++)
        {
            for (int c = 0; c < rows[r].Length && c < headers.Length; c++)
            {
                var cell = table.Rows[r + 1].Cells[c];
                cell.CellFormat.BackColor = r % 2 == 0 ? WhiteColor : IvoryBg;
                var para = cell.AddParagraph() as WParagraph;
                if (para == null) continue;
                var run = para.AppendText(rows[r][c]);
                run.CharacterFormat.FontSize = 10;
            }
        }

        // video-script テーブルスタイル検出 & 適用
        if (IsVideoScriptTable(headers))
            ApplyVideoScriptTableStyle(table);
    }

    /// <summary>
    /// video-script テンプレート判定: 「シーン | ナレーション | 画面説明 | 表示時間」列パターン
    /// </summary>
    private static bool IsVideoScriptTable(string[] headers)
    {
        if (headers.Length < 4) return false;
        // 日本語・英語両方に対応
        var h0 = headers[0].ToLowerInvariant();
        return h0.Contains("シーン") || h0.Contains("scene") || h0.Contains("#") || h0.Contains("no");
    }

    /// <summary>
    /// video-script テンプレート固有: 列幅を最適化
    /// 1列目（シーン番号）を狭く、2列目（ナレーション）を広く設定
    /// </summary>
    private static void ApplyVideoScriptTableStyle(WTable table)
    {
        if (table.Rows.Count == 0 || table.Rows[0].Cells.Count < 4) return;

        foreach (WTableRow row in table.Rows)
        {
            if (row.Cells.Count >= 4)
            {
                row.Cells[0].Width = 60;   // シーン番号
                row.Cells[1].Width = 250;  // ナレーション
                row.Cells[2].Width = 150;  // 画面説明
                row.Cells[3].Width = 70;   // 表示時間
            }
        }
    }

    /// <summary>
    /// key_metrics: テーブル形式で KPI カード風表示
    /// </summary>
    private void RenderKeyMetrics(WTextBody body, ReportSection section)
    {
        if (section.Metrics == null || section.Metrics.Count == 0) return;

        // タイトル
        if (!string.IsNullOrEmpty(section.Title))
        {
            var titlePara = body.AddParagraph() as WParagraph;
            if (titlePara != null)
            {
                var titleRun = titlePara.AppendText(section.Title);
                titleRun.CharacterFormat.Bold = true;
                titleRun.CharacterFormat.FontSize = 11;
                titlePara.ParagraphFormat.AfterSpacing = 6;
            }
        }

        // KPI カードを 2 列テーブルで描画
        var cols = Math.Min(section.Metrics.Count, 4);
        var table = body.AddTable() as WTable;
        if (table == null) return;

        table.ResetCells(2, cols);
        table.TableFormat.Borders.BorderType = Syncfusion.DocIO.DLS.BorderStyle.Single;
        table.TableFormat.Borders.Color = LightBorder;
        table.TableFormat.Borders.LineWidth = 0.5f;

        for (int i = 0; i < cols; i++)
        {
            var m = section.Metrics[i];

            // ラベル行
            var labelCell = table.Rows[0].Cells[i];
            labelCell.CellFormat.BackColor = IvoryBg;
            var labelPara = labelCell.AddParagraph() as WParagraph;
            if (labelPara != null)
            {
                var labelRun = labelPara.AppendText(m.Label);
                labelRun.CharacterFormat.FontSize = 9;
                labelRun.CharacterFormat.TextColor = System.Drawing.Color.Gray;
            }

            // 値行
            var valueCell = table.Rows[1].Cells[i];
            valueCell.CellFormat.BackColor = IvoryBg;
            var valuePara = valueCell.AddParagraph() as WParagraph;
            if (valuePara != null)
            {
                var valueRun = valuePara.AppendText(m.Value);
                valueRun.CharacterFormat.FontSize = 16;
                valueRun.CharacterFormat.Bold = true;

                if (!string.IsNullOrEmpty(m.Change))
                {
                    var changeRun = valuePara.AppendText($"  {m.Change}");
                    changeRun.CharacterFormat.FontSize = 9;
                    changeRun.CharacterFormat.TextColor = m.Trend switch
                    {
                        "positive" => System.Drawing.Color.Green,
                        "negative" => System.Drawing.Color.Red,
                        _ => System.Drawing.Color.Gray,
                    };
                }
            }
        }
    }
}

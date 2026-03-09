using System;
using System.IO;
using System.Threading.Tasks;
using InsightCast.Models.Report;
using Microsoft.Win32;
using Syncfusion.DocIO;

namespace InsightCast.Services.Report;

/// <summary>
/// INMV 固有: レポート出力先の制御
/// - export_file: .docx で保存ダイアログ
/// - new_project: INMV はプロジェクトファイル形式がないため export_file と同じ動作
/// - insert_current: 非対応（動画プロジェクトに docx を挿入する概念がないため）
/// </summary>
public class ReportOutputManager
{
    private readonly ReportRenderer _renderer = new();

    /// <summary>
    /// 出力先に応じてレポートを保存する
    /// </summary>
    public Task<ReportOutputResult> ExecuteAsync(ReportOutputRequest request)
    {
        return request.Destination switch
        {
            // INMV はプロジェクト形式がないため new_project も export_file と同じ
            "new_project" or "export_file" => ExportToFile(request),
            "insert_current" => Task.FromResult(new ReportOutputResult
            {
                Success = false,
                Destination = "insert_current",
                Error = "not_supported",
                MessageJa = "動画プロジェクトへのドキュメント挿入は対応していません",
            }),
            _ => Task.FromResult(new ReportOutputResult
            {
                Success = false,
                Destination = request.Destination,
                Error = $"Unknown destination: {request.Destination}",
            }),
        };
    }

    /// <summary>
    /// export_file: .docx で保存ダイアログ → DocIO で直接保存
    /// </summary>
    private Task<ReportOutputResult> ExportToFile(ReportOutputRequest request)
    {
        var report = request.Report;
        var safeTitle = SanitizeFileName(report.Title);

        var dialog = new SaveFileDialog
        {
            FileName = $"{safeTitle}.docx",
            DefaultExt = ".docx",
            Filter = "Word Document (*.docx)|*.docx",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        };

        if (dialog.ShowDialog() != true)
        {
            return Task.FromResult(new ReportOutputResult
            {
                Success = false,
                Destination = "export_file",
                Error = "cancelled",
                MessageJa = "保存がキャンセルされました",
            });
        }

        try
        {
            using var document = _renderer.RenderToDocument(report);
            using var stream = new FileStream(dialog.FileName, FileMode.Create, FileAccess.Write);
            document.Save(stream, FormatType.Docx);

            return Task.FromResult(new ReportOutputResult
            {
                Success = true,
                Destination = "export_file",
                OutputPath = dialog.FileName,
                OutputFormat = "docx",
                MessageJa = $"レポートを保存しました: {Path.GetFileName(dialog.FileName)}",
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ReportOutputResult
            {
                Success = false,
                Destination = "export_file",
                Error = ex.Message,
                MessageJa = $"保存に失敗しました: {ex.Message}",
            });
        }
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "Report";
        var invalidChars = Path.GetInvalidFileNameChars();
        foreach (var c in invalidChars)
            name = name.Replace(c, '_');
        return name;
    }
}

/// <summary>
/// レポート出力リクエスト
/// </summary>
public class ReportOutputRequest
{
    public ReportStructure Report { get; set; } = new();
    public string Destination { get; set; } = "export_file";
    public string Locale { get; set; } = "ja";
}

/// <summary>
/// レポート出力結果
/// </summary>
public class ReportOutputResult
{
    public bool Success { get; set; }
    public string Destination { get; set; } = "";
    public string? OutputPath { get; set; }
    public string? OutputFormat { get; set; }
    public string? Error { get; set; }
    public string? MessageJa { get; set; }
}

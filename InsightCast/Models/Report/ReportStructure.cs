using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace InsightCast.Models.Report;

/// <summary>
/// Claude Structured Output が返すレポート構造全体
/// </summary>
public class ReportStructure
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("subtitle")]
    public string? Subtitle { get; set; }

    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("outputFormat")]
    public string OutputFormat { get; set; } = "docx";

    [JsonPropertyName("sections")]
    public List<ReportSection> Sections { get; set; } = [];

    [JsonPropertyName("metadata")]
    public ReportMetadata? Metadata { get; set; }
}

public class ReportMetadata
{
    [JsonPropertyName("orientation")]
    public string? Orientation { get; set; }

    [JsonPropertyName("paperSize")]
    public string? PaperSize { get; set; }

    [JsonPropertyName("headerText")]
    public string? HeaderText { get; set; }

    [JsonPropertyName("footerText")]
    public string? FooterText { get; set; }

    [JsonPropertyName("showPageNumbers")]
    public bool? ShowPageNumbers { get; set; }
}

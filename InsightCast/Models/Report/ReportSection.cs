using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace InsightCast.Models.Report;

/// <summary>
/// レポートの1セクション（Claude が返す JSON の一要素）
/// </summary>
public class ReportSection
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("items")]
    public List<string>? Items { get; set; }

    [JsonPropertyName("tableData")]
    public ReportTableData? TableData { get; set; }

    [JsonPropertyName("metrics")]
    public List<ReportMetric>? Metrics { get; set; }

    [JsonPropertyName("level")]
    public int? Level { get; set; }
}

public class ReportTableData
{
    [JsonPropertyName("headers")]
    public string[] Headers { get; set; } = [];

    [JsonPropertyName("rows")]
    public string[][] Rows { get; set; } = [];

    [JsonPropertyName("columnFormats")]
    public string[]? ColumnFormats { get; set; }
}

public class ReportMetric
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("value")]
    public string Value { get; set; } = "";

    [JsonPropertyName("change")]
    public string? Change { get; set; }

    [JsonPropertyName("trend")]
    public string? Trend { get; set; }
}

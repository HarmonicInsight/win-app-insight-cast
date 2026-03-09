using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace InsightCast.Models.Report;

/// <summary>
/// report-config.json のルートモデル（export-report-config.ts が生成する JSON）
/// </summary>
public class ReportConfig
{
    [JsonPropertyName("_meta")]
    public ReportConfigMeta? Meta { get; set; }

    [JsonPropertyName("templates")]
    public List<ReportTemplate> Templates { get; set; } = [];

    [JsonPropertyName("tools")]
    public ReportToolSet? Tools { get; set; }

    [JsonPropertyName("limitsByPlan")]
    public Dictionary<string, ReportPlanLimits>? LimitsByPlan { get; set; }

    [JsonPropertyName("defaultOutputFormat")]
    public Dictionary<string, string>? DefaultOutputFormat { get; set; }

    [JsonPropertyName("outputSchema")]
    public JsonElement? OutputSchema { get; set; }

    [JsonPropertyName("systemPromptExtension")]
    public LocalizedText? SystemPromptExtension { get; set; }

    [JsonPropertyName("revisionPromptExtension")]
    public LocalizedText? RevisionPromptExtension { get; set; }

    [JsonPropertyName("outputDestinations")]
    public List<ReportOutputDestination>? OutputDestinations { get; set; }
}

public class ReportConfigMeta
{
    [JsonPropertyName("generatedAt")]
    public string? GeneratedAt { get; set; }

    [JsonPropertyName("productCode")]
    public string? ProductCode { get; set; }

    [JsonPropertyName("templateCount")]
    public int TemplateCount { get; set; }
}

public class ReportTemplate
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("nameJa")]
    public string NameJa { get; set; } = "";

    [JsonPropertyName("nameEn")]
    public string NameEn { get; set; } = "";

    [JsonPropertyName("descriptionJa")]
    public string DescriptionJa { get; set; } = "";

    [JsonPropertyName("descriptionEn")]
    public string DescriptionEn { get; set; } = "";

    [JsonPropertyName("outputFormat")]
    public string OutputFormat { get; set; } = "docx";

    [JsonPropertyName("products")]
    public List<string> Products { get; set; } = [];

    [JsonPropertyName("suggestedSections")]
    public List<string> SuggestedSections { get; set; } = [];

    [JsonPropertyName("promptHintJa")]
    public string PromptHintJa { get; set; } = "";

    [JsonPropertyName("promptHintEn")]
    public string PromptHintEn { get; set; } = "";
}

public class ReportToolSet
{
    [JsonPropertyName("generation")]
    public JsonElement? Generation { get; set; }

    [JsonPropertyName("revision")]
    public JsonElement? Revision { get; set; }
}

public class ReportPlanLimits
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("monthlyLimit")]
    public int? MonthlyLimit { get; set; }

    [JsonPropertyName("allowedFormats")]
    public List<string> AllowedFormats { get; set; } = [];

    [JsonPropertyName("customTemplates")]
    public bool CustomTemplates { get; set; }
}

public class LocalizedText
{
    [JsonPropertyName("ja")]
    public string Ja { get; set; } = "";

    [JsonPropertyName("en")]
    public string En { get; set; } = "";
}

public class ReportOutputDestination
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("nameJa")]
    public string NameJa { get; set; } = "";

    [JsonPropertyName("nameEn")]
    public string NameEn { get; set; } = "";

    [JsonPropertyName("descriptionJa")]
    public string DescriptionJa { get; set; } = "";

    [JsonPropertyName("descriptionEn")]
    public string DescriptionEn { get; set; } = "";

    [JsonPropertyName("icon")]
    public string Icon { get; set; } = "";

    [JsonPropertyName("availability")]
    public string Availability { get; set; } = "always";
}

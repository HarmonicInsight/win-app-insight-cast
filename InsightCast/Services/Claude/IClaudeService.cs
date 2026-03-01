using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using InsightCommon.AI;

namespace InsightCast.Services.Claude;

/// <summary>
/// Claude API サービスインターフェース
/// </summary>
public interface IClaudeService
{
    bool IsConfigured { get; }
    string CurrentModel { get; }

    void SetApiKey(string apiKey);
    void SetModelByIndex(int index);

    Task<string> SendMessageAsync(string userMessage, string? systemContext = null, CancellationToken ct = default);
    Task<string> SendChatAsync(List<AiMessage> history, string userMessage, string? systemContext = null, CancellationToken ct = default);
    Task<ClaudeToolResponse> SendMessageWithToolsAsync(List<object> messages, List<ToolDefinition> tools, string? systemContext = null, CancellationToken ct = default);

    static decimal CalculateCost(int inputTokens, int outputTokens, string modelId)
        => ClaudeApiClient.CalculateCost(inputTokens, outputTokens, modelId);
}

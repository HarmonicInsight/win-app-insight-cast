using InsightCommon.AI;

namespace InsightCast.Services.Claude;

/// <summary>
/// InsightCast 固有のプリセットプロンプト（IsImageMode プロパティ付き）
/// </summary>
public class InsightCastPresetPrompt : PresetPrompt
{
    /// <summary>
    /// 画像生成モード（DALL-E 3）で使用するプロンプトかどうか
    /// false = Claude（テキスト）モード、true = DALL-E（画像）モード
    /// </summary>
    public bool IsImageMode { get; init; } = false;
}

using System;
using System.Threading;
using System.Threading.Tasks;

namespace InsightCast.Services.OpenAI
{
    /// <summary>
    /// OpenAI APIとの連携を行うサービスのインターフェース。
    /// </summary>
    public interface IOpenAIService : IDisposable
    {
        /// <summary>
        /// APIキーを設定して接続を確認します。
        /// </summary>
        /// <param name="apiKey">OpenAI APIキー。</param>
        /// <param name="ct">キャンセルトークン。</param>
        /// <returns>接続が成功した場合はtrue。</returns>
        Task<bool> ConfigureAsync(string apiKey, CancellationToken ct = default);

        /// <summary>
        /// 指定したトピックに基づいてナレーションテキストを生成します。
        /// </summary>
        /// <param name="request">テキスト生成リクエスト。</param>
        /// <param name="ct">キャンセルトークン。</param>
        /// <returns>生成結果。</returns>
        Task<TextGenerationResult> GenerateNarrationAsync(
            TextGenerationRequest request,
            CancellationToken ct = default);

        /// <summary>
        /// 説明に基づいてDALL-E画像を生成します。
        /// </summary>
        /// <param name="request">画像生成リクエスト。</param>
        /// <param name="ct">キャンセルトークン。</param>
        /// <returns>生成結果。</returns>
        Task<ImageGenerationResult> GenerateImageAsync(
            ImageGenerationRequest request,
            CancellationToken ct = default);

        /// <summary>
        /// 現在設定されているナレーションモデル。
        /// </summary>
        string NarrationModel { get; set; }

        /// <summary>
        /// 現在設定されている画像生成モデル。
        /// </summary>
        string ImageModel { get; set; }

        /// <summary>
        /// 接続が設定済みかどうか。
        /// </summary>
        bool IsConfigured { get; }
    }
}

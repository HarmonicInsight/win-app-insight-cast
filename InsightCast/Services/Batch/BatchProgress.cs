using System.Text.Json.Serialization;

namespace InsightCast.Services.Batch
{
    /// <summary>
    /// バッチ処理のフェーズ。
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BatchPhase
    {
        /// <summary>待機中。</summary>
        Idle,
        /// <summary>プロジェクト読み込み中。</summary>
        Loading,
        /// <summary>AI生成処理中。</summary>
        AIGeneration,
        /// <summary>動画エクスポート中。</summary>
        Exporting,
        /// <summary>完了。</summary>
        Completed,
        /// <summary>失敗。</summary>
        Failed
    }

    /// <summary>
    /// バッチ処理の進捗情報。
    /// </summary>
    public class BatchProgress
    {
        /// <summary>
        /// 現在処理中のプロジェクトインデックス（0始まり）。
        /// </summary>
        public int CurrentProjectIndex { get; set; }

        /// <summary>
        /// 総プロジェクト数。
        /// </summary>
        public int TotalProjects { get; set; }

        /// <summary>
        /// 現在処理中のプロジェクト名。
        /// </summary>
        public string CurrentProjectName { get; set; } = string.Empty;

        /// <summary>
        /// 現在のフェーズ。
        /// </summary>
        public BatchPhase Phase { get; set; } = BatchPhase.Idle;

        /// <summary>
        /// 現在のメッセージ。
        /// </summary>
        public string CurrentMessage { get; set; } = string.Empty;

        /// <summary>
        /// 全体の進捗率（0-100）。
        /// </summary>
        public double OverallProgress => TotalProjects > 0
            ? (double)CurrentProjectIndex / TotalProjects * 100
            : 0;
    }
}

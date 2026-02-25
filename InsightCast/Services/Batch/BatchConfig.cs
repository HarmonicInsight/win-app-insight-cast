using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace InsightCast.Services.Batch
{
    /// <summary>
    /// バッチ処理の設定。
    /// </summary>
    public class BatchConfig
    {
        /// <summary>
        /// バッチ名。
        /// </summary>
        [JsonPropertyName("batchName")]
        public string BatchName { get; set; } = string.Empty;

        /// <summary>
        /// バージョン。
        /// </summary>
        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0";

        /// <summary>
        /// グローバル設定。
        /// </summary>
        [JsonPropertyName("globalSettings")]
        public BatchGlobalSettings GlobalSettings { get; set; } = new();

        /// <summary>
        /// 処理するプロジェクトのリスト。
        /// </summary>
        [JsonPropertyName("projects")]
        public List<BatchProjectItem> Projects { get; set; } = new();
    }

    /// <summary>
    /// バッチ処理のグローバル設定。
    /// </summary>
    public class BatchGlobalSettings
    {
        /// <summary>
        /// 出力ディレクトリ。
        /// </summary>
        [JsonPropertyName("outputDirectory")]
        public string OutputDirectory { get; set; } = string.Empty;

        /// <summary>
        /// OpenAI APIキー（環境変数参照可: ${OPENAI_API_KEY}）。
        /// </summary>
        [JsonPropertyName("openaiApiKey")]
        public string? OpenAIApiKey { get; set; }

        /// <summary>
        /// エラー発生時に処理を続行するかどうか。
        /// </summary>
        [JsonPropertyName("continueOnError")]
        public bool ContinueOnError { get; set; } = true;

        /// <summary>
        /// 並列実行するかどうか（現在は未実装、将来の拡張用）。
        /// </summary>
        [JsonPropertyName("parallelExecution")]
        public bool ParallelExecution { get; set; } = false;
    }

    /// <summary>
    /// バッチ処理する個別プロジェクト。
    /// </summary>
    public class BatchProjectItem
    {
        /// <summary>
        /// プロジェクトJSONファイルのパス。
        /// </summary>
        [JsonPropertyName("projectFile")]
        public string ProjectFile { get; set; } = string.Empty;

        /// <summary>
        /// 出力ファイル名（省略時はプロジェクト名.mp4）。
        /// </summary>
        [JsonPropertyName("outputName")]
        public string? OutputName { get; set; }

        /// <summary>
        /// プロジェクト設定のオーバーライド。
        /// </summary>
        [JsonPropertyName("overrides")]
        public BatchOverrides? Overrides { get; set; }
    }

    /// <summary>
    /// プロジェクト設定のオーバーライド。
    /// </summary>
    public class BatchOverrides
    {
        /// <summary>
        /// 解像度のオーバーライド。
        /// </summary>
        [JsonPropertyName("resolution")]
        public string? Resolution { get; set; }

        /// <summary>
        /// FPSのオーバーライド。
        /// </summary>
        [JsonPropertyName("fps")]
        public int? Fps { get; set; }
    }
}

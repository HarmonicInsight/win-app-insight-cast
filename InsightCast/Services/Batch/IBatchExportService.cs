using System;
using System.Threading;
using System.Threading.Tasks;
using InsightCast.Models;

namespace InsightCast.Services.Batch
{
    /// <summary>
    /// バッチエクスポート処理を行うサービスのインターフェース。
    /// </summary>
    public interface IBatchExportService
    {
        /// <summary>
        /// バッチエクスポートを実行します。
        /// </summary>
        /// <param name="config">バッチ設定。</param>
        /// <param name="defaultSpeakerId">デフォルトの話者ID。</param>
        /// <param name="getStyleForScene">シーンのスタイルを取得する関数。</param>
        /// <param name="progress">進捗報告。</param>
        /// <param name="ct">キャンセルトークン。</param>
        /// <returns>バッチ処理結果。</returns>
        Task<BatchResult> ExecuteBatchAsync(
            BatchConfig config,
            int defaultSpeakerId,
            Func<Scene, TextStyle> getStyleForScene,
            IProgress<BatchProgress> progress,
            CancellationToken ct);

        /// <summary>
        /// 単一のプロジェクトJSONをインポートしてエクスポートします。
        /// </summary>
        /// <param name="projectJsonPath">プロジェクトJSONファイルのパス。</param>
        /// <param name="outputPath">出力パス。</param>
        /// <param name="defaultSpeakerId">デフォルトの話者ID。</param>
        /// <param name="getStyleForScene">シーンのスタイルを取得する関数。</param>
        /// <param name="progress">進捗報告。</param>
        /// <param name="ct">キャンセルトークン。</param>
        /// <returns>エクスポート結果。</returns>
        Task<ExportResult> ImportAndExportAsync(
            string projectJsonPath,
            string outputPath,
            int defaultSpeakerId,
            Func<Scene, TextStyle> getStyleForScene,
            IProgress<string> progress,
            CancellationToken ct);

        /// <summary>
        /// JSONファイルからProjectオブジェクトをインポートします。
        /// </summary>
        /// <param name="jsonPath">JSONファイルのパス。</param>
        /// <returns>Projectオブジェクト。</returns>
        Project ImportProjectFromJson(string jsonPath);

        /// <summary>
        /// バッチ設定JSONファイルを読み込みます。
        /// </summary>
        /// <param name="configPath">設定ファイルのパス。</param>
        /// <returns>バッチ設定。</returns>
        BatchConfig LoadBatchConfig(string configPath);

        /// <summary>
        /// バッチ処理の進行状況が変わったときに発生するイベント。
        /// </summary>
        event EventHandler<BatchProgress>? ProgressChanged;

        /// <summary>
        /// プロジェクトが完了したときに発生するイベント。
        /// </summary>
        event EventHandler<BatchProjectResult>? ProjectCompleted;
    }
}

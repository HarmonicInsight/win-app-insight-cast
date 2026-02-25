using System;
using System.Collections.Generic;

namespace InsightCast.Services.Batch
{
    /// <summary>
    /// バッチ処理全体の結果。
    /// </summary>
    public class BatchResult
    {
        /// <summary>
        /// バッチ名。
        /// </summary>
        public string BatchName { get; set; } = string.Empty;

        /// <summary>
        /// 開始時刻。
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// 終了時刻。
        /// </summary>
        public DateTime EndTime { get; set; }

        /// <summary>
        /// 総プロジェクト数。
        /// </summary>
        public int TotalProjects { get; set; }

        /// <summary>
        /// 成功したプロジェクト数。
        /// </summary>
        public int SuccessCount { get; set; }

        /// <summary>
        /// 失敗したプロジェクト数。
        /// </summary>
        public int FailCount { get; set; }

        /// <summary>
        /// 処理時間。
        /// </summary>
        public TimeSpan Duration => EndTime - StartTime;

        /// <summary>
        /// 成功したプロジェクトの詳細。
        /// </summary>
        public List<BatchProjectResult> SuccessfulProjects { get; } = new();

        /// <summary>
        /// 失敗したプロジェクトの詳細。
        /// </summary>
        public List<BatchProjectResult> FailedProjects { get; } = new();

        /// <summary>
        /// すべて成功したかどうか。
        /// </summary>
        public bool AllSuccessful => FailCount == 0 && SuccessCount > 0;
    }

    /// <summary>
    /// 個別プロジェクトの処理結果。
    /// </summary>
    public class BatchProjectResult
    {
        /// <summary>
        /// プロジェクトファイルのパス。
        /// </summary>
        public string ProjectFile { get; set; } = string.Empty;

        /// <summary>
        /// 出力ファイルのパス（成功時）。
        /// </summary>
        public string? OutputPath { get; set; }

        /// <summary>
        /// 成功したかどうか。
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// エラーメッセージ（失敗時）。
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// 処理時間。
        /// </summary>
        public TimeSpan Duration { get; set; }
    }
}

namespace InsightCast.TTS;

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

/// <summary>
/// ナレーションテキスト中の間（ポーズ）指定を解析するヘルパー。
///
/// 記法:
///   、、   → 1.0秒の間（読点2つ）
///   、、、 → 1.5秒の間（読点3つ、連続するほど長くなる）
///   改行   → 0.3秒の間
/// ※ 単独の「、」は通常の句読点として TTS エンジンに任せる
/// </summary>
public static class NarrationPauseHelper
{
    /// <summary>読点1つあたりの間（秒）</summary>
    public const double CommaPauseSeconds = 0.5;

    /// <summary>改行時に自動挿入する間の秒数</summary>
    public const double NewlinePauseSeconds = 0.3;

    /// <summary>連続する読点（2つ以上）にマッチする正規表現。単独の「、」は通常の句読点として TTS に任せる。</summary>
    private static readonly Regex CommaPattern = new(@"、{2,}", RegexOptions.Compiled);

    /// <summary>
    /// テキストセグメント（テキスト部分 or ポーズ指定）。
    /// </summary>
    public record Segment(string Text, double PauseSeconds, bool IsPause)
    {
        public static Segment TextPart(string text) => new(text, 0, false);
        public static Segment Pause(double seconds) => new("", seconds, true);
    }

    /// <summary>
    /// ナレーションテキストをテキスト部分とポーズ部分に分割する。
    /// 「、」→ 0.5秒の間、改行 → 0.3秒の間に変換する。
    /// </summary>
    public static List<Segment> Parse(string text)
    {
        if (string.IsNullOrEmpty(text))
            return new List<Segment>();

        // 改行を正規化（連続改行は1つにまとめる）
        var normalized = Regex.Replace(text, @"\r?\n+", "\n");

        var segments = new List<Segment>();
        // まず改行で分割
        var lines = normalized.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            // 各行内で「、」を処理
            ParseCommasInLine(segments, lines[i]);

            // 改行の後にポーズを挿入（最後の行の後は除く）
            if (i < lines.Length - 1)
                segments.Add(Segment.Pause(NewlinePauseSeconds));
        }

        // 連続するポーズをマージし、空テキストセグメントを除去
        return MergeAndClean(segments);
    }

    /// <summary>
    /// 行内の連続する「、」をポーズに変換する。
    /// </summary>
    private static void ParseCommasInLine(List<Segment> segments, string line)
    {
        int lastIndex = 0;
        var matches = CommaPattern.Matches(line);

        foreach (Match match in matches)
        {
            // 「、」の前のテキスト部分
            if (match.Index > lastIndex)
            {
                var beforeText = line[lastIndex..match.Index].Trim();
                if (!string.IsNullOrEmpty(beforeText))
                    segments.Add(Segment.TextPart(beforeText));
            }

            // 「、」の数 × 0.5秒（最大 5秒に制限）
            var pauseSeconds = Math.Clamp(match.Length * CommaPauseSeconds, 0.1, 5.0);
            segments.Add(Segment.Pause(pauseSeconds));

            lastIndex = match.Index + match.Length;
        }

        // 残りのテキスト
        if (lastIndex < line.Length)
        {
            var remaining = line[lastIndex..].Trim();
            if (!string.IsNullOrEmpty(remaining))
                segments.Add(Segment.TextPart(remaining));
        }
    }

    /// <summary>
    /// 連続するポーズをマージし、空のテキストセグメントを除去する。
    /// </summary>
    private static List<Segment> MergeAndClean(List<Segment> segments)
    {
        var result = new List<Segment>();
        double accumulatedPause = 0;

        foreach (var seg in segments)
        {
            if (seg.IsPause)
            {
                accumulatedPause += seg.PauseSeconds;
            }
            else
            {
                if (accumulatedPause > 0)
                {
                    result.Add(Segment.Pause(Math.Clamp(accumulatedPause, 0.1, 5.0)));
                    accumulatedPause = 0;
                }

                if (!string.IsNullOrWhiteSpace(seg.Text))
                    result.Add(seg);
            }
        }

        // 末尾にポーズが残っている場合は追加
        if (accumulatedPause > 0)
            result.Add(Segment.Pause(Math.Clamp(accumulatedPause, 0.1, 5.0)));

        return result;
    }

    /// <summary>
    /// Edge TTS 用: パース結果を SSML の中身（break タグ付き）に変換する。
    /// テキストは XML エスケープ済みで返す。
    /// </summary>
    public static string ToSsmlContent(List<Segment> segments)
    {
        var sb = new System.Text.StringBuilder();

        foreach (var seg in segments)
        {
            if (seg.IsPause)
            {
                var ms = (int)(seg.PauseSeconds * 1000);
                sb.Append($"<break time=\"{ms}ms\"/>");
            }
            else
            {
                sb.Append(System.Security.SecurityElement.Escape(seg.Text));
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// テキストに明示的なポーズ指定（連続する「、、」または改行）が含まれているかを判定する。
    /// 単独の「、」は通常の日本語句読点であり TTS エンジンが自然に処理するため対象外。
    /// </summary>
    public static bool HasPauseMarkers(string text)
    {
        return text.Contains("、、") || text.Contains('\n') || text.Contains('\r');
    }
}

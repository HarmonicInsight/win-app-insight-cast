namespace InsightCast.TTS;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

/// <summary>
/// 読み上げ辞書：ナレーションテキストの英単語・固有名詞をカタカナ/ひらがなに変換する。
/// 字幕テキストはそのまま、TTS に渡すテキストだけ置換する。
/// </summary>
public class NarrationDictionary
{
    /// <summary>プリセット辞書エントリ（上書き不可だが無効化可能）。</summary>
    public static readonly List<DictionaryEntry> Presets = new()
    {
        new("EXCEL", "エクセル"),
        new("Excel", "エクセル"),
        new("PowerPoint", "パワーポイント"),
        new("POWERPOINT", "パワーポイント"),
        new("Word", "ワード"),
        new("Outlook", "アウトルック"),
        new("Teams", "チームズ"),
        new("Windows", "ウィンドウズ"),
        new("Microsoft", "マイクロソフト"),
        new("Google", "グーグル"),
        new("Chrome", "クローム"),
        new("YouTube", "ユーチューブ"),
        new("AI", "エーアイ"),
        new("PDF", "ピーディーエフ"),
        new("CSV", "シーエスブイ"),
        new("URL", "ユーアールエル"),
        new("API", "エーピーアイ"),
        new("SQL", "エスキューエル"),
        new("HTML", "エイチティーエムエル"),
        new("CSS", "シーエスエス"),
        new("UI", "ユーアイ"),
        new("UX", "ユーエックス"),
        new("OS", "オーエス"),
        new("PC", "ピーシー"),
        new("USB", "ユーエスビー"),
        new("Wi-Fi", "ワイファイ"),
        new("WiFi", "ワイファイ"),
        new("Bluetooth", "ブルートゥース"),
        new("iPhone", "アイフォーン"),
        new("iPad", "アイパッド"),
        new("Android", "アンドロイド"),
        new("GitHub", "ギットハブ"),
        new("Slack", "スラック"),
        new("Zoom", "ズーム"),
        new("AWS", "エーダブリューエス"),
        new("Azure", "アジュール"),
        new("Docker", "ドッカー"),
        new("Linux", "リナックス"),
        new("macOS", "マックオーエス"),
        new("Python", "パイソン"),
        new("JavaScript", "ジャバスクリプト"),
        new("TypeScript", "タイプスクリプト"),
        new("Copilot", "コパイロット"),
        new("ChatGPT", "チャットジーピーティー"),
        new("OpenAI", "オープンエーアイ"),
        new("Claude", "クロード"),
        new("Anthropic", "アンソロピック"),
        new("InsightCast", "インサイトキャスト"),
        new("Harmonic Insight", "ハーモニック インサイト"),
    };

    /// <summary>
    /// ユーザーが追加したカスタム辞書エントリ。
    /// Config に保存される。
    /// </summary>
    public List<DictionaryEntry> CustomEntries { get; set; } = new();

    /// <summary>
    /// 無効化されたプリセットの From 値一覧。
    /// </summary>
    public List<string> DisabledPresets { get; set; } = new();

    /// <summary>
    /// 有効な全エントリ（カスタム優先 → プリセット）を取得する。
    /// </summary>
    public List<DictionaryEntry> GetEffectiveEntries()
    {
        var result = new List<DictionaryEntry>();

        // カスタムエントリを先に追加（優先）
        result.AddRange(CustomEntries);

        // カスタムに存在しない＆無効化されていないプリセットを追加
        // 完全一致（Ordinal）で比較：EXCEL と Excel は別エントリとして扱う
        var customFromSet = new HashSet<string>(CustomEntries.Select(e => e.From), StringComparer.Ordinal);
        foreach (var preset in Presets)
        {
            if (!customFromSet.Contains(preset.From) && !DisabledPresets.Contains(preset.From))
            {
                result.Add(preset);
            }
        }

        // 長い文字列から先に置換する（部分一致の誤置換防止）
        return result.OrderByDescending(e => e.From.Length).ToList();
    }

    // キャッシュ済みの正規表現とルックアップテーブル
    private Regex? _cachedRegex;
    private Dictionary<string, string>? _cachedLookup;

    /// <summary>
    /// 内部キャッシュを構築する。辞書変更後に1度だけ呼ばれる。
    /// </summary>
    private void EnsureCache()
    {
        if (_cachedRegex != null) return;

        var entries = GetEffectiveEntries()
            .Where(e => !string.IsNullOrEmpty(e.From))
            .ToList();

        if (entries.Count == 0)
        {
            _cachedRegex = null;
            _cachedLookup = null;
            return;
        }

        var pattern = string.Join("|", entries.Select(e => Regex.Escape(e.From)));
        _cachedRegex = new Regex(pattern, RegexOptions.Compiled);
        _cachedLookup = new Dictionary<string, string>();
        foreach (var e in entries)
            _cachedLookup.TryAdd(e.From, e.To);
    }

    /// <summary>
    /// ナレーションテキストに辞書を適用し、TTS 用テキストを返す。
    /// 連鎖置換を防ぐため、Regex による一括置換を行う。
    /// </summary>
    public string Apply(string narrationText)
    {
        if (string.IsNullOrEmpty(narrationText))
            return narrationText;

        EnsureCache();
        if (_cachedRegex == null || _cachedLookup == null)
            return narrationText;

        return _cachedRegex.Replace(narrationText, m =>
            _cachedLookup.TryGetValue(m.Value, out var to) ? to : m.Value);
    }
}

/// <summary>
/// 読み上げ辞書の1エントリ。
/// </summary>
public class DictionaryEntry
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;

    public DictionaryEntry() { }

    public DictionaryEntry(string from, string to)
    {
        From = from;
        To = to;
    }
}

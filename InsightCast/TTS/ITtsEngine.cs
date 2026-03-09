namespace InsightCast.TTS;

using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// TTS エンジンの種別。
/// </summary>
public enum TtsEngineType
{
    /// <summary>Microsoft Edge Neural TTS (Nanami/Keita) — 高品質・無料・要インターネット</summary>
    EdgeNeural,

    /// <summary>Windows 標準音声 (Ayumi/Haruka/Ichiro/Sayaka) — オフライン・追加インストール不要</summary>
    WindowsOneCore,

    /// <summary>VOICEVOX — 最高品質・30+キャラクター音声・要別途インストール</summary>
    VoiceVox
}

/// <summary>
/// TTS 話者情報。エンジン間で統一的に扱うためのモデル。
/// </summary>
public class TtsSpeaker
{
    /// <summary>エンジン内部の話者 ID（VoiceVox: int の文字列化、Edge: "ja-JP-NanamiNeural" 等）</summary>
    public string Id { get; set; } = "";

    /// <summary>UI 表示名（例: "Microsoft Nanami (女性)"）</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>性別（UI フィルタ用）</summary>
    public string Gender { get; set; } = "";

    /// <summary>元の int 型スタイル ID（VoiceVox 互換用。Edge/Windows では -1）</summary>
    public int StyleId { get; set; } = -1;
}

/// <summary>
/// 全 TTS エンジンが実装する共通インターフェース。
/// ExportService / MainWindowViewModel はこのインターフェース経由で TTS を呼び出す。
/// </summary>
public interface ITtsEngine
{
    /// <summary>エンジン種別</summary>
    TtsEngineType EngineType { get; }

    /// <summary>エンジンの表示名（設定画面用）</summary>
    string DisplayName { get; }

    /// <summary>
    /// エンジン固有の制約事項（企業向け説明）。
    /// 設定画面やヘルプで表示する。
    /// </summary>
    IReadOnlyList<string> Constraints { get; }

    /// <summary>
    /// エンジンが利用可能かチェック。
    /// </summary>
    /// <returns>バージョン文字列 or 状態文字列。利用不可なら null。</returns>
    Task<string?> CheckConnectionAsync();

    /// <summary>
    /// 利用可能な話者一覧を取得。
    /// </summary>
    Task<List<TtsSpeaker>> GetSpeakersAsync();

    /// <summary>
    /// テキストから音声（WAV）を生成。
    /// </summary>
    /// <param name="text">読み上げるテキスト</param>
    /// <param name="speakerId">話者の内部 ID 文字列</param>
    /// <param name="speedScale">速度倍率（1.0 = 標準）</param>
    /// <returns>WAV 形式のバイト配列</returns>
    Task<byte[]> GenerateAudioAsync(string text, string speakerId, double speedScale = 1.0);
}

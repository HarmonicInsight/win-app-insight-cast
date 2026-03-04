using System.Collections.Generic;
using System.Text.Json.Nodes;
using InsightCommon.AI;

namespace InsightCast.Services.Claude;

/// <summary>
/// 動画編集用の Claude Tool Use 定義（12ツール）
/// </summary>
public static class VideoToolDefinitions
{
    public static List<ToolDefinition> GetAll() => new()
    {
        GetScenes,
        SetSceneNarration,
        SetSceneSubtitle,
        GetProjectSummary,
        SetMultipleScenes,
        GetPptxNotes,
        GenerateThumbnail,
        AddScene,
        RemoveScene,
        MoveScene,
        SetSceneMedia,
        GenerateSceneImage,
        GenerateAbThumbnails,
        AddCtaEndcard,
    };

    public static readonly ToolDefinition GetScenes = new()
    {
        Name = "get_scenes",
        Description = "全シーンの情報を取得します。各シーンのタイトル、ナレーション、字幕、メディアパス、長さモードを返します。",
        InputSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject(),
            ["required"] = new JsonArray(),
        },
    };

    public static readonly ToolDefinition SetSceneNarration = new()
    {
        Name = "set_scene_narration",
        Description = "指定シーンのナレーションテキストを設定します。",
        InputSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["scene_index"] = new JsonObject
                {
                    ["type"] = "integer",
                    ["description"] = "シーンのインデックス（0始まり）",
                },
                ["narration_text"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "設定するナレーションテキスト",
                },
            },
            ["required"] = new JsonArray("scene_index", "narration_text"),
        },
    };

    public static readonly ToolDefinition SetSceneSubtitle = new()
    {
        Name = "set_scene_subtitle",
        Description = "指定シーンの字幕テキストを設定します。",
        InputSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["scene_index"] = new JsonObject
                {
                    ["type"] = "integer",
                    ["description"] = "シーンのインデックス（0始まり）",
                },
                ["subtitle_text"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "設定する字幕テキスト",
                },
            },
            ["required"] = new JsonArray("scene_index", "subtitle_text"),
        },
    };

    public static readonly ToolDefinition GetProjectSummary = new()
    {
        Name = "get_project_summary",
        Description = "プロジェクト全体のサマリーを取得します。シーン数、メディア有無、ナレーション有無、字幕有無などの統計を返します。",
        InputSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject(),
            ["required"] = new JsonArray(),
        },
    };

    public static readonly ToolDefinition SetMultipleScenes = new()
    {
        Name = "set_multiple_scenes",
        Description = "複数シーンのナレーションや字幕を一括で設定します。",
        InputSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["updates"] = new JsonObject
                {
                    ["type"] = "array",
                    ["description"] = "更新対象のシーン配列",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["scene_index"] = new JsonObject
                            {
                                ["type"] = "integer",
                                ["description"] = "シーンのインデックス（0始まり）",
                            },
                            ["narration_text"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["description"] = "ナレーションテキスト（省略時は変更なし）",
                            },
                            ["subtitle_text"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["description"] = "字幕テキスト（省略時は変更なし）",
                            },
                        },
                        ["required"] = new JsonArray("scene_index"),
                    },
                },
            },
            ["required"] = new JsonArray("updates"),
        },
    };

    public static readonly ToolDefinition GetPptxNotes = new()
    {
        Name = "get_pptx_notes",
        Description = "各シーンに関連するスライドノート（PPTXインポート時のスピーカーノート）を取得します。AIGenerationのnotesフィールドから読み取ります。",
        InputSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject(),
            ["required"] = new JsonArray(),
        },
    };

    public static readonly ToolDefinition GenerateThumbnail = new()
    {
        Name = "generate_thumbnail",
        Description = "サムネイル画像（1280×720 PNG）を生成します。パターンとスタイルを指定してプロ品質のサムネイルを作成できます。",
        InputSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["pattern"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "サムネイルのレイアウトパターン",
                    ["enum"] = new JsonArray(
                        "PowerWord", "MainSub", "ThreeLine",
                        "EmotionalFace", "BeforeAfter", "BlackBox",
                        "Grid4", "NumbersFocus", "FocusLines",
                        "Question", "GenreAesthetic"),
                },
                ["style"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "配色・エフェクトのスタイルプリセット（デフォルト: BusinessImpact）",
                    ["enum"] = new JsonArray(
                        "BusinessImpact", "TrustManual", "ShockReveal",
                        "Elegant", "Pop", "Custom"),
                },
                ["main_text"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "メインテキスト（最もインパクトのある短いフレーズ）",
                },
                ["sub_text"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "サブテキスト（補足説明、省略可）",
                },
                ["sub_sub_text"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "サブサブテキスト（追加情報、省略可）",
                },
                ["background_image_scene_index"] = new JsonObject
                {
                    ["type"] = "integer",
                    ["description"] = "背景画像に使用するシーンのインデックス（0始まり、省略時は単色背景）",
                },
            },
            ["required"] = new JsonArray("pattern", "main_text"),
        },
    };

    public static readonly ToolDefinition AddScene = new()
    {
        Name = "add_scene",
        Description = "新しいシーンを追加します。insert_atを指定するとその位置に挿入、省略時は末尾に追加します。",
        InputSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["insert_at"] = new JsonObject
                {
                    ["type"] = "integer",
                    ["description"] = "挿入位置のインデックス（0始まり）。省略時は末尾に追加",
                },
            },
            ["required"] = new JsonArray(),
        },
    };

    public static readonly ToolDefinition RemoveScene = new()
    {
        Name = "remove_scene",
        Description = "指定したインデックスのシーンを削除します。最後の1つは削除できません。",
        InputSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["scene_index"] = new JsonObject
                {
                    ["type"] = "integer",
                    ["description"] = "削除するシーンのインデックス（0始まり）",
                },
            },
            ["required"] = new JsonArray("scene_index"),
        },
    };

    public static readonly ToolDefinition MoveScene = new()
    {
        Name = "move_scene",
        Description = "シーンの順序を変更します。from_indexのシーンをto_indexの位置に移動します。",
        InputSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["from_index"] = new JsonObject
                {
                    ["type"] = "integer",
                    ["description"] = "移動元のシーンインデックス（0始まり）",
                },
                ["to_index"] = new JsonObject
                {
                    ["type"] = "integer",
                    ["description"] = "移動先のシーンインデックス（0始まり）",
                },
            },
            ["required"] = new JsonArray("from_index", "to_index"),
        },
    };

    public static readonly ToolDefinition SetSceneMedia = new()
    {
        Name = "set_scene_media",
        Description = "指定シーンにメディア（画像ファイル）を設定します。ローカルファイルパスを指定してください。",
        InputSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["scene_index"] = new JsonObject
                {
                    ["type"] = "integer",
                    ["description"] = "シーンのインデックス（0始まり）",
                },
                ["media_path"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "設定する画像ファイルの絶対パス",
                },
            },
            ["required"] = new JsonArray("scene_index", "media_path"),
        },
    };

    public static readonly ToolDefinition GenerateSceneImage = new()
    {
        Name = "generate_scene_image",
        Description = "DALL-Eで画像を生成し、指定シーンのメディアとして自動設定します。OpenAI APIキーが必要です。",
        InputSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["scene_index"] = new JsonObject
                {
                    ["type"] = "integer",
                    ["description"] = "シーンのインデックス（0始まり）",
                },
                ["prompt"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "DALL-Eに渡す画像生成プロンプト（英語推奨）",
                },
                ["size"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "画像サイズ（デフォルト: 1024x1024）",
                    ["enum"] = new JsonArray("1024x1024", "1792x1024", "1024x1792"),
                },
            },
            ["required"] = new JsonArray("scene_index", "prompt"),
        },
    };

    public static readonly ToolDefinition GenerateAbThumbnails = new()
    {
        Name = "generate_ab_thumbnails",
        Description = "A/Bテスト用に複数パターンのサムネイルを一括生成します。5種類のパターン×スタイルの組み合わせで高CTRサムネイルを比較できます。",
        InputSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["main_text"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "サムネイルのメインテキスト",
                },
                ["sub_text"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "サブテキスト（省略可）",
                },
                ["sub_sub_text"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "補足テキスト（省略可）",
                },
            },
            ["required"] = new JsonArray("main_text"),
        },
    };

    public static readonly ToolDefinition AddCtaEndcard = new()
    {
        Name = "add_cta_endcard",
        Description = "動画の最後にCTAエンドカードシーンを追加します。templateで用途を選択できます: 'subscribe'（チャンネル登録）、'education'（教育・コンサル向け: 次回予告+個別相談+資料案内）。省略時はsubscribe。",
        InputSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["template"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "CTAテンプレート種別。subscribe: チャンネル登録+リンク誘導。education: 次回予告+個別相談+資料案内（教育動画・コンサル向け）。",
                    ["enum"] = new JsonArray("subscribe", "education"),
                },
                ["cta_text"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "CTAメッセージ（subscribe時: 登録文言、education時: 次回予告文言）。省略時はデフォルト文言を使用。",
                },
                ["link_text"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "リンク誘導メッセージ（subscribe時）またはダウンロード誘導（education時）。省略時はデフォルト文言を使用。",
                },
                ["consult_text"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "個別相談・問い合わせ誘導文言（education時のみ）。省略時はデフォルト文言を使用。",
                },
            },
        },
    };
}

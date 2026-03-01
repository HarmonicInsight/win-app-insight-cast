using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using InsightCast.Models;

namespace InsightCast.Services;

public static class PromptLibraryService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string PromptDirectory
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "InsightCast", "Prompts");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static void SavePrompt(UserPrompt prompt)
    {
        try
        {
            var path = Path.Combine(PromptDirectory, $"{prompt.Id}.json");
            var json = JsonSerializer.Serialize(prompt, Options);
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WARN] Prompt save failed: {prompt.Id}: {ex.Message}");
        }
    }

    public static List<UserPrompt> LoadAllPrompts()
    {
        var prompts = new List<UserPrompt>();
        if (!Directory.Exists(PromptDirectory))
            return prompts;

        foreach (var file in Directory.GetFiles(PromptDirectory, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var prompt = JsonSerializer.Deserialize<UserPrompt>(json, Options);
                if (prompt != null)
                    prompts.Add(prompt);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WARN] Prompt load failed: {file}: {ex.Message}");
            }
        }

        return prompts.OrderByDescending(p => p.UpdatedAt).ToList();
    }

    public static bool DeletePrompt(string id)
    {
        try
        {
            var path = Path.Combine(PromptDirectory, $"{id}.json");
            if (File.Exists(path))
            {
                File.Delete(path);
                return true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WARN] Prompt delete failed: {id}: {ex.Message}");
        }
        return false;
    }

    public static void IncrementUseCount(string id)
    {
        var path = Path.Combine(PromptDirectory, $"{id}.json");
        if (!File.Exists(path)) return;

        try
        {
            var json = File.ReadAllText(path);
            var prompt = JsonSerializer.Deserialize<UserPrompt>(json, Options);
            if (prompt != null)
            {
                prompt.UseCount++;
                prompt.LastUsedAt = DateTime.Now;
                File.WriteAllText(path, JsonSerializer.Serialize(prompt, Options));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WARN] Prompt use count update failed: {id}: {ex.Message}");
        }
    }

    /// <summary>
    /// 初回起動時にデフォルトのマイプロンプトを5つ登録する。
    /// 既にプロンプトが存在する場合はスキップ。
    /// </summary>
    public static void SeedDefaultPrompts()
    {
        if (LoadAllPrompts().Count > 0)
            return;

        var defaults = new[]
        {
            new UserPrompt
            {
                Id = "default01",
                Label = "動画の概要を教えて",
                Icon = "\U0001F4CB", // 📋
                Category = "全般",
                Prompt = "get_scenesツールとget_project_summaryツールを使って、この動画プロジェクトの全体像を教えてください。シーン数、ナレーション・字幕の設定状況、画像の有無などを一覧で見やすくまとめてください。",
            },
            new UserPrompt
            {
                Id = "default02",
                Label = "全シーンにナレーション追加",
                Icon = "\U0001F3A4", // 🎤
                Category = "ナレーション",
                Prompt = "get_scenesツールで全シーンを取得し、まだナレーションが設定されていないシーンに対して、スライド画像の内容に合った教育的なナレーションテキストを生成してください。1シーン100〜150文字程度で、ですます調で統一してください。set_multiple_scenesで設定してください。",
            },
            new UserPrompt
            {
                Id = "default03",
                Label = "字幕を一括生成",
                Icon = "\U0001F4DD", // 📝
                Category = "字幕",
                Prompt = "get_scenesツールでシーン情報を取得し、ナレーションテキストから字幕テキストを生成してください。字幕は1行20文字以内で、ナレーションの要点を簡潔にまとめてください。set_multiple_scenesで設定してください。",
            },
            new UserPrompt
            {
                Id = "default04",
                Label = "プロジェクト進捗チェック",
                Icon = "\u2705", // ✅
                Category = "全般",
                Prompt = "get_scenesツールとget_project_summaryツールを使って、プロジェクトの完成度を確認してください。以下をチェックしてレポートしてください:\n\n- 画像未設定のシーン\n- ナレーション未入力のシーン\n- 字幕未設定のシーン\n- 全体の完了率（%）\n- 次にやるべきことの提案",
            },
            new UserPrompt
            {
                Id = "default05",
                Label = "動画の改善提案",
                Icon = "\U0001F4A1", // 💡
                Category = "品質改善",
                Prompt = "get_scenesツールで動画の全内容を分析し、品質向上のための具体的な改善提案をしてください:\n\n1. ナレーションの文体・トーンの統一感\n2. シーン間の流れの自然さ\n3. 字幕の読みやすさ\n4. 全体の長さバランス\n5. 教育効果を高めるためのアドバイス\n\n改善点があればset_multiple_scenesで修正してください。",
            },
        };

        foreach (var prompt in defaults)
        {
            SavePrompt(prompt);
        }
    }
}

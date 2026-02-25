using System;
using System.Security.Cryptography;
using System.Text;
using InsightCast.Core;

namespace InsightCast.Services.OpenAI
{
    /// <summary>
    /// OpenAI APIキーの安全な保存と取得を管理します。
    /// Windows Data Protection API (DPAPI) を使用して暗号化します。
    /// </summary>
    public static class ApiKeyManager
    {
        /// <summary>
        /// 環境変数からAPIキーを取得します。
        /// </summary>
        /// <returns>APIキー、または見つからない場合はnull。</returns>
        public static string? GetApiKeyFromEnvironment()
        {
            return Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        }

        /// <summary>
        /// Configから暗号化されたAPIキーを復号して取得します。
        /// </summary>
        /// <param name="config">Configインスタンス。</param>
        /// <returns>復号されたAPIキー、または失敗時はnull。</returns>
        public static string? GetApiKey(Config config)
        {
            // まず環境変数をチェック
            var envKey = GetApiKeyFromEnvironment();
            if (!string.IsNullOrEmpty(envKey))
                return envKey;

            // Configから取得して復号
            var encrypted = config.OpenAIApiKey;
            if (string.IsNullOrEmpty(encrypted))
                return null;

            return Decrypt(encrypted);
        }

        /// <summary>
        /// APIキーを暗号化してConfigに保存します。
        /// </summary>
        /// <param name="config">Configインスタンス。</param>
        /// <param name="apiKey">保存するAPIキー。</param>
        public static void SaveApiKey(Config config, string apiKey)
        {
            if (string.IsNullOrEmpty(apiKey))
            {
                config.OpenAIApiKey = null;
                return;
            }

            config.OpenAIApiKey = Encrypt(apiKey);
        }

        /// <summary>
        /// Configに保存されたAPIキーをクリアします。
        /// </summary>
        /// <param name="config">Configインスタンス。</param>
        public static void ClearApiKey(Config config)
        {
            config.OpenAIApiKey = null;
        }

        /// <summary>
        /// JSON設定ファイル内のAPIキー参照を解決します。
        /// ${ENV_VAR} 形式の場合は環境変数から取得します。
        /// </summary>
        /// <param name="keyRef">APIキーまたは環境変数参照。</param>
        /// <returns>解決されたAPIキー。</returns>
        public static string? ResolveApiKeyReference(string? keyRef)
        {
            if (string.IsNullOrEmpty(keyRef))
                return null;

            // ${ENV_VAR} 形式の場合は環境変数から取得
            if (keyRef.StartsWith("${") && keyRef.EndsWith("}"))
            {
                var envVar = keyRef[2..^1];
                return Environment.GetEnvironmentVariable(envVar);
            }

            return keyRef;
        }

        /// <summary>
        /// APIキーが有効な形式かどうかを検証します。
        /// </summary>
        /// <param name="apiKey">検証するAPIキー。</param>
        /// <returns>有効な形式の場合はtrue。</returns>
        public static bool IsValidApiKeyFormat(string? apiKey)
        {
            if (string.IsNullOrEmpty(apiKey))
                return false;

            // OpenAI APIキーは "sk-" で始まる
            return apiKey.StartsWith("sk-") && apiKey.Length > 20;
        }

        private static string Encrypt(string plainText)
        {
            try
            {
                var bytes = Encoding.UTF8.GetBytes(plainText);
                var encrypted = ProtectedData.Protect(
                    bytes,
                    null,
                    DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(encrypted);
            }
            catch
            {
                // 暗号化に失敗した場合はそのまま返す（非推奨だが動作は継続）
                return plainText;
            }
        }

        private static string? Decrypt(string encryptedText)
        {
            try
            {
                var bytes = Convert.FromBase64String(encryptedText);
                var decrypted = ProtectedData.Unprotect(
                    bytes,
                    null,
                    DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(decrypted);
            }
            catch
            {
                // 復号に失敗した場合（未暗号化データの可能性）
                // sk- で始まる場合は生のAPIキーとして扱う
                if (encryptedText.StartsWith("sk-"))
                    return encryptedText;
                return null;
            }
        }
    }
}

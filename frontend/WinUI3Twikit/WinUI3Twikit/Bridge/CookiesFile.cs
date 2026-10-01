using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace WinUI3Twikit.Bridge
{
    /// <summary>
    /// <c>cookies.json</c>（<c>{"auth_token": ..., "ct0": ...}</c>）の読み書き。設定画面とブリッジで共有する。
    /// </summary>
    internal static class CookiesFile
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        /// <summary>保存先（<see cref="RepositoryPaths.CookiesPath"/>）。</summary>
        public static string Path => RepositoryPaths.CookiesPath;

        /// <summary>
        /// auth_token / ct0 を保存する。フォルダーが無ければ作る（単一 exe を初めて使うときは
        /// exe の隣に <c>data\</c> がまだ無い）。前後の空白・改行は取り除く。
        /// </summary>
        /// <returns>書き込んだファイルのパス。</returns>
        public static string Save(string path, string? authToken, string? ct0)
        {
            var dict = new Dictionary<string, string>
            {
                ["auth_token"] = (authToken ?? string.Empty).Trim(),
                ["ct0"] = (ct0 ?? string.Empty).Trim(),
            };

            var directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, JsonSerializer.Serialize(dict, JsonOptions));
            return path;
        }

        /// <summary>
        /// 保存済みの値を読む。ファイルが無い・壊れているときは false（例外は投げない）。
        /// </summary>
        public static bool TryRead(string path, out string authToken, out string ct0)
        {
            authToken = string.Empty;
            ct0 = string.Empty;
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                if (document.RootElement.TryGetProperty("auth_token", out var auth) && auth.ValueKind == JsonValueKind.String)
                {
                    authToken = auth.GetString() ?? string.Empty;
                }

                if (document.RootElement.TryGetProperty("ct0", out var csrf) && csrf.ValueKind == JsonValueKind.String)
                {
                    ct0 = csrf.GetString() ?? string.Empty;
                }

                return true;
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                System.Diagnostics.Debug.WriteLine($"CookiesFile: {path} を読めません: {ex.Message}");
                return false;
            }
        }
    }
}

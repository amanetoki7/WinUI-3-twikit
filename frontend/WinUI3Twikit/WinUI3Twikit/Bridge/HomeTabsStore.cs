using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinUI3Twikit.Bridge
{
    /// <summary>
    /// ホームのタブ構成（「+」で追加したリスト）の保存・復元。
    /// <c>home_tabs.json</c> を <c>cookies.json</c> と同じフォルダーに置く（<see cref="RepositoryPaths.HomeTabsPath"/>）。
    /// </summary>
    internal static class HomeTabsStore
    {
        /// <summary>ファイル形式のバージョン。形を変えるときに上げる。</summary>
        private const int CurrentVersion = 1;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            // リスト名の日本語をそのまま書く（\uXXXX にしない）
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        private sealed class TabsFile
        {
            [JsonPropertyName("version")]
            public int Version { get; set; } = CurrentVersion;

            [JsonPropertyName("list_tabs")]
            public List<ListTabEntry?>? ListTabs { get; set; }
        }

        private sealed class ListTabEntry
        {
            [JsonPropertyName("id")]
            public string? Id { get; set; }

            [JsonPropertyName("name")]
            public string? Name { get; set; }
        }

        public static string Path => RepositoryPaths.HomeTabsPath;

        /// <summary>保存済みのタブ。ファイルが無い・壊れているときは空（例外は投げない）。</summary>
        public static List<HomeListTab> Load()
        {
            var path = Path;
            if (!File.Exists(path))
            {
                return [];
            }

            try
            {
                var file = JsonSerializer.Deserialize<TabsFile>(File.ReadAllText(path), JsonOptions);
                var tabs = new List<HomeListTab>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in file?.ListTabs ?? [])
                {
                    var id = (entry?.Id ?? string.Empty).Trim();
                    if (id.Length == 0 || !seen.Add(id))
                    {
                        continue;
                    }

                    tabs.Add(new HomeListTab { ListId = id, Name = (entry!.Name ?? string.Empty).Trim() });
                }

                return tabs;
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"HomeTabsStore: {path} を読めません: {ex.Message}");
                return [];
            }
        }

        /// <summary>タブの並びをそのまま保存する。失敗してもアプリは続行する（ログのみ）。</summary>
        public static void Save(IEnumerable<HomeListTab> tabs)
        {
            var path = Path;
            try
            {
                var directory = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var file = new TabsFile
                {
                    ListTabs = [.. tabs.Select(t => new ListTabEntry { Id = t.ListId, Name = t.Name })],
                };
                File.WriteAllText(path, JsonSerializer.Serialize(file, JsonOptions));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"HomeTabsStore: {path} に保存できません: {ex.Message}");
            }
        }
    }
}

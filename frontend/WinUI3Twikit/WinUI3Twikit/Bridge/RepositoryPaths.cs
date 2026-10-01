using System;
using System.IO;
using System.Reflection;

namespace WinUI3Twikit.Bridge
{
    /// <summary>リポジトリ相対パスの解決（旧 <c>backend/paths.py</c>）。</summary>
    internal static class RepositoryPaths
    {
        private static string? _repositoryRoot;
        private static bool _repositoryRootResolved;

        /// <summary>リポジトリルート（<c>data/</c> と <c>frontend/</c> を持つディレクトリ）。見つからなければ null。</summary>
        public static string? RepositoryRoot
        {
            get
            {
                if (!_repositoryRootResolved)
                {
                    _repositoryRoot = FindRepositoryRoot();
                    _repositoryRootResolved = true;
                }

                return _repositoryRoot;
            }
        }

        /// <summary>
        /// cookies.json のパス。環境変数 <c>COOKIES_FILE</c> → <c>WINUI3TWIKIT_ROOT</c>/data/cookies.json →
        /// 実行ファイルの場所から上へ辿って見つかる data/cookies.json → リポジトリルート/data/cookies.json の順
        /// （設定画面の解決順と同じ。設定画面で保存したファイルがそのまま使われる）。
        /// </summary>
        public static string CookiesPath
        {
            get
            {
                var configured = Environment.GetEnvironmentVariable("COOKIES_FILE");
                if (!string.IsNullOrWhiteSpace(configured))
                {
                    return configured;
                }

                var root = Environment.GetEnvironmentVariable("WINUI3TWIKIT_ROOT");
                if (!string.IsNullOrWhiteSpace(root))
                {
                    return Path.Combine(root, "data", "cookies.json");
                }

                var directory = new DirectoryInfo(AppContext.BaseDirectory);
                while (directory != null)
                {
                    var candidate = Path.Combine(directory.FullName, "data", "cookies.json");
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }

                    directory = directory.Parent;
                }

                var repositoryRoot = RepositoryRoot;
                if (repositoryRoot is not null)
                {
                    return Path.Combine(repositoryRoot, "data", "cookies.json");
                }

                return Path.Combine(AppContext.BaseDirectory, "data", "cookies.json");
            }
        }

        /// <summary><c>static/favicon.ico</c>（旧 API の <c>/favicon.ico</c> 用）。</summary>
        public static string? FaviconPath
            => RepositoryRoot is { } root ? Path.Combine(root, "static", "favicon.ico") : null;

        private static string? FindRepositoryRoot()
        {
            var candidates = new[]
            {
                Environment.GetEnvironmentVariable("WINUI3TWIKIT_ROOT"),
                Directory.GetCurrentDirectory(),
                AppContext.BaseDirectory,
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
            };

            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                var directory = new DirectoryInfo(candidate);
                while (directory != null)
                {
                    if (Directory.Exists(Path.Combine(directory.FullName, "data"))
                        && Directory.Exists(Path.Combine(directory.FullName, "frontend")))
                    {
                        return directory.FullName;
                    }

                    directory = directory.Parent;
                }
            }

            return null;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace WinUI3Twikit.Bridge
{
    /// <summary>リポジトリ相対パスの解決（旧 <c>backend/paths.py</c>）。</summary>
    internal static class RepositoryPaths
    {
        private static string? _repositoryRoot;
        private static bool _repositoryRootResolved;

        /// <summary>
        /// 実行ファイルのあるディレクトリ。単一 exe（PublishSingleFile）では <see cref="AppContext.BaseDirectory"/> が
        /// <c>%TEMP%\.net\...</c> の展開先を指すため、ユーザーが exe を置いた場所はこちらで取る。
        /// </summary>
        public static string ExecutableDirectory
        {
            get
            {
                var processPath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(processPath)
                    || string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
                {
                    // `dotnet App.dll` で起動されたときは dotnet.exe の場所ではなくアプリの場所を使う。
                    return AppContext.BaseDirectory;
                }

                var directory = Path.GetDirectoryName(processPath);
                return string.IsNullOrEmpty(directory) ? AppContext.BaseDirectory : directory;
            }
        }

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
        /// cookies.json のパス。次の順で決める（設定画面の保存先も同じ）。
        /// <list type="number">
        /// <item>環境変数 <c>COOKIES_FILE</c></item>
        /// <item>環境変数 <c>WINUI3TWIKIT_ROOT</c> → <c>data/cookies.json</c></item>
        /// <item>exe の場所（および展開先）から上へ辿って見つかる既存の <c>data/cookies.json</c></item>
        /// <item>リポジトリルート（開発時）→ <c>data/cookies.json</c></item>
        /// <item>exe の隣の <c>data/cookies.json</c>（単一 exe の既定。まだ無ければ保存時に作られる）</item>
        /// </list>
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

                foreach (var start in new[] { ExecutableDirectory, AppContext.BaseDirectory })
                {
                    var existing = FindUpward(start, directory => File.Exists(Path.Combine(directory, "data", "cookies.json")));
                    if (existing is not null)
                    {
                        return Path.Combine(existing, "data", "cookies.json");
                    }
                }

                var repositoryRoot = RepositoryRoot;
                if (repositoryRoot is not null)
                {
                    return Path.Combine(repositoryRoot, "data", "cookies.json");
                }

                return Path.Combine(ExecutableDirectory, "data", "cookies.json");
            }
        }

        /// <summary><c>static/favicon.ico</c>（旧 API の <c>/favicon.ico</c> 用）。</summary>
        public static string? FaviconPath
            => RepositoryRoot is { } root ? Path.Combine(root, "static", "favicon.ico") : null;

        private static string? FindRepositoryRoot()
        {
            var candidates = new List<string?>
            {
                Environment.GetEnvironmentVariable("WINUI3TWIKIT_ROOT"),
                Directory.GetCurrentDirectory(),
                ExecutableDirectory,
                AppContext.BaseDirectory,
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
            };

            foreach (var candidate in candidates)
            {
                var found = FindUpward(
                    candidate,
                    directory => Directory.Exists(Path.Combine(directory, "data"))
                                 && Directory.Exists(Path.Combine(directory, "frontend")));
                if (found is not null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary><paramref name="start"/> から親へ辿り、条件を満たす最初のディレクトリを返す。</summary>
        private static string? FindUpward(string? start, Func<string, bool> predicate)
        {
            if (string.IsNullOrWhiteSpace(start))
            {
                return null;
            }

            DirectoryInfo? directory;
            try
            {
                directory = new DirectoryInfo(start);
            }
            catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
            {
                return null;
            }

            while (directory != null)
            {
                if (predicate(directory.FullName))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            return null;
        }
    }
}

using Microsoft.UI;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Threading.Tasks;
using Windows.UI;

namespace WinUI3Twikit
{
    public class ServerManager
    {
        private static Process? _pythonProcess;
        private const string Host = "127.0.0.1";
        private const int Port = 8000;
        private const string StartupCompleteMarker = "Application startup complete";

        public static async Task StartServerAsync(TextBlock statusTextBlock)
        {
            if (IsPortInUse(Port))
            {
                UpdateStatus(statusTextBlock, "API Server: Already Running ✅", Colors.LimeGreen);
                return;
            }

            UpdateStatus(statusTextBlock, "API Server: Starting…", Colors.Yellow);

            try
            {
                string backendDirectory = FindBackendDirectory();
                string workingDir = Directory.GetParent(backendDirectory)?.FullName
                    ?? throw new DirectoryNotFoundException("Repository root was not found.");

                var startupComplete = new TaskCompletionSource<bool>();

                _pythonProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "uvicorn",
                        Arguments = $"backend.api:app --host {Host} --port {Port}",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WorkingDirectory = workingDir
                    }
                };

                void OnUvicornLog(string? line)
                {
                    if (string.IsNullOrEmpty(line))
                    {
                        return;
                    }

                    Debug.WriteLine($"Uvicorn: {line}");
                    if (!line.Contains(StartupCompleteMarker))
                    {
                        return;
                    }

                    startupComplete.TrySetResult(true);
                    App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
                    {
                        if (App.IsShuttingDown)
                        {
                            return;
                        }

                        UpdateStatus(statusTextBlock, "API Server: Running ✅", Colors.LimeGreen);
                    });
                }

                _pythonProcess.OutputDataReceived += (_, e) => OnUvicornLog(e.Data);
                _pythonProcess.ErrorDataReceived += (_, e) => OnUvicornLog(e.Data);

                _pythonProcess.Start();
                _pythonProcess.BeginOutputReadLine();
                _pythonProcess.BeginErrorReadLine();

                await Task.WhenAny(startupComplete.Task, Task.Delay(TimeSpan.FromSeconds(30)));

                if (startupComplete.Task.IsCompleted)
                {
                    return;
                }

                if (IsPortInUse(Port))
                {
                    UpdateStatus(statusTextBlock, "API Server: Running ✅", Colors.LimeGreen);
                }
                else
                {
                    UpdateStatus(statusTextBlock, "API Server: Failed to Start ❌", Colors.Red);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus(statusTextBlock, $"API Error: {ex.Message}", Colors.Red);
                Debug.WriteLine($"Server start failed: {ex}");
            }
        }

        private static string FindBackendDirectory()
        {
            var configuredRoot = Environment.GetEnvironmentVariable("WINUI3TWIKIT_ROOT");
            var candidates = new[]
            {
                configuredRoot,
                Directory.GetCurrentDirectory(),
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
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
                    var backend = Path.Combine(directory.FullName, "backend");
                    if (File.Exists(Path.Combine(backend, "api.py")))
                    {
                        return backend;
                    }

                    directory = directory.Parent;
                }
            }

            throw new DirectoryNotFoundException(
                "backend/api.py was not found. Set WINUI3TWIKIT_ROOT to the repository root.");
        }

        private static bool IsPortInUse(int port)
        {
            try
            {
                using var client = new TcpClient();
                var result = client.BeginConnect(Host, port, null, null);
                bool success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(1));
                if (success) client.EndConnect(result);
                return success;
            }
            catch { return false; }
        }

        private static void UpdateStatus(TextBlock? textBlock, string message, Color color)
        {
            if (App.IsShuttingDown || textBlock == null)
            {
                return;
            }

            textBlock.Text = message;
            textBlock.Foreground = new SolidColorBrush(color);
        }

        public static void StopServer()
        {
            if (_pythonProcess == null)
            {
                return;
            }

            try
            {
                if (!_pythonProcess.HasExited)
                {
                    _pythonProcess.Kill(true);
                }
            }
            catch { }
            finally
            {
                _pythonProcess.Dispose();
                _pythonProcess = null;
            }
        }

        /// <summary>
        /// auth_token / ct0 など cookie 更新後に反映するため、API サーバーを停止して起動し直す。
        /// </summary>
        public static async Task RestartServerAsync(TextBlock statusTextBlock)
        {
            UpdateStatus(statusTextBlock, "API Server: Restarting…", Colors.Yellow);

            StopServer();

            // ポート解放待ち（Kill 直後は TIME_WAIT 等でまだ使用中のことがある）
            for (var i = 0; i < 50; i++)
            {
                if (!IsPortInUse(Port))
                {
                    break;
                }

                await Task.Delay(100);
            }

            if (IsPortInUse(Port))
            {
                UpdateStatus(statusTextBlock, "API Server: Restart Failed (port busy) ❌", Colors.Red);
                return;
            }

            await StartServerAsync(statusTextBlock);
        }
    }
}

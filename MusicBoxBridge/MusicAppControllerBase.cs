using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32; // 添加 Registry 操作命名空间 for path finding
namespace MusicBridge
{
    public abstract class MusicAppControllerBase : IMusicAppController
        {
            public abstract string Name { get; }
            public abstract string ProcessName { get; }
            private string? _executablePath = null; // 缓存找到的路径
            private bool _pathSearched = false;    // 标记是否已尝试查找路径

            // 公开获取路径的属性
            public string? ExecutablePath => _executablePath;

            // 子类需要提供默认的可执行文件名，用于从注册表 InstallLocation 推断完整路径
            protected abstract string DefaultExeName { get; }

            // 默认的 IsRunning 实现
            public virtual bool IsRunning() => Process.GetProcessesByName(ProcessName).Length > 0;

            // 异步查找可执行文件路径 (模板方法)
            public async Task<string?> FindExecutablePathAsync()
            {
                // 如果已经找到，直接返回缓存结果
                if (_executablePath != null) return _executablePath;
                // 如果已经搜索过但没找到，也直接返回 null
                if (_pathSearched) return null;

                Debug.WriteLine($"[{Name}] 开始查找可执行文件路径...");
                string? path = await Task.Run(() => FindPathFromRegistry()); // 在后台线程执行查找

                // 可以在这里添加其他查找策略，例如检查默认安装位置
                if (path == null)
                {
                    path = CheckDefaultInstallLocations();
                }

                _executablePath = path;
                _pathSearched = true; // 标记已搜索过

                if (_executablePath != null)
                {
                    Debug.WriteLine($"[{Name}] 找到路径: {_executablePath}");
                }
                else
                {
                    Debug.WriteLine($"[{Name}] 未能自动找到路径。");
                }

                return _executablePath;
            }

            // 从注册表查找路径的具体实现
            public string? FindPathFromRegistry()
            {
                // 尝试在不同的注册表位置查找应用的卸载信息
                string? path = SearchRegistryUninstallKey(Registry.CurrentUser, $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall") ??
                               SearchRegistryUninstallKey(Registry.LocalMachine, $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall") ??
                               SearchRegistryUninstallKey(Registry.LocalMachine, $@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"); // 针对64位系统上的32位应用

                // 有些应用可能在 HKCU 的 WOW6432Node 下（不常见，但可以加上）
                if (path == null)
                {
                    path = SearchRegistryUninstallKey(Registry.CurrentUser, $@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall");
                }

                return path;
            }

            // 搜索单个注册表根键下的卸载项
            private string? SearchRegistryUninstallKey(RegistryKey baseKey, string keyPath)
            {
                try
                {
                    using (RegistryKey? key = baseKey.OpenSubKey(keyPath))
                    {
                        if (key == null) return null;

                        foreach (string subKeyName in key.GetSubKeyNames())
                        {
                            using (RegistryKey? subKey = key.OpenSubKey(subKeyName))
                            {
                                if (subKey == null) continue;

                                object? displayNameObj = subKey.GetValue("DisplayName");
                                object? installLocationObj = subKey.GetValue("InstallLocation");
                                object? displayIconObj = subKey.GetValue("DisplayIcon");

                                // 优先匹配 DisplayName
                                if (displayNameObj != null && displayNameObj.ToString()?.IndexOf(Name, StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    // 尝试使用 InstallLocation 拼接默认 Exe 名称
                                    if (installLocationObj != null && !string.IsNullOrWhiteSpace(installLocationObj.ToString()))
                                    {
                                        string potentialPath = Path.Combine(installLocationObj.ToString()!, DefaultExeName);
                                        if (File.Exists(potentialPath)) return potentialPath;
                                        // 有些应用的 InstallLocation 可能不包含子目录，需要额外尝试
                                        // 例如 酷狗的 InstallLocation 可能是 C:\Program Files (x86)\KuGou\, 需要拼接 KGMusic\KuGou.exe
                                        // 这部分逻辑可以在子类中覆盖 FindPathFromRegistry() 来实现
                                    }
                                    // 尝试从 DisplayIcon 提取路径
                                    if (displayIconObj != null && !string.IsNullOrWhiteSpace(displayIconObj.ToString()))
                                    {
                                        string iconPath = displayIconObj.ToString()!.Trim('"');
                                        // DisplayIcon 可能包含参数 ",0" 等，需要移除
                                        string potentialPath = iconPath.Split(',')[0];
                                        if (File.Exists(potentialPath) && Path.GetExtension(potentialPath).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                                        {
                                            return potentialPath;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[注册表搜索错误] 搜索 {baseKey.Name}\\{keyPath} 时出错: {ex.Message}");
                }
                return null;
            }

            // 检查默认安装位置 (需要子类根据情况实现)
            protected virtual string? CheckDefaultInstallLocations()
            {
                // 子类可以重写此方法来检查 C:\Program Files, AppData 等
                return null;
            }

            // 启动应用 (异步)
            public virtual async Task LaunchAsync()
            {
                if (IsRunning())
                {
                    Debug.WriteLine($"[{Name}] 已经在运行。");
                    return;
                }

                // 确保已尝试查找路径
                if (!_pathSearched)
                {
                    await FindExecutablePathAsync();
                }

                if (string.IsNullOrEmpty(_executablePath) || !File.Exists(_executablePath))
                {
                    string errorMsg = $"无法找到 {Name} 的可执行文件路径。\n请确保 {Name} 已正确安装，或尝试手动配置路径。";
                    Debug.WriteLine($"[LaunchAsync] {errorMsg}");
                    MessageBox.Show(errorMsg, "启动失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                try
                {
                    // 使用 using 确保 Process 资源被释放
                    using (var process = new Process())
                    {
                        process.StartInfo.FileName = _executablePath;
                        process.StartInfo.UseShellExecute = true; // 使用 ShellExecute 通常更好，可以处理 UAC 等问题
                        process.Start();
                    }
                    await Task.Delay(500); // 给进程一点启动时间
                    Debug.WriteLine($"[{Name}] 启动命令已发送。");
                }
                catch (Exception ex)
                {
                    string errorMsg = $"启动 {Name} 失败: {ex.Message}\n路径: {_executablePath}";
                    Debug.WriteLine($"[LaunchAsync] {errorMsg}");
                    MessageBox.Show(errorMsg, "启动错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            // 关闭应用 (异步，尝试优雅关闭)
            public virtual async Task CloseAppAsync()
            {
                Process[] processes = Process.GetProcessesByName(ProcessName);
                if (processes.Length == 0)
                {
                    Debug.WriteLine($"[{Name}] 未找到运行中的进程。");
                    return;
                }

                Debug.WriteLine($"[{Name}] 找到 {processes.Length} 个进程，尝试关闭...");

                foreach (Process process in processes)
                {
                    using (process) // 确保每个 process 对象被释放
                    {
                        try
                        {
                            if (!process.HasExited)
                            {
                                Debug.WriteLine($"[{Name}] 尝试发送关闭消息到进程 PID: {process.Id}");
                                if (process.CloseMainWindow()) // 尝试优雅关闭
                                {
                                    // 在后台线程等待进程退出，避免阻塞UI
                                    if (await Task.Run(() => process.WaitForExit(3000))) // 等待最多3秒
                                    {
                                        Debug.WriteLine($"[{Name}] 进程 PID: {process.Id} 已成功关闭。");
                                    }
                                    else
                                    {
                                        Debug.WriteLine($"[{Name}] 进程 PID: {process.Id} 未在3秒内响应关闭消息，强制终止...");
                                        process.Kill(); // 超时则强制终止
                                        await Task.Run(() => process.WaitForExit(1000)); // 等待终止完成
                                        Debug.WriteLine($"[{Name}] 进程 PID: {process.Id} 已强制终止。");
                                    }
                                }
                                else
                                {
                                    // 如果没有主窗口或发送关闭消息失败，直接强制终止
                                    Debug.WriteLine($"[{Name}] 无法发送关闭消息到进程 PID: {process.Id} (可能没有主窗口或已无响应)，强制终止...");
                                    process.Kill();
                                    await Task.Run(() => process.WaitForExit(1000));
                                    Debug.WriteLine($"[{Name}] 进程 PID: {process.Id} 已强制终止。");
                                }
                            }
                        }
                        catch (InvalidOperationException ex)
                        {
                            Debug.WriteLine($"[{Name}] 关闭进程 PID: {process.Id} 时出错 (进程可能已退出): {ex.Message}");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[{Name}] 关闭进程 PID: {process.Id} 时发生意外错误: {ex.Message}");
                        }
                    }
                }
                await Task.Delay(500); // 等待系统清理资源
            }

            // 发送媒体命令 (异步，默认使用 WM_APPCOMMAND)
            public virtual async Task SendCommandAsync(MediaCommand command)
            {
                IntPtr hwnd = WinAPI.FindMainWindow(ProcessName);
                if (hwnd == IntPtr.Zero)
                {
                    Debug.WriteLine($"[{Name} SendCommandAsync] 未找到目标窗口。");
                    return;
                }

                int? appCommand = command switch
                {
                    MediaCommand.PlayPause => WinAPI.APPCOMMAND_MEDIA_PLAY_PAUSE,
                    MediaCommand.NextTrack => WinAPI.APPCOMMAND_MEDIA_NEXTTRACK,
                    MediaCommand.PreviousTrack => WinAPI.APPCOMMAND_MEDIA_PREVIOUSTRACK,
                    MediaCommand.VolumeMute => WinAPI.APPCOMMAND_VOLUME_MUTE,
                    MediaCommand.VolumeDown => WinAPI.APPCOMMAND_VOLUME_DOWN,
                    MediaCommand.VolumeUp => WinAPI.APPCOMMAND_VOLUME_UP,
                    _ => null
                };

                if (appCommand.HasValue)
                {
                    Debug.WriteLine($"[{Name} SendCommandAsync] 发送 WM_APPCOMMAND ({command}) 到 HWND: {hwnd}");
                    // WM_APPCOMMAND 通常不需要激活窗口，直接发送
                    WinAPI.SendMessageW(hwnd, WinAPI.WM_APPCOMMAND, hwnd, (IntPtr)appCommand.Value);
                    // SendMessage 是同步的，但命令执行是异步的，稍作等待可能有助于状态刷新
                    await Task.Delay(50);
                }
                else
                {
                    Debug.WriteLine($"[{Name} SendCommandAsync] 收到无效的命令: {command}");
                }
            }

            // 获取当前歌曲 (同步方法，依赖 FindMainWindow)
            public virtual string GetCurrentSong()
            {
                IntPtr hwnd = WinAPI.FindMainWindow(ProcessName);
                if (hwnd != IntPtr.Zero)
                {
                    int length = WinAPI.GetWindowTextLength(hwnd);
                    if (length > 0)
                    {
                        StringBuilder sb = new StringBuilder(length + 1);
                        WinAPI.GetWindowText(hwnd, sb, sb.Capacity);
                        string title = sb.ToString();

                        // 尝试移除常见的应用名后缀
                        string[] suffixes = { $"- {Name}", "- 网易云音乐", "- QQ音乐", "- 酷狗音乐" }; // 可以配置或扩展
                        foreach (var suffix in suffixes)
                        {
                            if (title.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                            {
                                return title.Substring(0, title.Length - suffix.Length).Trim();
                            }
                        }
                        return title.Trim(); // 没有匹配到后缀，返回原始标题
                    }
                }
                return "无"; // 默认或未找到
            }
        }
}
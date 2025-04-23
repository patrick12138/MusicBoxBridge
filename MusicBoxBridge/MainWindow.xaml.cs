using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32; // 添加 Registry 操作命名空间 for path finding

namespace MusicBridge
{
    // Windows API和常量
    public static class WinAPI
    {
        // --- Windows 消息常量 ---
        public const int WM_APPCOMMAND = 0x0319;        // 用于向窗口发送媒体控制命令的Windows消息

        // --- APPCOMMAND 常量 (用于 WM_APPCOMMAND 消息) ---
        // 这些命令通常可以直接发送给支持媒体控制的应用程序窗口
        // 使用移位表示更符合官方文档 (command << 16)
        public const int APPCOMMAND_MEDIA_PLAY_PAUSE = 14 << 16; // 播放/暂停
        public const int APPCOMMAND_MEDIA_NEXTTRACK = 11 << 16;  // 下一曲
        public const int APPCOMMAND_MEDIA_PREVIOUSTRACK = 12 << 16; // 上一曲
        public const int APPCOMMAND_VOLUME_MUTE = 8 << 16;       // 静音切换
        public const int APPCOMMAND_VOLUME_DOWN = 9 << 16;       // 降低音量
        public const int APPCOMMAND_VOLUME_UP = 10 << 16;        // 提高音量

        // --- 虚拟键码常量 (用于 keybd_event 或其他键盘钩子) ---
        // 媒体控制虚拟键码
        public const byte VK_MEDIA_PLAY_PAUSE = 0xB3;
        public const byte VK_MEDIA_NEXT_TRACK = 0xB0;
        public const byte VK_MEDIA_PREV_TRACK = 0xB1;
        public const byte VK_VOLUME_MUTE = 0xAD;
        public const byte VK_VOLUME_DOWN = 0xAE;
        public const byte VK_VOLUME_UP = 0xAF;

        // 普通键盘按键的虚拟键码
        public const byte VK_SPACE = 0x20;
        public const byte VK_LEFT = 0x25;
        public const byte VK_RIGHT = 0x27;
        public const byte VK_UP = 0x26;
        public const byte VK_DOWN = 0x28;
        public const byte VK_CONTROL = 0x11;
        public const byte VK_MENU = 0x12;    // Alt键
        public const byte VK_SHIFT = 0x10;
        public const byte VK_LWIN = 0x5B;    // 左 Windows 键
        public const byte VK_RWIN = 0x5C;    // 右 Windows 键
        public const byte VK_P = 0x50;       // P 键

        // --- 键盘事件标志 (用于 keybd_event 函数) ---
        public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;  // 指示扩展键
        public const uint KEYEVENTF_KEYUP = 0x0002;        // 指示按键释放

        // --- Windows API 函数导入 ---
        // 建议为需要处理 Unicode 字符串的函数指定 CharSet
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessageW(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern int GetWindowTextLength(IntPtr hWnd); // 获取窗口标题长度，可以避免分配过大的 StringBuilder

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)] // 显式指定返回类型
        public static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        // SetLastError = true 允许之后调用 Marshal.GetLastWin32Error() 获取错误码
        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(IntPtr hWnd); // 检查是否最小化

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        public const int SW_RESTORE = 9;  // 还原窗口命令

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)] // 显式指定返回类型
        public static extern bool SetForegroundWindow(IntPtr hWnd); // 设置为前台窗口

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow(); // 获取当前前台窗口

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] // 获取窗口类名，用于更精确查找
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        // 回调委托定义
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        // 查找主窗口方法
        // 注意：这仍然是启发式方法。更可靠的方式是结合窗口类名（ClassName）。
        // ClassName 需要使用 Spy++ 等工具查找特定应用程序的主窗口类名。
        public static IntPtr FindMainWindow(string processName)
        {
            IntPtr foundHwnd = IntPtr.Zero;
            int maxTitleLen = 0; // 仍然可以作为备选依据
            List<IntPtr> potentialHwnds = new List<IntPtr>(); // 存储所有属于该进程的可见窗口

            EnumWindows((hWnd, lParam) =>
            {
                // 过滤不可见或没有标题的窗口，减少后续处理
                if (!IsWindowVisible(hWnd) || GetWindowTextLength(hWnd) == 0)
                    return true; // 继续枚举下一个窗口

                uint pid;
                GetWindowThreadProcessId(hWnd, out pid);
                if (pid == 0) return true; // 获取进程ID失败，继续

                try
                {
                    using (Process proc = Process.GetProcessById((int)pid)) // 使用 using 确保资源释放
                    {
                        if (proc.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase))
                        {
                            potentialHwnds.Add(hWnd); // 添加到候选列表

                            // 可以在这里添加基于窗口类名的检查，以提高优先级
                            // StringBuilder className = new StringBuilder(256);
                            // GetClassName(hWnd, className, 256);
                            // if (className.ToString() == "ExpectedClassName") { /* 标记为高优先级 */ }

                            // 保留基于标题长度的判断作为备选
                            int currentTitleLen = GetWindowTextLength(hWnd);
                            if (currentTitleLen > maxTitleLen)
                            {
                                maxTitleLen = currentTitleLen;
                                foundHwnd = hWnd; // 记录当前标题最长的窗口
                            }
                        }
                    } // Process 对象在此处被 Dispose
                }
                catch (ArgumentException) { /* 进程可能已经退出 */ }
                catch (InvalidOperationException) { /* 进程可能已经退出 */ }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[FindMainWindow] 枚举窗口时出错: {ex.Message}"); // 记录错误而不是忽略
                }
                return true; // 继续枚举
            }, IntPtr.Zero);

            // 决策逻辑：如果基于最长标题找到了窗口，优先返回它。
            // 否则，如果整个进程只有一个可见窗口，也返回它。
            if (foundHwnd != IntPtr.Zero)
            {
                return foundHwnd;
            }
            else if (potentialHwnds.Count == 1)
            {
                return potentialHwnds[0];
            }

            return IntPtr.Zero; // 未找到合适的窗口
        }

        // 确保所有修饰键都被释放 (异步版本)
        public static async Task ReleaseAllModifierKeysAsync()
        {
            // 尝试释放常用的修饰键
            for (int i = 0; i < 2; i++) // 通常尝试1-2次足够
            {
                keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                keybd_event(VK_SHIFT, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero); // 左 Win 键
                keybd_event(VK_RWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero); // 右 Win 键

                // 使用异步延迟，避免阻塞UI线程
                await Task.Delay(50); // 给操作系统一点时间处理按键事件
            }
            await Task.Delay(50); // 最后的额外等待
        }


        // --- 辅助方法：用于执行键盘模拟操作 ---

        // 模拟单个按键（按下和抬起）
        public static async Task SendKeyPressAsync(byte vkCode)
        {
            keybd_event(vkCode, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero); // 按下
            await Task.Delay(20); // 模拟短暂按住
            keybd_event(vkCode, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);      // 抬起
        }

        // 模拟组合键 (例如 Ctrl+Right)
        public static async Task SendCombinedKeyPressAsync(byte modifierVkCode, byte vkCode)
        {
            keybd_event(modifierVkCode, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero); // 按下修饰键
            await Task.Delay(20);
            keybd_event(vkCode, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);         // 按下普通键
            await Task.Delay(20);
            keybd_event(vkCode, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);               // 抬起普通键
            await Task.Delay(20);
            keybd_event(modifierVkCode, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);       // 抬起修饰键
        }

        // 尝试将窗口置于前台并执行操作 (异步)
        // 返回值: bool 表示操作是否成功尝试（不保证按键一定生效）
        public static async Task<bool> ExecuteKeystrokeCommandAsync(IntPtr targetHwnd, Func<Task> sendKeysAction)
        {
            if (targetHwnd == IntPtr.Zero || sendKeysAction == null) return false;

            IntPtr originalForegroundWindow = GetForegroundWindow(); // 保存原始前台窗口
            bool success = false;

            try
            {
                // 1. 检查窗口是否最小化，如果是则恢复
                if (IsIconic(targetHwnd))
                {
                    ShowWindow(targetHwnd, SW_RESTORE);
                    await Task.Delay(150); // 等待窗口动画
                }

                // 2. 尝试将目标窗口置于前台
                if (!SetForegroundWindow(targetHwnd))
                {
                    Debug.WriteLine($"[ExecuteKeystroke] SetForegroundWindow 失败，HWND: {targetHwnd}");
                    // 可能由于权限或其他原因失败，直接返回
                    return false;
                }

                // 3. 等待窗口真正变为前台 (设置超时)
                int waitMs = 0;
                const int timeout = 1500;
                while (GetForegroundWindow() != targetHwnd && waitMs < timeout)
                {
                    await Task.Delay(50);
                    waitMs += 50;
                }

                // 4. 检查是否成功激活
                if (GetForegroundWindow() == targetHwnd)
                {
                    await Task.Delay(100); // 短暂等待焦点稳定
                    // 5. 执行发送按键的操作
                    await sendKeysAction();
                    await Task.Delay(50); // 等待按键处理
                    success = true; // 标记操作已执行
                }
                else
                {
                    Debug.WriteLine($"[ExecuteKeystroke] 窗口 HWND: {targetHwnd} 未能在 {timeout}ms 内成为前台窗口。");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ExecuteKeystroke] 执行键盘模拟时出错: {ex.Message}");
                success = false;
            }
            finally
            {
                // 6. 确保释放所有可能残留的修饰键
                await ReleaseAllModifierKeysAsync();

                // 7. 尝试恢复原始窗口为前台（仅当原始窗口有效且不是目标窗口时）
                if (originalForegroundWindow != IntPtr.Zero && originalForegroundWindow != targetHwnd)
                {
                    SetForegroundWindow(originalForegroundWindow); // 尝试恢复，不保证成功
                }
            }
            return success;
        }

    }

    public partial class MainWindow : Window
    {
        // 媒体命令枚举
        public enum MediaCommand
        {
            PlayPause,
            NextTrack,
            PreviousTrack,
            VolumeMute,
            VolumeDown,
            VolumeUp
        }

        // 音乐软件控制器接口 (方法改为异步)
        public interface IMusicAppController
        {
            string Name { get; }          // 应用名称 (用于显示)
            string ProcessName { get; }   // 进程名称 (用于查找)
            string? ExecutablePath { get; } // 可执行文件路径 (可能为空)
            bool IsRunning();             // 检查应用是否正在运行
            Task LaunchAsync();           // 启动应用 (异步)
            Task CloseAppAsync();         // 关闭应用 (异步)
            Task SendCommandAsync(MediaCommand command); // 发送媒体命令 (异步)
            string GetCurrentSong();      // 获取当前播放歌曲名称 (保持同步，因其依赖WinAPI调用)
            Task<string?> FindExecutablePathAsync(); // 异步查找可执行文件路径
        }

        // 控制器基类 (用于共享查找路径、启动、关闭等逻辑)
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

        // --- QQ音乐控制器实现 ---
        public class QQMusicController : MusicAppControllerBase
        {
            public override string Name => "QQ音乐";
            public override string ProcessName => "QQMusic";
            protected override string DefaultExeName => "QQMusic.exe";

            // QQ音乐通常对 WM_APPCOMMAND 响应良好，直接使用基类实现即可。
            // 如果需要特殊处理路径查找（例如检查特定注册表项），可以重写 FindPathFromRegistry 或 CheckDefaultInstallLocations

            protected override string? CheckDefaultInstallLocations()
            {
                string path86 = @"C:\Program Files (x86)\Tencent\QQMusic\QQMusic.exe";
                string path64 = @"C:\Program Files\Tencent\QQMusic\QQMusic.exe"; // 可能的64位路径
                if (File.Exists(path86)) return path86;
                if (File.Exists(path64)) return path64;
                // 还可以检查 AppData 等路径
                return base.CheckDefaultInstallLocations();
            }
        }

        // --- 酷狗音乐控制器实现 ---
        public class KugouMusicController : MusicAppControllerBase
        {
            public override string Name => "酷狗音乐";
            public override string ProcessName => "KuGou"; // 主进程名通常是 KuGou
            protected override string DefaultExeName => "KuGou.exe"; // 注意：实际执行文件可能在子目录

            // 酷狗的 InstallLocation 可能指向父目录，需要特殊处理
            // 重写基类的查找方法来适应酷狗的特殊情况
            protected new string? FindPathFromRegistry() // 使用 new 关键字隐藏基类方法
            {
                string? basePath = base.FindPathFromRegistry(); // 先调用基类的查找逻辑
                if (basePath != null)
                {
                    // 如果找到的路径直接是exe文件，则返回
                    if (File.Exists(basePath) && basePath.EndsWith(DefaultExeName, StringComparison.OrdinalIgnoreCase))
                    {
                        return basePath;
                    }
                    // 如果找到的是目录 (InstallLocation)，尝试拼接子目录和文件名
                    if (Directory.Exists(basePath))
                    {
                        string potentialPath = Path.Combine(basePath, "KGMusic", DefaultExeName); // 常见子目录
                        if (File.Exists(potentialPath)) return potentialPath;
                        // 尝试不带子目录
                        potentialPath = Path.Combine(basePath, DefaultExeName);
                        if (File.Exists(potentialPath)) return potentialPath;
                    }
                }
                return null; // 如果基类找不到或者拼接后文件不存在，则返回 null
            }


            protected override string? CheckDefaultInstallLocations()
            {
                string path86 = @"C:\Program Files (x86)\KuGou\KGMusic\KuGou.exe";
                string path64 = @"C:\Program Files\KuGou\KGMusic\KuGou.exe";
                if (File.Exists(path86)) return path86;
                if (File.Exists(path64)) return path64;
                return base.CheckDefaultInstallLocations();
            }

            // 重写 SendCommandAsync 以处理键盘模拟 (如果 WM_APPCOMMAND 对播放控制无效)
            public override async Task SendCommandAsync(MediaCommand command)
            {
                IntPtr hwnd = WinAPI.FindMainWindow(ProcessName);
                if (hwnd == IntPtr.Zero)
                {
                    Debug.WriteLine($"[{Name} SendCommandAsync] 未找到目标窗口。");
                    return;
                }

                // 优先尝试 WM_APPCOMMAND (特别是音量控制，通常有效)
                if (command == MediaCommand.VolumeUp || command == MediaCommand.VolumeDown || command == MediaCommand.VolumeMute)
                {
                    Debug.WriteLine($"[{Name} SendCommandAsync] 尝试 WM_APPCOMMAND 处理音量命令: {command}");
                    await base.SendCommandAsync(command); // 调用基类方法
                    return;
                }

                // --- 对于播放控制 (Play/Pause, Next, Previous) ---
                // 1. **首先尝试 WM_APPCOMMAND** (调用基类实现)
                // Debug.WriteLine($"[{Name} SendCommandAsync] 尝试 WM_APPCOMMAND 处理播放命令: {command}");
                // await base.SendCommandAsync(command);

                // 2. **(可选) 如果 WM_APPCOMMAND 无效，再考虑使用键盘模拟作为后备**
                //    需要根据实际测试结果决定是否启用下面的代码。
                //    如果 WM_APPCOMMAND 有效，注释掉或删除下面的 else if 部分。

                // 假设测试后发现 WM_APPCOMMAND 对酷狗的播放控制无效
                // 可以添加一个标记或者检查之前的调用结果来判断是否需要键盘模拟
                bool wmAppCommandFailed = true; // 这里假设失败，实际应根据测试结果判断

                if (wmAppCommandFailed)
                {
                    Debug.WriteLine($"[{Name} SendCommandAsync] WM_APPCOMMAND 对 {command} 可能无效，尝试键盘模拟...");
                    Func<Task>? sendKeysAction = null;
                    switch (command)
                    {
                        case MediaCommand.PlayPause:
                            sendKeysAction = async () => await WinAPI.SendKeyPressAsync(WinAPI.VK_SPACE);
                            break;
                        case MediaCommand.NextTrack:
                            sendKeysAction = async () => await WinAPI.SendCombinedKeyPressAsync(WinAPI.VK_MENU, WinAPI.VK_RIGHT);
                            break;
                        case MediaCommand.PreviousTrack:
                            sendKeysAction = async () => await WinAPI.SendCombinedKeyPressAsync(WinAPI.VK_MENU, WinAPI.VK_LEFT);
                            break;
                    }

                    if (sendKeysAction != null)
                    {
                        await WinAPI.ExecuteKeystrokeCommandAsync(hwnd, sendKeysAction);
                    }
                }

            }
        }

        // --- 网易云音乐控制器实现 ---
        public class NeteaseMusicController : MusicAppControllerBase
        {
            public override string Name => "网易云音乐";
            public override string ProcessName => "cloudmusic";
            protected override string DefaultExeName => "cloudmusic.exe";

            protected override string? CheckDefaultInstallLocations()
            {
                // Program Files
                string path86 = @"C:\Program Files (x86)\Netease\CloudMusic\cloudmusic.exe";
                string path64 = @"C:\Program Files\Netease\CloudMusic\cloudmusic.exe";
                if (File.Exists(path86)) return path86;
                if (File.Exists(path64)) return path64;

                // AppData (新版可能安装在这里)
                try
                {
                    string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    string appDataPath = Path.Combine(localAppData, @"Netease\CloudMusic\cloudmusic.exe");
                    if (File.Exists(appDataPath)) return appDataPath;

                    string roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    string roamingPath = Path.Combine(roamingAppData, @"Netease\CloudMusic\cloudmusic.exe");
                    if (File.Exists(roamingPath)) return roamingPath;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[网易云] 检查 AppData 路径时出错: {ex.Message}");
                }

                return base.CheckDefaultInstallLocations();
            }

            // 重写 SendCommandAsync 以处理键盘模拟 (如果 WM_APPCOMMAND 对播放控制无效)
            public override async Task SendCommandAsync(MediaCommand command)
            {
                IntPtr hwnd = WinAPI.FindMainWindow(ProcessName);
                if (hwnd == IntPtr.Zero)
                {
                    Debug.WriteLine($"[{Name} SendCommandAsync] 未找到目标窗口。");
                    return;
                }

                // 优先尝试 WM_APPCOMMAND (特别是音量控制)
                if (command == MediaCommand.VolumeUp || command == MediaCommand.VolumeDown || command == MediaCommand.VolumeMute)
                {
                    Debug.WriteLine($"[{Name} SendCommandAsync] 尝试 WM_APPCOMMAND 处理音量命令: {command}");
                    await base.SendCommandAsync(command);
                    return;
                }

                // --- 对于播放控制 ---
                // 1. 首先尝试 WM_APPCOMMAND
                //Debug.WriteLine($"[{Name} SendCommandAsync] 尝试 WM_APPCOMMAND 处理播放命令: {command}");
                //await base.SendCommandAsync(command);

                // 2. (可选) 如果 WM_APPCOMMAND 无效，启用键盘模拟后备

                bool wmAppCommandFailed = true; // 假设失败

                if (wmAppCommandFailed)
                {
                    Debug.WriteLine($"[{Name} SendCommandAsync] WM_APPCOMMAND 对 {command} 可能无效，尝试键盘模拟...");
                    Func<Task>? sendKeysAction = null;
                    switch (command)
                    {
                        case MediaCommand.PlayPause:
                            // 网易云常用快捷键: Ctrl+P
                            sendKeysAction = async () => await WinAPI.SendCombinedKeyPressAsync(WinAPI.VK_CONTROL, WinAPI.VK_P);
                            break;
                        case MediaCommand.NextTrack:
                            // 网易云常用快捷键: Ctrl+Alt+Right (或 Ctrl+Right)
                            sendKeysAction = async () => await WinAPI.SendCombinedKeyPressAsync(WinAPI.VK_CONTROL, WinAPI.VK_RIGHT);
                            break;
                        case MediaCommand.PreviousTrack:
                            // 网易云常用快捷键: Ctrl+Alt+Left (或 Ctrl+Left)
                            sendKeysAction = async () => await WinAPI.SendCombinedKeyPressAsync(WinAPI.VK_CONTROL, WinAPI.VK_LEFT);
                            break;
                    }

                    if (sendKeysAction != null)
                    {
                        await WinAPI.ExecuteKeystrokeCommandAsync(hwnd, sendKeysAction);
                    }
                }

            }
        }

        // --- 主窗口逻辑 ---
        private IMusicAppController? currentController; // 当前选中的控制器 (可空)
        private readonly List<IMusicAppController> controllers = new List<IMusicAppController>(); // 控制器列表
        private readonly DispatcherTimer statusTimer = new DispatcherTimer(); // 状态刷新定时器

        public MainWindow()
        {
            InitializeComponent(); // 初始化 XAML 组件（必须在最前面）

            try
            {
                // 初始化控制器列表
                controllers.Add(new QQMusicController());
                controllers.Add(new NeteaseMusicController());
                controllers.Add(new KugouMusicController());

                // 填充下拉框
                foreach (var controller in controllers)
                {
                    MusicAppComboBox.Items.Add(new ComboBoxItem { Content = controller.Name });
                }

                // 默认选中第一个，并初始化 currentController
                if (MusicAppComboBox.Items.Count > 0)
                {
                    MusicAppComboBox.SelectedIndex = 0;
                    // SelectionChanged 事件会处理 currentController 的赋值
                }
                else
                {
                    UpdateStatus("错误：没有找到任何音乐播放器控制器。");
                    SetAllControlsEnabled(false); // 禁用所有控件
                }

                // 配置状态刷新定时器
                statusTimer.Interval = TimeSpan.FromSeconds(3); // 每 3 秒刷新一次
                statusTimer.Tick += StatusTimer_Tick;
                statusTimer.Start();

                // 绑定 Loaded 事件，在窗口加载完成后执行初始刷新
                Loaded += MainWindow_Loaded;

            }
            catch (Exception ex)
            {
                MessageBox.Show($"初始化应用时发生严重错误: {ex.Message}\n应用可能无法正常工作。", "初始化失败", MessageBoxButton.OK, MessageBoxImage.Error);
                SetAllControlsEnabled(false); // 出错时禁用控件
            }
        }

        // 窗口加载完成事件处理 (异步)
        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 异步尝试查找所有控制器的路径
            await FindAllControllerPathsAsync();
            // 异步刷新初始状态
            await RefreshMusicAppStatusAsync();
        }

        // 异步查找所有控制器路径 (可以在后台进行)
        private async Task FindAllControllerPathsAsync()
        {
            List<Task> findPathTasks = new List<Task>();
            foreach (var controller in controllers)
            {
                findPathTasks.Add(controller.FindExecutablePathAsync());
            }
            // 并行等待所有查找任务完成
            await Task.WhenAll(findPathTasks);
            Debug.WriteLine("所有控制器的路径查找已完成（或已尝试）。");
        }

        // 定时器事件处理 (异步)
        private async void StatusTimer_Tick(object? sender, EventArgs e)
        {
            // 异步刷新状态
            await RefreshMusicAppStatusAsync();
        }

        // 下拉框选择变化事件 (异步)
        private async void MusicAppComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MusicAppComboBox.SelectedItem is ComboBoxItem selectedItem && selectedItem.Content != null)
            {
                string appName = selectedItem.Content.ToString()!;
                var selectedController = controllers.FirstOrDefault(c => c.Name == appName);

                if (selectedController != null)
                {
                    currentController = selectedController;
                    UpdateStatus($"已切换到: {currentController.Name}");

                    // 确保路径已查找 (如果之前没找到，这里可以再试一次)
                    if (currentController.ExecutablePath == null)
                    {
                        await currentController.FindExecutablePathAsync();
                    }

                    // 异步刷新新选择的应用状态
                    await RefreshMusicAppStatusAsync();
                }
                else
                {
                    // 理论上不应该发生，因为下拉项来自 controllers 列表
                    UpdateStatus($"错误：未找到名为 {appName} 的控制器。");
                    currentController = null;
                    await RefreshMusicAppStatusAsync(); // 刷新以禁用控件
                }
            }
            else
            {
                // 没有选中任何项，或选中项无效
                currentController = null;
                UpdateStatus("请选择一个音乐播放器。");
                await RefreshMusicAppStatusAsync(); // 刷新以禁用控件
            }
        }

        // --- 按钮点击事件 (全部改为异步) ---
        private async void LaunchAppButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentController == null)
            {
                MessageBox.Show("请先在下拉列表中选择一个音乐播放器。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 防止重复点击
            LaunchAppButton.IsEnabled = false;
            UpdateStatus($"正在尝试启动 {currentController.Name}...");

            try
            {
                await currentController.LaunchAsync();
                await Task.Delay(2000); // 等待启动后刷新状态
                await RefreshMusicAppStatusAsync();
            }
            catch (Exception ex) // 捕获 LaunchAsync 可能抛出的其他异常
            {
                UpdateStatus($"启动 {currentController.Name} 时发生意外错误: {ex.Message}");
                MessageBox.Show($"启动 {currentController.Name} 时发生意外错误: {ex.Message}", "启动错误", MessageBoxButton.OK, MessageBoxImage.Error);
                // 即使启动失败，也需要刷新状态来决定按钮是否应该重新启用
                await RefreshMusicAppStatusAsync();
            }
            finally
            {
                // LaunchAppButton 的状态由 RefreshMusicAppStatusAsync 中的 SetAllControlsEnabled 控制
            }
        }

        // 封装发送命令和刷新的逻辑
        private async Task SendCommandAndRefreshAsync(MediaCommand command)
        {
            if (currentController == null) return; // 没有选定控制器

            if (!currentController.IsRunning())
            {
                UpdateStatus($"{currentController.Name} 未运行，无法发送命令 {command}。");
                // 可以考虑提示用户是否需要启动
                // var result = MessageBox.Show($"{currentController.Name} 未运行，是否立即启动？", "提示", MessageBoxButton.YesNo, MessageBoxImage.Question);
                // if (result == MessageBoxResult.Yes) await LaunchAppButton_Click(null, null); // 调用启动逻辑
                return;
            }

            try
            {
                // 可以在发送前禁用按钮，防止快速点击，发送后再启用
                // SetMediaControlsEnabled(false);

                await currentController.SendCommandAsync(command);
                // 命令发送后，稍作等待让应用响应，然后刷新状态以获取最新歌曲信息
                await Task.Delay(250); // 延迟时间可调整
                await RefreshMusicAppStatusAsync();
            }
            catch (Exception ex)
            {
                UpdateStatus($"发送命令 {command} 到 {currentController.Name} 时出错: {ex.Message}");
                Debug.WriteLine($"[SendCommandAndRefreshAsync] Error: {ex.ToString()}");
                // 即使出错，也刷新状态
                await RefreshMusicAppStatusAsync();
            }
            finally
            {
                // SetMediaControlsEnabled(currentController?.IsRunning() ?? false);
            }
        }

        // 各个媒体控制按钮的事件处理
        private async void PlayPauseButton_Click(object sender, RoutedEventArgs e) => await SendCommandAndRefreshAsync(MediaCommand.PlayPause);
        private async void NextButton_Click(object sender, RoutedEventArgs e) => await SendCommandAndRefreshAsync(MediaCommand.NextTrack);
        private async void PreviousButton_Click(object sender, RoutedEventArgs e) => await SendCommandAndRefreshAsync(MediaCommand.PreviousTrack);
        private async void VolumeUpButton_Click(object sender, RoutedEventArgs e) => await SendCommandAndRefreshAsync(MediaCommand.VolumeUp);
        private async void VolumeDownButton_Click(object sender, RoutedEventArgs e) => await SendCommandAndRefreshAsync(MediaCommand.VolumeDown);
        private async void MuteButton_Click(object sender, RoutedEventArgs e) => await SendCommandAndRefreshAsync(MediaCommand.VolumeMute);

        private async void CloseAppButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentController == null) return;

            if (!currentController.IsRunning())
            {
                UpdateStatus($"{currentController.Name} 当前未运行。");
                return;
            }

            // 禁用按钮，防止重复点击
            CloseAppButton.IsEnabled = false;
            UpdateStatus($"正在尝试关闭 {currentController.Name}...");

            try
            {
                await currentController.CloseAppAsync();
                // 关闭后等待一段时间确保进程退出或状态更新
                await Task.Delay(1000);
                await RefreshMusicAppStatusAsync(); // 刷新状态
            }
            catch (Exception ex) // 捕获 CloseAppAsync 可能抛出的其他异常
            {
                UpdateStatus($"关闭 {currentController.Name} 时发生意外错误: {ex.Message}");
                MessageBox.Show($"关闭 {currentController.Name} 时发生意外错误: {ex.Message}", "关闭错误", MessageBoxButton.OK, MessageBoxImage.Error);
                await RefreshMusicAppStatusAsync(); // 即使失败也要刷新状态
            }
            finally
            {
                // CloseAppButton 的状态由 RefreshMusicAppStatusAsync 中的 SetAllControlsEnabled 控制
            }
        }

        // 刷新音乐应用状态 (异步)
        private async Task RefreshMusicAppStatusAsync()
        {
            if (currentController == null)
            {
                UpdateStatus("请选择播放器");
                // 确保在UI线程更新UI元素
                await Dispatcher.InvokeAsync(() => {
                    if (CurrentSongTextBlock != null) CurrentSongTextBlock.Text = "N/A";
                    SetAllControlsEnabled(false); // 没有控制器，禁用所有按钮
                    if (MusicAppComboBox != null) MusicAppComboBox.IsEnabled = true; // 下拉框始终可用
                });
                return;
            }

            bool isRunning = false;
            string song = "无";
            string status = $"[{currentController.Name}] ";

            try
            {
                // IsRunning() 通常很快，可以在UI线程执行，如果它变慢则考虑 Task.Run
                isRunning = currentController.IsRunning();

                if (isRunning)
                {
                    status += "运行中";
                    // GetCurrentSong 可能涉及窗口查找，如果卡顿可以移到 Task.Run
                    song = currentController.GetCurrentSong();
                }
                else
                {
                    status += "未运行";
                    song = "N/A"; // 未运行时显示 N/A
                }

                UpdateStatus(status);

                // 更新歌曲文本和控件状态 (必须在UI线程)
                await Dispatcher.InvokeAsync(() => {
                    if (CurrentSongTextBlock != null)
                    {
                        CurrentSongTextBlock.Text = song; // 直接显示歌曲或 N/A
                    }
                    SetAllControlsEnabled(isRunning); // 根据运行状态设置按钮可用性
                });
            }
            catch (Exception ex)
            {
                UpdateStatus($"刷新 {currentController.Name} 状态时出错: {ex.Message}");
                Debug.WriteLine($"[RefreshMusicAppStatusAsync] Error: {ex.ToString()}");
                // 出错时，保守地禁用控件
                await Dispatcher.InvokeAsync(() => SetAllControlsEnabled(false));
            }
        }

        // 更新状态栏文本 (确保在UI线程执行)
        private void UpdateStatus(string status)
        {
            // 检查是否在UI线程
            if (!Dispatcher.CheckAccess())
            {
                // 异步切换到UI线程执行更新
                Dispatcher.InvokeAsync(() => UpdateStatus(status));
                return;
            }

            // 已在UI线程，直接更新
            if (CurrentStatusTextBlock != null)
            {
                CurrentStatusTextBlock.Text = status;
            }
            Debug.WriteLine($"[状态更新] {status}"); // 保留日志
        }

        // 统一设置所有相关控件的启用/禁用状态 (确保在UI线程执行)
        private void SetAllControlsEnabled(bool isRunning)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.InvokeAsync(() => SetAllControlsEnabled(isRunning));
                return;
            }

            // 启动按钮：仅在应用未运行时启用
            LaunchAppButton.IsEnabled = !isRunning && (currentController?.ExecutablePath != null); // 只有找到路径才能启动

            // 媒体控制按钮和关闭按钮：仅在应用运行时启用
            PlayPauseButton.IsEnabled = isRunning;
            NextButton.IsEnabled = isRunning;
            PreviousButton.IsEnabled = isRunning;
            VolumeUpButton.IsEnabled = isRunning;
            VolumeDownButton.IsEnabled = isRunning;
            MuteButton.IsEnabled = isRunning;
            CloseAppButton.IsEnabled = isRunning;

            // 下拉框始终保持启用，以便用户切换
            MusicAppComboBox.IsEnabled = true;
        }
    }
}
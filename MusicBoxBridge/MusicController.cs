using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace MusicBridge
{
            // --- QQ音乐控制器实现 ---
        public class QQMusicController : MusicAppControllerBase
        {
            public override string Name => "QQ音乐";
            public override string ProcessName => "QQMusic";
            protected override string DefaultExeName => "QQMusic.exe";

            // QQ音乐通常对 WM_APPCOMMAND 响应良好，直接使用基类实现即可。
            // 如果需要特殊处理路径查找（例如检查特定注册表项），可以重写 FindPathFromRegistry 或 CheckDefaultInstallLocations
        }

        // --- 酷狗音乐控制器实现 ---
        public class KugouMusicController : MusicAppControllerBase
        {
            public override string Name => "酷狗音乐";
            public override string ProcessName => "KuGou"; // 主进程名通常是 KuGou
            protected override string DefaultExeName => "KuGou.exe"; // 注意：实际执行文件可能在子目录

            // 酷狗的 InstallLocation 可能指向父目录，需要特殊处理
            // 重写基类的查找方法来适应酷狗的特殊情况
            // protected new string? FindPathFromRegistry() // 使用 new 关键字隐藏基类方法
            // {
            //     string? basePath = base.FindPathFromRegistry(); // 先调用基类的查找逻辑
            //     if (basePath != null)
            //     {
            //         // 如果找到的路径直接是exe文件，则返回
            //         if (File.Exists(basePath) && basePath.EndsWith(DefaultExeName, StringComparison.OrdinalIgnoreCase))
            //         {
            //             return basePath;
            //         }
            //         // 如果找到的是目录 (InstallLocation)，尝试拼接子目录和文件名
            //         if (Directory.Exists(basePath))
            //         {
            //             string potentialPath = Path.Combine(basePath, "KGMusic", DefaultExeName); // 常见子目录
            //             if (File.Exists(potentialPath)) return potentialPath;
            //             // 尝试不带子目录
            //             potentialPath = Path.Combine(basePath, DefaultExeName);
            //             if (File.Exists(potentialPath)) return potentialPath;
            //         }
            //     }
            //     return null; // 如果基类找不到或者拼接后文件不存在，则返回 null
            // }

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

}
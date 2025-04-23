using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MusicBridge
{
    /// <summary>
    /// 包含 Windows API P/Invoke 定义和相关辅助方法的静态类。
    /// </summary>
    public static class WinAPI
    {
        // --- Windows 消息常量 ---
        public const int WM_APPCOMMAND = 0x0319;        // 用于向窗口发送媒体控制命令的Windows消息

        // --- APPCOMMAND 常量 (用于 WM_APPCOMMAND 消息) ---
        // 这些命令通常可以直接发送给支持媒体控制的应用程序窗口
        // 注意：原始代码中的位移操作是正确的，这里保持原样
        public const int APPCOMMAND_MEDIA_PLAY_PAUSE = 14 << 16; // 播放/暂停
        public const int APPCOMMAND_MEDIA_NEXTTRACK = 11 << 16;  // 下一曲
        public const int APPCOMMAND_MEDIA_PREVIOUSTRACK = 12 << 16; // 上一曲
        public const int APPCOMMAND_VOLUME_MUTE = 8 << 16;       // 静音切换
        public const int APPCOMMAND_VOLUME_DOWN = 9 << 16;       // 降低音量
        public const int APPCOMMAND_VOLUME_UP = 10 << 16;        // 提高音量

        // --- 虚拟键码常量 (用于 keybd_event 或其他键盘钩子) ---
        // 媒体控制虚拟键码 (这些通常由多媒体键盘直接发送，模拟它们可能不总是有用)
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
        public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;  // 指示扩展键 (例如箭头键、功能键等)
        public const uint KEYEVENTF_KEYUP = 0x0002;        // 指示按键释放

        // --- Windows API 函数导入 ---
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessageW(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

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
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd); // 设置为前台窗口

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow(); // 获取当前前台窗口

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        // 回调委托定义
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        /// <summary>
        /// 查找与指定进程名关联的主窗口句柄。
        /// 这是一个启发式方法，优先选择标题最长且可见的窗口。
        /// </summary>
        /// <param name="processName">目标进程的名称 (不含 .exe)。</param>
        /// <returns>找到的主窗口句柄，如果未找到则返回 IntPtr.Zero。</returns>
        public static IntPtr FindMainWindow(string processName)
        {
            IntPtr foundHwnd = IntPtr.Zero;
            uint targetPid = 0;

            // 获取目标进程ID (可能存在多个同名进程，这里取第一个)
            Process[] processes = Process.GetProcessesByName(processName);
            if (processes.Length > 0)
            {
                targetPid = (uint)processes[0].Id;
            }
            else
            {
                return IntPtr.Zero; // 进程未运行
            }

            int maxTitleLen = 0; // 用于比较标题长度

            EnumWindows((hWnd, lParam) =>
            {
                // 检查窗口是否属于目标进程
                GetWindowThreadProcessId(hWnd, out uint windowPid);
                if (windowPid != targetPid)
                {
                    return true; // 不是目标进程的窗口，继续枚举
                }

                // 检查窗口是否可见且有标题
                if (!IsWindowVisible(hWnd))
                {
                    return true; // 不可见，继续
                }

                int titleLen = GetWindowTextLength(hWnd);
                if (titleLen == 0)
                {
                    return true; // 没有标题，继续
                }

                // 启发式：选择标题最长的窗口作为主窗口
                if (titleLen > maxTitleLen)
                {
                    maxTitleLen = titleLen;
                    foundHwnd = hWnd;
                }

                return true; // 继续枚举以找到最合适的
            }, IntPtr.Zero);

            return foundHwnd;
        }

        /// <summary>
        /// 异步释放常用的修饰键 (Ctrl, Alt, Shift, Win)。
        /// </summary>
        public static async Task ReleaseAllModifierKeysAsync()
        {
            byte[] modifierKeys = { VK_CONTROL, VK_MENU, VK_SHIFT, VK_LWIN, VK_RWIN };
            foreach (byte key in modifierKeys)
            {
                // 发送抬起事件，以防万一按键被卡住
                keybd_event(key, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            }
            await Task.Delay(30); // 短暂等待
        }

        /// <summary>
        /// 异步模拟单个按键的按下和抬起。
        /// </summary>
        /// <param name="vkCode">要模拟的虚拟键码。</param>
        public static async Task SendKeyPressAsync(byte vkCode)
        {
            keybd_event(vkCode, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero); // 按下
            await Task.Delay(30); // 模拟短暂按住
            keybd_event(vkCode, 0, KEYEVENTF_KEYUP | KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero); // 抬起
        }

        /// <summary>
        /// 异步模拟组合键 (例如 Ctrl+Right)。
        /// </summary>
        /// <param name="modifierVkCode">修饰键的虚拟键码 (例如 VK_CONTROL)。</param>
        /// <param name="vkCode">普通键的虚拟键码 (例如 VK_RIGHT)。</param>
        public static async Task SendCombinedKeyPressAsync(byte modifierVkCode, byte vkCode)
        {
            keybd_event(modifierVkCode, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero); // 按下修饰键
            await Task.Delay(30);
            keybd_event(vkCode, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);         // 按下普通键
            await Task.Delay(30);
            keybd_event(vkCode, 0, KEYEVENTF_KEYUP | KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero); // 抬起普通键
            await Task.Delay(30);
            keybd_event(modifierVkCode, 0, KEYEVENTF_KEYUP | KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero); // 抬起修饰键
        }

        /// <summary>
        /// 尝试将目标窗口置于前台，然后异步执行键盘模拟操作。
        /// 会尝试恢复原始的前台窗口。
        /// </summary>
        /// <param name="targetHwnd">目标窗口句柄。</param>
        /// <param name="sendKeysAction">包含发送按键逻辑的异步委托。</param>
        /// <returns>如果成功激活窗口并尝试执行了操作，则返回 true；否则返回 false。</returns>
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
                    await Task.Delay(200); // 等待窗口动画
                }

                // 2. 尝试将目标窗口置于前台
                if (!SetForegroundWindow(targetHwnd))
                {
                    Debug.WriteLine($"[ExecuteKeystroke] SetForegroundWindow 失败，HWND: {targetHwnd}");
                    // 即使失败，有时键盘事件也能送达，继续尝试发送
                    // return false; // 如果严格要求必须激活，则取消注释此行
                }

                // 3. 等待窗口变为前台 (或至少给系统一点时间响应)
                await Task.Delay(150); // 增加延迟以提高成功率

                // 4. 检查是否成功激活 (可选，因为 SetForegroundWindow 可能异步完成)
                // if (GetForegroundWindow() == targetHwnd)
                // {
                    // await Task.Delay(100); // 短暂等待焦点稳定
                    // 5. 执行发送按键的操作
                    await sendKeysAction();
                    await Task.Delay(50); // 等待按键处理
                    success = true; // 标记操作已尝试执行
                // }
                // else
                // {
                //     Debug.WriteLine($"[ExecuteKeystroke] 窗口 HWND: {targetHwnd} 未能立即成为前台窗口。");
                //     // 即使未成为前台，也尝试发送按键，有时仍然有效
                //     await sendKeysAction();
                //     await Task.Delay(50);
                //     success = true; // 标记操作已尝试执行
                // }
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
                if (originalForegroundWindow != IntPtr.Zero && originalForegroundWindow != targetHwnd && GetForegroundWindow() != originalForegroundWindow)
                {
                    SetForegroundWindow(originalForegroundWindow); // 尝试恢复，不保证成功
                }
            }
            return success;
        }
    }
}
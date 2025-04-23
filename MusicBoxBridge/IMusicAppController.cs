namespace MusicBridge
{
    /// <summary>
    /// 定义媒体控制命令的枚举。
    /// </summary>
    public enum MediaCommand
    {
        PlayPause,
        NextTrack,
        PreviousTrack,
        VolumeMute,
        VolumeDown,
        VolumeUp
    }

    /// <summary>
    /// 定义音乐应用程序控制器的接口。
    /// </summary>
    public interface IMusicAppController
    {
        /// <summary>
        /// 获取应用名称 (用于显示)。
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 获取应用进程名称 (用于查找窗口和进程)。
        /// </summary>
        string ProcessName { get; }

        /// <summary>
        /// 获取应用的可执行文件路径 (如果找到)。
        /// </summary>
        string? ExecutablePath { get; }

        /// <summary>
        /// 检查应用当前是否正在运行。
        /// </summary>
        /// <returns>如果应用正在运行，则为 true；否则为 false。</returns>
        bool IsRunning();

        /// <summary>
        /// 异步启动应用程序。
        /// </summary>
        Task LaunchAsync();

        /// <summary>
        /// 异步关闭应用程序。
        /// </summary>
        Task CloseAppAsync();

        /// <summary>
        /// 异步向应用程序发送媒体控制命令。
        /// </summary>
        /// <param name="command">要发送的媒体命令。</param>
        Task SendCommandAsync(MediaCommand command);

        /// <summary>
        /// 获取当前正在播放的歌曲名称。
        /// (注意：此方法保持同步，因为它通常依赖于同步的 WinAPI 调用来获取窗口标题)。
        /// </summary>
        /// <returns>当前歌曲名称，如果无法获取则返回默认字符串 (例如 "无")。</returns>
        string GetCurrentSong();

        /// <summary>
        /// 异步查找应用程序的可执行文件路径。
        /// </summary>
        /// <returns>找到的路径，如果未找到则返回 null。</returns>
        Task<string?> FindExecutablePathAsync();
    }
}
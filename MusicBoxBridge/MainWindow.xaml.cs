using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace MusicBridge
{
    public partial class MainWindow : Window
    {
        #region // --- 主窗口逻辑 ---
        private IMusicAppController? currentController; // 当前选中的控制器 (可空)
        private readonly List<IMusicAppController> controllers = new List<IMusicAppController>(); // 控制器列表
        private readonly DispatcherTimer statusTimer = new DispatcherTimer(); // 状态刷新定时器

        // 当前活跃应用标记
        private string? currentActiveAppTag = null;

        private ContextMenu? appContextMenu;
        private MenuItem? closeAppMenuItem;

        public MainWindow()
        {
            InitializeComponent(); // 初始化 XAML 组件（必须在最前面）

            try
            {
                // 初始化控制器列表
                controllers.Add(new QQMusicController());
                controllers.Add(new NeteaseMusicController());
                controllers.Add(new KugouMusicController());

                // 获取上下文菜单引用
                Grid appSelectionGrid = (Grid)this.FindName("AppSelectionGrid");
                if (appSelectionGrid != null)
                {
                    appContextMenu = appSelectionGrid.ContextMenu;
                    if (appContextMenu != null)
                    {
                        foreach (var item in appContextMenu.Items)
                        {
                            if (item is MenuItem menuItem && menuItem.Name == "CloseAppMenuItem")
                            {
                                closeAppMenuItem = menuItem;
                                break;
                            }
                        }
                    }
                }

                // 配置状态刷新定时器
                statusTimer.Interval = TimeSpan.FromSeconds(3); // 每 3 秒刷新一次
                statusTimer.Tick += StatusTimer_Tick;
                statusTimer.Start();

                // 绑定 Loaded 事件，在窗口加载完成后执行初始刷新
                Loaded += MainWindow_Loaded;

                // 初始化右键菜单
                InitializeContextMenu();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"初始化应用时发生严重错误: {ex.Message}\n应用可能无法正常工作。", "初始化失败", MessageBoxButton.OK, MessageBoxImage.Error);
                SetMediaControlsEnabled(false); // 出错时禁用控件
            }
        }

        // 初始化右键菜单
        private void InitializeContextMenu()
        {
            if (appContextMenu != null)
            {
                appContextMenu.Closed += (s, e) =>
                {
                    // 右键菜单关闭后，清除临时属性
                    appContextMenu.Tag = null;
                };
            }
        }

        // 窗口加载完成事件处理
        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 异步尝试查找所有控制器的路径
            await FindAllControllerPathsAsync();
            // 异步刷新初始状态
            await RefreshMusicAppStatusAsync();
        }
        
        // 应用图标点击事件处理
        private async void AppIconButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string tag)
            {
                // 从 Tag 中恢复原始的应用标识 (例如 "QQMusic", "NetEase", "Kugou")
                // 因为 Tag 可能被设置为 "Active"
                string appTag = GetOriginalTag(button);
                if (appTag == null) return; // 如果无法获取原始Tag，则退出

                var selectedController = GetControllerByTag(appTag);

                if (selectedController != null)
                {
                    // 无论是否运行，都将点击的应用设为当前控制目标
                    currentController = selectedController;
                    currentActiveAppTag = appTag; // 更新当前活跃（被控制）的应用Tag
                    UpdateStatus($"已选择控制: {currentController.Name}");

                    // 如果应用未运行，则启动它
                    if (!currentController.IsRunning())
                    {
                        UpdateStatus($"正在尝试启动 {currentController.Name}...");
                        await currentController.LaunchAsync();
                        await Task.Delay(1000); // 等待启动
                    }

                    // 更新所有图标的视觉状态
                    UpdateAppIconsStatus();
                    // 刷新状态和媒体控件
                    await RefreshMusicAppStatusAsync();
                }
            }
        }

        // 应用图标右键点击事件
        private void AppIconButton_RightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is Button button && appContextMenu != null)
            {
                string appTag = GetOriginalTag(button); // 获取原始Tag
                if (appTag == null) return;

                // 设置右键菜单的 Tag 属性，以便在右键菜单项点击时知道是哪个应用
                appContextMenu.Tag = appTag;

                // 获取对应的控制器
                var controller = GetControllerByTag(appTag);

                // 根据应用是否运行，设置关闭菜单项的可用状态
                if (controller != null && closeAppMenuItem != null)
                {
                    closeAppMenuItem.IsEnabled = controller.IsRunning();
                    closeAppMenuItem.Header = $"关闭 {controller.Name}";
                }
                else if (closeAppMenuItem != null)
                {
                    closeAppMenuItem.IsEnabled = false;
                    closeAppMenuItem.Header = "关闭应用";
                }

                // 显示右键菜单
                appContextMenu.IsOpen = true;

                // 标记事件已处理
                e.Handled = true;
            }
        }

        // 右键菜单关闭应用点击事件
        private async void CloseAppMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (appContextMenu?.Tag is string appTag)
            {
                var controller = GetControllerByTag(appTag);
                if (controller != null && controller.IsRunning())
                {
                    UpdateStatus($"正在尝试关闭 {controller.Name}...");
                    await controller.CloseAppAsync();
                    await Task.Delay(500); // 等待关闭

                    // 如果关闭的是当前控制的应用，则清除控制焦点
                    if (appTag == currentActiveAppTag)
                    {
                        currentController = null;
                        currentActiveAppTag = null;
                    }

                    // 刷新状态
                    UpdateAppIconsStatus();
                    await RefreshMusicAppStatusAsync();
                }
            }
        }

        // 更新应用图标状态 
        private void UpdateAppIconsStatus()
        {
            if (AppIconsPanel == null) return; // 添加空检查

            // 遍历所有应用图标，更新其状态
            foreach (UIElement element in AppIconsPanel.Children)
            {
                if (element is Button button)
                {
                    string originalTag = GetOriginalTag(button); // 获取原始Tag
                    if (originalTag == null) continue;

                    // 如果是当前活跃（被控制）的应用，设置为活跃状态
                    if (originalTag == currentActiveAppTag)
                    {
                        button.Tag = "Active";
                    }
                    // 否则，恢复其原始 Tag (例如 "QQMusic")
                    else
                    {
                        // 只有当 Tag 不是原始 Tag 时才更新，避免不必要的更改
                        if (!(button.Tag is string currentTag && currentTag == originalTag))
                        {
                            button.Tag = originalTag;
                        }
                    }
                }
            }
        }

        // 刷新音乐应用状态 (异步) - 修改为仅关注当前控制器
        private async Task RefreshMusicAppStatusAsync()
        {
            // 先更新所有图标的视觉状态，确保运行状态正确反映（虽然目前只高亮当前控制的）
            UpdateAppIconsStatus(); // 移动到开头或结尾皆可，这里放开头

            if (currentController == null)
            {
                UpdateStatus("请选择要控制的播放器");
                await Dispatcher.InvokeAsync(() =>
                {
                    if (CurrentSongTextBlock != null) CurrentSongTextBlock.Text = "无";
                    SetMediaControlsEnabled(false); // 没有控制器，禁用媒体按钮
                });
                return;
            }

            bool isRunning = false;
            string song = "无";
            string status = $"[{currentController.Name}] ";

            try
            {
                // 检查当前控制器的运行状态
                isRunning = currentController.IsRunning();

                if (isRunning)
                {
                    status += "运行中 (已控制)";
                    // 获取当前歌曲信息
                    song = currentController.GetCurrentSong();
                }
                else
                {
                    status += "未运行";
                    song = "N/A"; // 未运行时显示 N/A
                    // 如果当前控制的应用不再运行，清除控制焦点
                    if (GetOriginalTagFromController(currentController) == currentActiveAppTag)
                    {
                        currentActiveAppTag = null;
                        currentController = null; // 清除控制器引用
                        UpdateAppIconsStatus(); // 更新图标状态
                    }
                }

                UpdateStatus(status);

                // 更新歌曲文本和控件状态 (必须在UI线程)
                await Dispatcher.InvokeAsync(() =>
                {
                    if (CurrentSongTextBlock != null)
                    {
                        CurrentSongTextBlock.Text = song;
                    }
                    // 根据当前控制的应用是否运行来设置媒体按钮
                    SetMediaControlsEnabled(isRunning && currentController != null);
                });
            }
            catch (Exception ex)
            {
                UpdateStatus($"刷新 {currentController?.Name ?? "未知应用"} 状态时出错: {ex.Message}");
                Debug.WriteLine($"[RefreshMusicAppStatusAsync] Error: {ex.ToString()}");
                // 出错时，保守地禁用控件
                await Dispatcher.InvokeAsync(() => SetMediaControlsEnabled(false));
            }
        }
        #endregion

        #region // --- 辅助方法 ---

        // 新增：根据按钮获取其原始 Tag (QQMusic, NetEase, Kugou)
        private string? GetOriginalTag(Button button)
        {
            if (button.Name == "QQMusicButton") return "QQMusic";
            if (button.Name == "NetEaseButton") return "NetEase";
            if (button.Name == "KugouButton") return "Kugou";
            // 如果 Tag 本身就是原始 Tag (不是 "Active")
            if (button.Tag is string tag && (tag == "QQMusic" || tag == "NetEase" || tag == "Kugou")) return tag;
            return null; // 无法确定原始 Tag
        }

        // 新增：根据控制器获取其对应的原始 Tag
        private string? GetOriginalTagFromController(IMusicAppController controller)
        {
            if (controller is QQMusicController) return "QQMusic";
            if (controller is NeteaseMusicController) return "NetEase";
            if (controller is KugouMusicController) return "Kugou";
            return null;
        }

        // 新增：在可视化树中查找父元素
        public static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parentObject = VisualTreeHelper.GetParent(child);
            if (parentObject == null) return null;
            T? parent = parentObject as T;
            if (parent != null)
            {
                return parent;
            }
            else
            {
                return FindVisualParent<T>(parentObject);
            }
        }

        // 根据 Tag 获取控制器
        private IMusicAppController? GetControllerByTag(string tag)
        {
            switch (tag)
            {
                case "QQMusic":
                    return controllers.FirstOrDefault(c => c is QQMusicController);
                case "NetEase":
                    return controllers.FirstOrDefault(c => c is NeteaseMusicController);
                case "Kugou":
                    return controllers.FirstOrDefault(c => c is KugouMusicController);
                default:
                    return null;
            }
        }

        // 关闭当前应用
        private async Task CloseCurrentAppAsync()
        {
            if (currentController == null) return;

            if (!currentController.IsRunning())
            {
                UpdateStatus($"{currentController.Name} 当前未运行。");
                return;
            }

            UpdateStatus($"正在尝试关闭 {currentController.Name}...");

            try
            {
                await currentController.CloseAppAsync();
                // 关闭后等待一段时间确保进程退出或状态更新
                await Task.Delay(1000);

                // 清除当前活跃应用标记
                if (currentActiveAppTag != null)
                {
                    string closedAppTag = currentActiveAppTag;
                    currentActiveAppTag = null;
                    UpdateAppIconsStatus();
                }

                await RefreshMusicAppStatusAsync(); // 刷新状态
            }
            catch (Exception ex)
            {
                UpdateStatus($"关闭 {currentController.Name} 时发生意外错误: {ex.Message}");
                MessageBox.Show($"关闭 {currentController.Name} 时发生意外错误: {ex.Message}", "关闭错误", MessageBoxButton.OK, MessageBoxImage.Error);
                await RefreshMusicAppStatusAsync(); // 即使失败也要刷新状态
            }
        }

        // 关闭应用按钮点击事件处理
        private async void CloseAppButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string tag)
            {
                var controllerToClose = GetControllerByTag(tag);
                if (controllerToClose != null)
                {
                    if (controllerToClose.IsRunning())
                    {
                        UpdateStatus($"正在尝试关闭 {controllerToClose.Name}...");
                        await controllerToClose.CloseAppAsync();
                        await Task.Delay(500); // 等待关闭

                        // 如果关闭的是当前控制的应用，则清除控制焦点
                        if (tag == currentActiveAppTag)
                        {
                            currentController = null;
                            currentActiveAppTag = null;
                        }

                        // 刷新状态
                        UpdateAppIconsStatus();
                        await RefreshMusicAppStatusAsync();
                    }
                    else
                    {
                        UpdateStatus($"{controllerToClose.Name} 当前未运行。");
                    }
                }
            }
        }

        // 关闭当前应用按钮点击事件处理
        private async void CloseCurrentAppButton_Click(object sender, RoutedEventArgs e)
        {
            // 检查是否存在当前活跃的应用
            if (currentController == null)
            {
                UpdateStatus("没有选择活跃的应用可关闭");
                return;
            }

            // 检查当前应用是否在运行
            if (!currentController.IsRunning())
            {
                UpdateStatus($"{currentController.Name} 当前未运行。");
                return;
            }

            // 关闭当前活跃的应用
            await CloseCurrentAppAsync();
        }

        // 仅设置媒体控制按钮的可用状态
        private void SetMediaControlsEnabled(bool isEnabled)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.InvokeAsync(() => SetMediaControlsEnabled(isEnabled));
                return;
            }

            PlayPauseButton.IsEnabled = isEnabled;
            NextButton.IsEnabled = isEnabled;
            PreviousButton.IsEnabled = isEnabled;
            VolumeUpButton.IsEnabled = isEnabled;
            VolumeDownButton.IsEnabled = isEnabled;
            MuteButton.IsEnabled = isEnabled;
        }

        // 统一设置所有相关控件的启用/禁用状态 (确保在UI线程执行) - 这个方法将不再使用，保留以供参考
        private void SetAllControlsEnabled(bool isRunning)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.InvokeAsync(() => SetAllControlsEnabled(isRunning));
                return;
            }

            // 媒体控制按钮：仅在应用运行时启用
            SetMediaControlsEnabled(isRunning);
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

        #endregion

        // 各个媒体控制按钮的事件处理
        private async void PlayPauseButton_Click(object sender, RoutedEventArgs e) => await SendCommandAndRefreshAsync(MediaCommand.PlayPause);
        private async void NextButton_Click(object sender, RoutedEventArgs e) => await SendCommandAndRefreshAsync(MediaCommand.NextTrack);
        private async void PreviousButton_Click(object sender, RoutedEventArgs e) => await SendCommandAndRefreshAsync(MediaCommand.PreviousTrack);
        private async void VolumeUpButton_Click(object sender, RoutedEventArgs e) => await SendCommandAndRefreshAsync(MediaCommand.VolumeUp);
        private async void VolumeDownButton_Click(object sender, RoutedEventArgs e) => await SendCommandAndRefreshAsync(MediaCommand.VolumeDown);
        private async void MuteButton_Click(object sender, RoutedEventArgs e) => await SendCommandAndRefreshAsync(MediaCommand.VolumeMute);
    }
}
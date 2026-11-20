using System.Drawing;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace KeyTrail.Desktop;

public partial class App : Application
{
    private readonly bool _renderOnly;
    public App() { }
    internal App(bool renderOnly) { _renderOnly = renderOnly; }
    private Mutex? _instanceMutex;
    private bool _ownsMutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showWait;
    private EventWaitHandle? _exitEvent;
    private RegisteredWaitHandle? _exitWait;
    private DispatcherTimer? _releaseUiTimer;
    private WindowSession? _windowSession;
    private StatsController? _controller;
    private Forms.NotifyIcon? _tray;
    private Icon? _icon;
    private Forms.ToolStripMenuItem? _pauseMenu;
    private bool _exitStarted;
    private bool _notifiedHidden;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (_renderOnly) return;
        try
        {
            var args = e.Args;
            var dataIndex = Array.IndexOf(args, "--data-dir");
            if (dataIndex >= 0 && dataIndex + 1 >= args.Length) throw new ArgumentException("--data-dir 需要指定数据目录。");
            var demo = args.Contains("--demo");
            if (demo && dataIndex < 0) throw new ArgumentException("演示模式必须指定独立的 --data-dir，不能写入真实统计。");
            var root = Path.GetFullPath(dataIndex >= 0 ? args[dataIndex + 1] : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KeyTrail"));
            // A directory marker prevents a demonstration fixture from ever becoming a real collection store.
            var modeMarker = Path.Combine(root, ".demo-data");
            if (!demo && File.Exists(modeMarker)) throw new InvalidOperationException("该目录包含演示数据，请选择其他目录进行真实统计。");
            if (demo)
            {
                if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any() && !File.Exists(modeMarker))
                    throw new InvalidOperationException("演示模式只允许使用空目录或已有的演示目录。");
                Directory.CreateDirectory(root); File.WriteAllText(modeMarker, "KeyTrail demonstration only");
            }
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root.ToUpperInvariant())))[..20];
            _instanceMutex = new Mutex(true, @"Local\KeyTrail-" + hash, out _ownsMutex);
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\KeyTrail-show-" + hash);
            if (args.Contains("--exit"))
            {
                if (!_ownsMutex && EventWaitHandle.TryOpenExisting(@"Local\KeyTrail-exit-" + hash, out var existing))
                { using (existing) existing.Set(); Shutdown(); }
                else Shutdown(_ownsMutex ? 0 : 2);
                return;
            }
            if (!_ownsMutex) { _showEvent.Set(); Shutdown(); return; }
            _exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\KeyTrail-exit-" + hash);
            _controller = new StatsController(root, demo);
            CreateTray();
            _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent, (_, _) => Dispatcher.BeginInvoke(ShowMain), null, -1, false);
            _exitWait = ThreadPool.RegisterWaitForSingleObject(_exitEvent, (_, _) => Dispatcher.BeginInvoke(ExitApplication), null, -1, false);
            SystemEvents.SessionSwitch += SessionSwitch;
            SystemEvents.PowerModeChanged += PowerModeChanged;
            if (!args.Contains("--background")) ShowMain();
        }
        catch (Exception ex)
        {
            MessageBox.Show("键盘统计器未能启动。\n\n" + ex.Message, "键盘统计器 · 启动提示", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void CreateTray()
    {
        using var resource = GetResourceStream(new Uri("pack://application:,,,/Assets/KeyTrail.ico"))!.Stream;
        _icon = new Icon(resource);
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开键盘统计器", null, (_, _) => Dispatcher.Invoke(ShowMain));
        _pauseMenu = new Forms.ToolStripMenuItem("暂停统计", null, (_, _) => Dispatcher.Invoke(() =>
        {
            try { _controller!.SetPaused(!_controller.Accumulator.Paused); (MainWindow as MainWindow)?.Refresh(); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "键盘统计器"); }
        }));
        menu.Items.Add(_pauseMenu);
        menu.Opening += (_, _) =>
        {
            _pauseMenu.Text = _controller!.Accumulator.Paused ? "继续统计" : "暂停统计";
            _pauseMenu.Enabled = !_controller.IsDemo;
        };
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(ExitApplication));
        _tray = new Forms.NotifyIcon { Text = _controller!.IsDemo ? "键盘统计器 · 演示模式" : "键盘统计器 · 每日键鼠统计", Icon = _icon, ContextMenuStrip = menu, Visible = true };
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowMain);
    }

    private void ShowMain()
    {
        if (_exitStarted || _controller is null) return;
        _releaseUiTimer?.Stop();
        var window = OpenDashboard();
        window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
    }

    internal void AttachController(StatsController controller) => _controller = controller;
    internal MainWindow OpenDashboard()
    {
        _releaseUiTimer?.Stop();
        if (MainWindow is MainWindow existing) return existing;
        var window = new MainWindow(_controller ?? throw new InvalidOperationException("Statistics controller is unavailable."));
        MainWindow = window;
        if (_windowSession is not null) window.RestoreSession(_windowSession);
        return window;
    }

    internal void ReleaseDashboard()
    {
        _releaseUiTimer?.Stop();
        if (MainWindow is not MainWindow window || window.IsVisible || _exitStarted) return;
        _windowSession = window.CaptureSession();
        window.AllowClose = true;
        window.Close();
        MainWindow = null;
        _controller?.Store.ReleaseReadCache();
        // One collection when a substantial UI is retired, never a recurring working-set trim.
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            if (MainWindow is null) GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, blocking: false);
        }));
    }

    public void NotifyHidden()
    {
        if (_releaseUiTimer is null)
        {
            _releaseUiTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            _releaseUiTimer.Tick += (_, _) => ReleaseDashboard();
        }
        _releaseUiTimer.Stop(); _releaseUiTimer.Start();
        if (_notifiedHidden) return;
        _notifiedHidden = true;
        _tray?.ShowBalloonTip(2500, "键盘统计器已留在系统托盘", _controller!.Accumulator.Paused ? "统计保持暂停。双击托盘图标可重新打开。" : "键鼠统计继续运行。双击托盘图标可重新打开。", Forms.ToolTipIcon.Info);
    }

    public void ExitApplication()
    {
        if (_exitStarted) return;
        try { _controller?.Flush(); }
        catch (Exception ex)
        {
            ShowMain();
            MessageBox.Show("尚未成功保存统计，请处理后再退出。\n\n" + ex.Message, "键盘统计器 · 保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        _exitStarted = true;
        if (MainWindow is MainWindow window) { window.AllowClose = true; window.Close(); }
        Shutdown();
    }

    private void SessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        _controller?.Accumulator.ResetPressed();
        if (e.Reason == SessionSwitchReason.SessionLock) Task.Run(() => { try { _controller?.Flush(); } catch { /* background saver reports persistent failures */ } });
    }
    private void PowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        _controller?.Accumulator.ResetPressed();
        if (e.Mode == PowerModes.Suspend) { try { _controller?.Flush(); } catch { } }
    }
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    { try { _controller?.Flush(); } catch { } base.OnSessionEnding(e); }
    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.SessionSwitch -= SessionSwitch;
        SystemEvents.PowerModeChanged -= PowerModeChanged;
        _showWait?.Unregister(null);
        _showEvent?.Dispose();
        _exitWait?.Unregister(null);
        _exitEvent?.Dispose();
        _releaseUiTimer?.Stop();
        _tray?.Dispose(); _icon?.Dispose();
        try { _controller?.Dispose(); } catch { /* Avoid blocking Windows shutdown; the previous snapshot remains recoverable. */ }
        if (_ownsMutex) _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}

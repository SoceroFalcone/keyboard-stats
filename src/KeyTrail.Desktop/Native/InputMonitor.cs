using System.ComponentModel;
using System.Runtime.InteropServices;

namespace KeyTrail.Desktop.Native;

// A dedicated message-pump thread keeps the OS hook independent of rendering and disk I/O.
// The callback immediately aggregates counts; it never translates keys into entered characters.
public sealed class InputMonitor : IDisposable
{
    private readonly KeyAccumulator _accumulator;
    private readonly bool _allowInjectedForTest;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly HookProc _callback;
    private readonly HookProc _mouseCallback;
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _hook;
    private IntPtr _mouseHook;
    private Exception? _startError;
    public bool IsRunning => _hook != IntPtr.Zero && _mouseHook != IntPtr.Zero;
    public string? LastError { get; private set; }

    public InputMonitor(KeyAccumulator accumulator, bool allowInjectedForTest = false)
    {
        _accumulator = accumulator;
        _allowInjectedForTest = allowInjectedForTest;
        _callback = Callback;
        _mouseCallback = MouseCallback;
    }

    public void Start()
    {
        if (_thread is not null) throw new InvalidOperationException("监听已经启动。");
        _thread = new Thread(Run) { IsBackground = true, Name = "KeyTrail input monitor" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        if (!_ready.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("键鼠监听启动超时，请重新启动应用。");
        if (_startError is not null) throw new InvalidOperationException("无法启动键鼠统计。", _startError);
    }

    private void Run()
    {
        try
        {
            _threadId = GetCurrentThreadId();
            PeekMessage(out _, IntPtr.Zero, 0, 0, 0); // Ensure PostThreadMessage can always reach the thread.
            _hook = SetWindowsHookEx(13, _callback, GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            _mouseHook = SetWindowsHookEx(14, _mouseCallback, GetModuleHandle(null), 0);
            if (_mouseHook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            _ready.Set();
            int result;
            while ((result = GetMessage(out var message, IntPtr.Zero, 0, 0)) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
            if (result == -1) LastError = "键鼠监听消息循环中断，请重新启动应用。";
        }
        catch (Exception ex) { _startError = ex; LastError = ex.Message; }
        finally
        {
            _ready.Set();
            if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
            if (_mouseHook != IntPtr.Zero) { UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
        }
    }

    private IntPtr MouseCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        ProcessMouseMessage(code, wParam.ToInt32(), lParam);
        return CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    internal void ProcessMouseMessage(int code, int message, IntPtr lParam)
    {
        // Mouse motion can arrive at high frequency: pass it through before marshalling or taking a lock.
        if (code >= 0 && MouseCatalog.IsButtonMessage(message))
        {
            try
            {
                var data = Marshal.PtrToStructure<MouseData>(lParam);
                _accumulator.HandleMouse(message, data.ButtonData,
                    !_allowInjectedForTest && (data.Flags & 3) != 0, DateTime.Now);
            }
            catch (Exception ex) { LastError = "鼠标统计发生异常：" + ex.Message; }
        }
    }

    private IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var kind = wParam.ToInt32();
            if (kind is 0x100 or 0x101 or 0x104 or 0x105)
            {
                try
                {
                    var data = Marshal.PtrToStructure<KeyboardData>(lParam);
                    _accumulator.Handle((int)data.VkCode, (int)data.ScanCode, (data.Flags & 1) != 0,
                        kind is 0x100 or 0x104, !_allowInjectedForTest && (data.Flags & 0x10) != 0, DateTime.Now);
                }
                catch (Exception ex) { LastError = "键盘统计发生异常：" + ex.Message; }
            }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (_thread is null) return;
        PostThreadMessage(_threadId, 0x12, IntPtr.Zero, IntPtr.Zero);
        if (!_thread.Join(TimeSpan.FromSeconds(3))) LastError = "键鼠监听线程未能及时退出。";
        _thread = null;
    }

    [StructLayout(LayoutKind.Sequential)] private struct KeyboardData
    { public uint VkCode, ScanCode, Flags, Time; public UIntPtr ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseData
    { public int X, Y; public uint ButtonData, Flags, Time; public UIntPtr ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct Message
    { public IntPtr Hwnd; public uint Value; public UIntPtr WParam; public IntPtr LParam; public uint Time; public int X, Y; public uint Private; }
    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc callback, IntPtr module, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern int GetMessage(out Message message, IntPtr window, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out Message message, IntPtr window, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref Message message);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint id, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
}

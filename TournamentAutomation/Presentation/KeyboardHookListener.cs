using System.Runtime.InteropServices;
using TournamentAutomation.Application.Hotkeys;
using TournamentAutomation.Application.Logging;

namespace TournamentAutomation.Presentation;

public sealed class KeyboardHookListener : IHotkeyListener
{
    private readonly IAppLogger _logger;

    public KeyboardHookListener(IAppLogger logger)
    {
        _logger = logger;
    }

    public Task ListenAsync(IEnumerable<string> keySequences, Func<KeyStroke, Task> onKey, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Keyboard hook hotkeys are only supported on Windows.");

        return Task.Run(() => RunLoop(onKey, cancellationToken), cancellationToken);
    }

    private void RunLoop(Func<KeyStroke, Task> onKey, CancellationToken cancellationToken)
    {
        using var hook = new KeyboardHook(onKey, _logger);
        hook.Start();

        while (!cancellationToken.IsCancellationRequested)
        {
            if (!GetMessage(out var msg, IntPtr.Zero, 0, 0))
                break;

            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
    }

    private sealed class KeyboardHook : IDisposable
    {
        private readonly Func<KeyStroke, Task> _onKey;
        private readonly IAppLogger _logger;
        private readonly LowLevelKeyboardProc _proc;
        private IntPtr _hookId;

        public KeyboardHook(Func<KeyStroke, Task> onKey, IAppLogger logger)
        {
            _onKey = onKey;
            _logger = logger;
            _proc = HookCallback;
        }

        public void Start()
        {
            _hookId = SetHook(_proc);
            if (_hookId == IntPtr.Zero)
                _logger.Warn("HOTKEY: Failed to install keyboard hook.");
            else
                _logger.Info("HOTKEY: Keyboard hook active.");
        }

        public void Dispose()
        {
            if (_hookId != IntPtr.Zero)
                UnhookWindowsHookEx(_hookId);
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                var vkCode = Marshal.ReadInt32(lParam);
                var keyName = VkToKeyString((uint)vkCode);
                if (!string.IsNullOrWhiteSpace(keyName))
                {
                    var stroke = new KeyStroke
                    {
                        Key = keyName,
                        Ctrl = IsKeyDown(VK_CONTROL),
                        Alt = IsKeyDown(VK_MENU),
                        Shift = IsKeyDown(VK_SHIFT)
                    };

                    _onKey(stroke).GetAwaiter().GetResult();
                }
            }

            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private static bool IsKeyDown(int vk) => (GetKeyState(vk) & 0x8000) != 0;

        private static IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;
            return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule?.ModuleName), 0);
        }
    }

    private static string VkToKeyString(uint key)
    {
        if (key is >= 0x41 and <= 0x5A)
            return ((char)key).ToString();

        if (key is >= 0x30 and <= 0x39)
            return "D" + (char)key;

        if (key is >= 0x70 and <= 0x7B)
            return $"F{key - 0x6F}";

        return key switch
        {
            0x0D => ConsoleKey.Enter.ToString(),
            0x1B => ConsoleKey.Escape.ToString(),
            0x20 => ConsoleKey.Spacebar.ToString(),
            0x09 => ConsoleKey.Tab.ToString(),
            0x08 => ConsoleKey.Backspace.ToString(),
            0x26 => ConsoleKey.UpArrow.ToString(),
            0x28 => ConsoleKey.DownArrow.ToString(),
            0x25 => ConsoleKey.LeftArrow.ToString(),
            0x27 => ConsoleKey.RightArrow.ToString(),
            _ => string.Empty
        };
    }

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public UIntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
        public uint lPrivate;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }
}

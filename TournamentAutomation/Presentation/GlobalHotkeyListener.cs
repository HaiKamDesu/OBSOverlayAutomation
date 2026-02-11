using System.Runtime.InteropServices;
using TournamentAutomation.Application.Hotkeys;
using TournamentAutomation.Application.Logging;

namespace TournamentAutomation.Presentation;

public sealed class GlobalHotkeyListener : IHotkeyListener
{
    private readonly IAppLogger _logger;

    public GlobalHotkeyListener(IAppLogger logger)
    {
        _logger = logger;
    }

    public Task ListenAsync(IEnumerable<string> keySequences, Func<KeyStroke, Task> onKey, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Global hotkeys are only supported on Windows.");

        return Task.Run(() => RunLoop(keySequences, onKey, cancellationToken), cancellationToken);
    }

    private void RunLoop(IEnumerable<string> keySequences, Func<KeyStroke, Task> onKey, CancellationToken cancellationToken)
    {
        var sequences = keySequences
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var registrations = new Dictionary<int, (uint Modifiers, uint Key)>(sequences.Length);

        try
        {
            var id = 1;
            foreach (var sequence in sequences)
            {
                if (!TryParseSequence(sequence, out var mods, out var key))
                {
                    _logger.Warn($"Could not parse hotkey '{sequence}'.");
                    continue;
                }

                if (!RegisterHotKey(IntPtr.Zero, id, mods, key))
                {
                    _logger.Warn($"Failed to register hotkey '{sequence}'.");
                    continue;
                }

                registrations[id] = (mods, key);
                id++;
            }

            _logger.Info($"Registered {registrations.Count} global hotkeys.");

            var threadId = GetCurrentThreadId();
            using var _ = cancellationToken.Register(() => PostThreadMessage(threadId, WM_QUIT, UIntPtr.Zero, IntPtr.Zero));

            while (GetMessage(out var msg, IntPtr.Zero, 0, 0))
            {
                if (msg.message == WM_HOTKEY)
                {
                    var hotkeyId = (int)msg.wParam;
                    if (registrations.TryGetValue(hotkeyId, out var entry))
                    {
                        var stroke = CreateKeyStroke(entry.Modifiers, entry.Key);
                        onKey(stroke).GetAwaiter().GetResult();
                    }
                }

                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }
        finally
        {
            foreach (var id in registrations.Keys)
            {
                UnregisterHotKey(IntPtr.Zero, id);
            }
        }
    }

    private static KeyStroke CreateKeyStroke(uint modifiers, uint key)
    {
        return new KeyStroke
        {
            Ctrl = (modifiers & MOD_CONTROL) != 0,
            Alt = (modifiers & MOD_ALT) != 0,
            Shift = (modifiers & MOD_SHIFT) != 0,
            Key = VkToKeyString(key)
        };
    }

    private static bool TryParseSequence(string sequence, out uint modifiers, out uint key)
    {
        modifiers = 0;
        key = 0;

        var parts = sequence.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            var token = part.Trim();
            if (token.Equals("CTRL", StringComparison.OrdinalIgnoreCase) || token.Equals("CONTROL", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= MOD_CONTROL;
                continue;
            }

            if (token.Equals("ALT", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= MOD_ALT;
                continue;
            }

            if (token.Equals("SHIFT", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= MOD_SHIFT;
                continue;
            }

            if (token.Equals("WIN", StringComparison.OrdinalIgnoreCase) || token.Equals("WINDOWS", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= MOD_WIN;
                continue;
            }

            if (!TryParseKey(token, out var parsedKey))
                return false;

            key = parsedKey;
        }

        return key != 0;
    }

    private static bool TryParseKey(string token, out uint key)
    {
        key = 0;

        if (Enum.TryParse<ConsoleKey>(token, true, out var consoleKey))
        {
            key = ConsoleKeyToVirtualKey(consoleKey);
            return key != 0;
        }

        if (token.Length == 1)
        {
            var ch = token[0];
            key = char.ToUpperInvariant(ch) switch
            {
                >= 'A' and <= 'Z' => (uint)ch,
                >= '0' and <= '9' => (uint)ch,
                _ => 0
            };
            return key != 0;
        }

        return false;
    }

    private static uint ConsoleKeyToVirtualKey(ConsoleKey key)
    {
        if (key is >= ConsoleKey.A and <= ConsoleKey.Z)
            return (uint)('A' + (key - ConsoleKey.A));

        if (key is >= ConsoleKey.D0 and <= ConsoleKey.D9)
            return (uint)('0' + (key - ConsoleKey.D0));

        if (key is >= ConsoleKey.F1 and <= ConsoleKey.F12)
            return (uint)(0x70 + (key - ConsoleKey.F1));

        return key switch
        {
            ConsoleKey.Enter => 0x0D,
            ConsoleKey.Escape => 0x1B,
            ConsoleKey.Spacebar => 0x20,
            ConsoleKey.Tab => 0x09,
            ConsoleKey.Backspace => 0x08,
            ConsoleKey.UpArrow => 0x26,
            ConsoleKey.DownArrow => 0x28,
            ConsoleKey.LeftArrow => 0x25,
            ConsoleKey.RightArrow => 0x27,
            _ => 0
        };
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
            _ => key.ToString()
        };
    }

    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;

    private const uint WM_HOTKEY = 0x0312;
    private const uint WM_QUIT = 0x0012;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint idThread, uint msg, UIntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

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

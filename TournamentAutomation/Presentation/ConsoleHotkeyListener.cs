using TournamentAutomation.Application.Hotkeys;

namespace TournamentAutomation.Presentation;

public sealed class ConsoleHotkeyListener : IHotkeyListener
{
    public Task ListenAsync(IEnumerable<string> keySequences, Func<KeyStroke, Task> onKey, CancellationToken cancellationToken)
    {
        return Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!Console.KeyAvailable)
                {
                    await Task.Delay(25, cancellationToken);
                    continue;
                }

                var keyInfo = Console.ReadKey(intercept: true);
                var stroke = new KeyStroke
                {
                    Key = keyInfo.Key.ToString(),
                    Ctrl = keyInfo.Modifiers.HasFlag(ConsoleModifiers.Control),
                    Alt = keyInfo.Modifiers.HasFlag(ConsoleModifiers.Alt),
                    Shift = keyInfo.Modifiers.HasFlag(ConsoleModifiers.Shift)
                };

                await onKey(stroke);
            }
        }, cancellationToken);
    }
}

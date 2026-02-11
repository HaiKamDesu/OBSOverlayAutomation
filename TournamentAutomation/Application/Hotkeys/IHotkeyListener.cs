namespace TournamentAutomation.Application.Hotkeys;

public interface IHotkeyListener
{
    Task ListenAsync(IEnumerable<string> keySequences, Func<KeyStroke, Task> onKey, CancellationToken cancellationToken);
}

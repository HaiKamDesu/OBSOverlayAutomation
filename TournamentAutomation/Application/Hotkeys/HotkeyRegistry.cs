using TournamentAutomation.Application.Commands;

namespace TournamentAutomation.Application.Hotkeys;

public sealed class HotkeyRegistry
{
    private readonly Dictionary<string, ICommand> _bindings = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, ICommand> Bindings => _bindings;

    public void Add(ConsoleKey key, ICommand command) => Add(key.ToString(), command);

    public void Add(string keySequence, ICommand command)
    {
        if (string.IsNullOrWhiteSpace(keySequence))
            throw new ArgumentException("Key sequence is required.", nameof(keySequence));
        ArgumentNullException.ThrowIfNull(command);

        _bindings[keySequence] = command;
    }
}

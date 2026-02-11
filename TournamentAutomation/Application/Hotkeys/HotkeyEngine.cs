using TournamentAutomation.Application.Commands;
using TournamentAutomation.Application.Logging;

namespace TournamentAutomation.Application.Hotkeys;

public sealed class HotkeyEngine
{
    private readonly IHotkeyListener _listener;
    private readonly HotkeyRegistry _registry;
    private readonly CommandDispatcher _dispatcher;
    private readonly CommandContext _context;
    private readonly IAppLogger _logger;
    private readonly string _modeKey;
    private readonly TimeSpan _modeTimeout;

    private DateTimeOffset? _modeEnteredAt;

    public HotkeyEngine(
        IHotkeyListener listener,
        HotkeyRegistry registry,
        CommandDispatcher dispatcher,
        CommandContext context,
        IAppLogger logger,
        string modeKey,
        TimeSpan modeTimeout)
    {
        _listener = listener;
        _registry = registry;
        _dispatcher = dispatcher;
        _context = context;
        _logger = logger;
        _modeKey = modeKey;
        _modeTimeout = modeTimeout;
    }

    public Task RunAsync(CancellationToken cancellationToken)
    {
        var keySequences = _registry.Bindings.Keys
            .Concat(new[] { _modeKey })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return _listener.ListenAsync(keySequences, HandleKeyAsync, cancellationToken);
    }

    private async Task HandleKeyAsync(KeyStroke stroke)
    {
        var keyString = stroke.ToString();

        if (string.Equals(keyString, _modeKey, StringComparison.OrdinalIgnoreCase))
        {
            _modeEnteredAt = DateTimeOffset.UtcNow;
            _logger.Info("HOTKEY: Mode enabled.");
            return;
        }

        if (_modeEnteredAt is null)
            return;

        if (DateTimeOffset.UtcNow - _modeEnteredAt > _modeTimeout)
        {
            _modeEnteredAt = null;
            _logger.Info("HOTKEY: Mode timed out.");
            return;
        }

        _modeEnteredAt = null;

        if (_registry.Bindings.TryGetValue(keyString, out var command))
        {
            await _dispatcher.ExecuteAsync(command, _context, CancellationToken.None);
        }
        else
        {
            _logger.Warn($"HOTKEY: No binding for '{keyString}'.");
        }
    }
}

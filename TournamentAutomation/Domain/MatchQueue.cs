using System.Collections.Concurrent;

namespace TournamentAutomation.Domain;

public sealed class MatchQueue
{
    private readonly ConcurrentQueue<MatchState> _queue = new();

    public int Count => _queue.Count;

    public void Enqueue(MatchState match)
    {
        ArgumentNullException.ThrowIfNull(match);
        _queue.Enqueue(match);
    }

    public bool TryDequeue(out MatchState? match) => _queue.TryDequeue(out match);

    public IReadOnlyList<MatchState> Snapshot() => _queue.ToArray();

    public void Clear()
    {
        while (_queue.TryDequeue(out _))
        {
        }
    }
}

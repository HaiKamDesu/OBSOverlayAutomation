namespace TournamentAutomation.Application.Hotkeys;

public sealed record KeyStroke
{
    public string Key { get; init; } = "";
    public bool Ctrl { get; init; }
    public bool Alt { get; init; }
    public bool Shift { get; init; }

    public override string ToString()
    {
        var parts = new List<string>(4);
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        parts.Add(Key);
        return string.Join("+", parts);
    }
}

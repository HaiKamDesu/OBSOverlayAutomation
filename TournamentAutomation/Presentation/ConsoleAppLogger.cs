using TournamentAutomation.Application.Logging;

namespace TournamentAutomation.Presentation;

public sealed class ConsoleAppLogger : IAppLogger
{
    public void Info(string message) => Write("INFO", message, ConsoleColor.Gray);
    public void Warn(string message) => Write("WARN", message, ConsoleColor.Yellow);
    public void Error(string message, Exception? exception = null)
    {
        Write("ERROR", message, ConsoleColor.Red);
        if (exception is not null)
        {
            Console.ForegroundColor = ConsoleColor.DarkRed;
            Console.WriteLine(exception);
            Console.ResetColor();
        }
    }

    private static void Write(string level, string message, ConsoleColor color)
    {
        var timestamp = DateTimeOffset.Now.ToString("HH:mm:ss");
        var categoryColor = TryGetCategoryColor(message);
        Console.ForegroundColor = categoryColor ?? color;
        Console.WriteLine($"[{timestamp}] {level} {message}");
        Console.ResetColor();
    }

    private static ConsoleColor? TryGetCategoryColor(string message)
    {
        if (message.StartsWith("HOTKEY:", StringComparison.OrdinalIgnoreCase))
            return ConsoleColor.Cyan;
        if (message.StartsWith("CMD ", StringComparison.OrdinalIgnoreCase))
            return ConsoleColor.Green;
        if (message.StartsWith("OBS:", StringComparison.OrdinalIgnoreCase))
            return ConsoleColor.Magenta;

        return null;
    }
}

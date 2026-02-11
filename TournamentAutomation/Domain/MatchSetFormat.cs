namespace TournamentAutomation.Domain;

public enum MatchSetFormat
{
    FT2 = 2,
    FT3 = 3,
    BO5 = 3,
    BO7 = 4
}

public static class BestOfFormatExtensions
{
    public static int WinsRequired(this MatchSetFormat format) => (int)format;
}

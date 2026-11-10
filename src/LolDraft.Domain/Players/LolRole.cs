namespace LolDraft.Domain.Players;

/// <summary>As cinco posições do Summoner's Rift.</summary>
public enum LolRole
{
    Top = 0,
    Jungle = 1,
    Mid = 2,
    Adc = 3,
    Support = 4
}

public static class LolRoleExtensions
{
    public static readonly LolRole[] All =
        [LolRole.Top, LolRole.Jungle, LolRole.Mid, LolRole.Adc, LolRole.Support];

    public static string Label(this LolRole role) => role switch
    {
        LolRole.Top => "Top",
        LolRole.Jungle => "Jungle",
        LolRole.Mid => "Mid",
        LolRole.Adc => "ADC",
        LolRole.Support => "Support",
        _ => role.ToString()
    };

    /// <summary>Emoji usado nos embeds e menus do Discord.</summary>
    public static string Emoji(this LolRole role) => role switch
    {
        LolRole.Top => "🛡️",
        LolRole.Jungle => "🌲",
        LolRole.Mid => "🔥",
        LolRole.Adc => "🏹",
        LolRole.Support => "💖",
        _ => "❔"
    };
}

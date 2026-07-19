namespace BeatLeader_RankedPlay_Tester;

/// <summary>
/// Generates test-bot account credentials for every rank bracket. The bot
/// runner uses these to log in and fill the ranked-play queue.
///
/// Login format: "TestPlayer_{bracketName}_{index}" (e.g. "TestPlayer_rank5000_1").
/// Password = login. The main server recognises this pattern in RankedPlayBotUtils,
/// parses the rank out of the login, and seeds the bot's MMR from that rank
/// via the standard log-curve formula (§5.1) — the ApproxSeedMMR column on
/// each bracket is just an informational hint for the operator.
///
/// Brackets cover the spectrum from top-10 down through rank 20000. Pick which
/// brackets to enable by uncommenting the rows below.
/// </summary>
public static class PlayerGenerator {

    public static readonly RankBracket[] Brackets = {
        new("top10",     1,     10,    2500f),
        new("top50",     10,    50,    2160f),
        new("top100",    50,    100,   2100f),
        new("rank200",   100,   200,   2040f),
        new("rank300",   200,   300,   2000f),
        new("rank400",   300,   400,   1980f),
        new("rank500",   400,   500,   1960f),
        new("rank1000",  500,   1000,  1900f),
        new("rank1500",  1000,  1500,  1860f),
        new("rank2000",  1500,  2000,  1840f),
        new("rank3000",  2000,  3000,  1800f),
        new("rank5000",  3000,  5000,  1760f),
        new("rank10000", 5000,  10000, 1700f),
        new("rank20000", 10000, 20000, 1640f),
    };

    public const int PlayersPerBracket = 2;

    public static List<PlayerProfile> GenerateAll(int playersPerBracket = PlayersPerBracket) {
        var players = new List<PlayerProfile>();

        foreach (var bracket in Brackets) {
            for (int i = 1; i <= playersPerBracket; i++) {
                string login = $"TestPlayer_{bracket.Name}_{i}";

                int rank = bracket.RankMin +
                    (int)((bracket.RankMax - bracket.RankMin) * ((float)(i - 1) / playersPerBracket));

                players.Add(new PlayerProfile {
                    Login = login,
                    BracketName = bracket.Name,
                    BracketIndex = i,
                    EstimatedRank = rank,
                });
            }
        }

        return players;
    }
}

/// <summary>
/// Bracket descriptor. <see cref="ApproxSeedMMR"/> is informational only — the
/// real seed comes from the server applying the §5.1 log-curve to the bot's
/// designated rank.
/// </summary>
public record RankBracket(string Name, int RankMin, int RankMax, float ApproxSeedMMR);

public class PlayerProfile {
    public string Login { get; set; } = "";
    public string BracketName { get; set; } = "";
    public int BracketIndex { get; set; }
    public int EstimatedRank { get; set; }
}

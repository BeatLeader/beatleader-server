namespace BeatLeader_Server.Extensions
{
    public static class StringExtensions
    {
        public static string FirstCharToLower(this string input) =>
            input switch
            {
                null => null,
                "" => "",
                _ => string.Concat(input[0].ToString().ToLower(), input.AsSpan(1))
            };
    }
}

namespace BeatLeader_Server.Utils
{
    public class LinkUtils
    {
        public static (string?, string?) ServiceAndIconFromLink(string link) {
            if (link.StartsWith("https://www.youtube.com/watch?") || link.StartsWith("https://youtu.be/")) {
                return ("YouTube", "https://beatleadercdn.blob.core.windows.net/assets/youtubeservice.png");
            } else if (link.StartsWith("https://twitter.com/")) {
                return ("Twitter", "https://beatleadercdn.blob.core.windows.net/assets/twitterservice.png");
            } else if (link.StartsWith("https://www.twitch.tv/videos/")) {
                return ("Twitch", "https://beatleadercdn.blob.core.windows.net/assets/twitchservice.png");
            }

            return (null, null);
        }
    }
}

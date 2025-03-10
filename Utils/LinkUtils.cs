﻿namespace BeatLeader_Server.Utils
{
    public class LinkUtils
    {
        public static (string?, string?) ServiceAndIconFromLink(string link) {
            if (link.StartsWith("https://www.youtube.com/watch?") || link.StartsWith("https://youtu.be/")) {
                return ("YouTube", "https://cdn.assets.beatleader.com/youtubeservice.png");
            } else if (link.StartsWith("https://twitter.com/")) {
                return ("Twitter", "https://cdn.assets.beatleader.com/twitterservice.png");
            } else if (link.StartsWith("https://www.twitch.tv/videos/")) {
                return ("Twitch", "https://cdn.assets.beatleader.com/twitchservice.png");
            }

            return (null, null);
        }
    }
}

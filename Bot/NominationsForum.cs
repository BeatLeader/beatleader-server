using BeatLeader_Server.Models;
using CoreHtmlToImage;
using Discord;
using Discord.WebSocket;

namespace BeatLeader_Server.Bot
{
    public class NominationsForum
    {
        public NominationsForum() {
        }

        public async Task<ulong> OpenNomination(string playerName, Leaderboard leaderboard) {
            var guild = BotService.Client.GetGuild(921820046345523311);

            var ForumChannel = guild.GetForumChannel(1075436060139597886);

            string message = "";
            message += " **" + playerName + "** nominated **" + leaderboard.Difficulty.DifficultyName + "** diff of **" + leaderboard.Song.Name + "**! \n";
            message += "\n";
            message += "https://beatleader.xyz/leaderboard/global/" + leaderboard.Id;

            var post = await ForumChannel.CreatePostAsync(leaderboard.Song.Name, ThreadArchiveDuration.OneWeek, null, message, embeds: new Embed []{ 
                new EmbedBuilder()
                    .WithThumbnailUrl(leaderboard.Song.CoverImage)
                    .WithTitle("Leaderboard")
                    .WithUrl("https://beatleader.xyz/leaderboard/global/" + leaderboard.Id)
                    .Build()
            });

            return post.Id;
        }

        public async Task CloseNomination(ulong id) {
            var channel = BotService.Client.GetGuild(921820046345523311).GetThreadChannel(id);
            if (channel != null) {
                await channel.ModifyAsync(props => {
                    props.Archived = true;
                });
            }
        }

        public async Task PostMessage(ulong forum, string message) {
            var channel = BotService.Client.GetGuild(921820046345523311).GetThreadChannel(forum);

            await channel.SendMessageAsync(message);
        }
        
        public async Task<ulong> PostComment(ulong forum, string comment, Player player) {
            var converter = new HtmlConverter();
            var bytes = converter.FromHtmlString(comment);

            var channel = BotService.Client.GetGuild(921820046345523311).GetThreadChannel(forum);

            return (await channel.SendFileAsync(new FileAttachment(new MemoryStream(bytes), "message.jpg"), "From " + player.Name)).Id;
        }

        public async Task<ulong> UpdateComment(ulong forum, ulong id, string comment, Player player) {
            await DeleteComment(forum, id);

            var converter = new HtmlConverter();
            var bytes = converter.FromHtmlString(comment);

            var channel = BotService.Client.GetGuild(921820046345523311).GetThreadChannel(forum);

            return (await channel.SendFileAsync(new FileAttachment(new MemoryStream(bytes), "message.jpg"), "From " + player.Name)).Id;
        }

        public async Task DeleteComment(ulong forum, ulong id) {
            await BotService.Client.GetGuild(921820046345523311).GetThreadChannel(forum).DeleteMessageAsync(id);
        }
    }
}

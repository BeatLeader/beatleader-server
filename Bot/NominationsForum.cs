using BeatLeader_Server.Models;
using CoreHtmlToImage;
using Discord;
using Discord.WebSocket;
using System.Reflection;

namespace BeatLeader_Server.Bot
{
    public class NominationsForum
    {
        public NominationsForum() {
        }

        private async Task<SocketThreadChannel?> ReturnOrUnarchiveThread(string id) {
            var guild = BotService.Client.GetGuild(921820046345523311);
            ulong longId = ulong.Parse(id); 
            var channel = guild.GetThreadChannel(longId);
            
            if (channel == null) {

                var ctor = typeof(SocketThreadChannel).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).FirstOrDefault();

                channel = (SocketThreadChannel)ctor.Invoke(new object[5] { BotService.Client, guild, longId, guild.GetForumChannel(1075436060139597886), null });
                
                try {
                    await channel.ModifyAsync(props => {
                        props.Archived = false;
                    });
                } catch { }

                await Task.Delay(TimeSpan.FromSeconds(2));
                channel = guild.GetThreadChannel(longId);
            }

            return channel;
        }

        public async Task<string> OpenNomination(string playerName, Leaderboard leaderboard) {
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

            return post.Id.ToString();
        }

        public async Task CloseNomination(string id) {
            var channel = BotService.Client.GetGuild(921820046345523311).GetThreadChannel(ulong.Parse(id));
            if (channel != null) {
                await channel.ModifyAsync(props => {
                    props.Archived = true;
                });
            }
        }

        public async Task PostMessage(string forum, string message) {
            var channel = await ReturnOrUnarchiveThread(forum);

            if (channel != null) {
                await channel.SendMessageAsync(message);
            }
        }
        
        public async Task<string> PostComment(string forum, string comment, Player player) {
            var converter = new HtmlConverter();
            var length = 300;
            if (comment.Length > 200) {
                length = 700;
            }

            var bytes = converter.FromHtmlString(comment, length, CoreHtmlToImage.ImageFormat.Png, 100);

            var channel = await ReturnOrUnarchiveThread(forum);
            if (channel == null) {
                return "";
            }

            var playername = player.Name; 
            
            var discord = player.Socials?.FirstOrDefault(s => s.Service == "Discord");
            if (discord != null)
            {
                try {
                    ulong discordId = ulong.Parse(discord.UserId); 
                    playername = $"<@{discordId}>";
                    var user = await ((IGuild)BotService.Client.GetGuild(921820046345523311)).GetUserAsync(discordId, CacheMode.AllowDownload);
                    await channel.AddUserAsync(user);
                } catch { }
            }

            return (await channel.SendFileAsync(
                new FileAttachment(new MemoryStream(bytes), "message.png"), "From " + playername,
                allowedMentions: new AllowedMentions { UserIds = new List<ulong>() })).Id.ToString();
        }

        public async Task<string> UpdateComment(string forum, string id, string comment, Player player) {
            await DeleteComment(forum, id);

            return await PostComment(forum, comment, player);
        }

        public async Task DeleteComment(string forum, string id) {
            var channel = await ReturnOrUnarchiveThread(forum);
            if (channel == null) {
                return;
            }
            await channel.DeleteMessageAsync(ulong.Parse(id));
        }
    }
}

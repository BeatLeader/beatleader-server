using BeatLeader_Server.Models;
using CoreHtmlToImage;
using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Reflection;

namespace BeatLeader_Server.Bot
{
    public class RTNominationsForum
    {
        public const ulong NominationForumID = 1080190087310090241;

        public RTNominationsForum() {}

        private async Task<SocketThreadChannel?> ReturnOrUnarchiveThread(string id) {
            var guild = BotService.Client.GetGuild(BotService.BLServerID);
            ulong longId = ulong.Parse(id); 
            var channel = guild.GetThreadChannel(longId);
            
            if (channel == null) {

                var ctor = typeof(SocketThreadChannel).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).FirstOrDefault();

                channel = (SocketThreadChannel)ctor.Invoke(new object[5] { BotService.Client, guild, longId, guild.GetForumChannel(NominationForumID), null });
                
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

        private string NominationMessage(Leaderboard leaderboard) {
            string message = "";
            message += $"**{leaderboard.Song.Name}** | {leaderboard.Difficulty.DifficultyName} \n";
            message += "\n";
            message += "https://beatleader.xyz/leaderboard/global/" + leaderboard.Id;

            return message;
        }

        public async Task<string> OpenNomination(Player mapper, Leaderboard leaderboard) {
            var guild = BotService.Client.GetGuild(BotService.BLServerID);

            var ForumChannel = guild.GetForumChannel(NominationForumID);
            var post = await ForumChannel.CreatePostAsync($"{leaderboard.Song.Name} - {mapper.Name}", ThreadArchiveDuration.OneWeek, null, NominationMessage(leaderboard), embeds: new Embed []{ 
                new EmbedBuilder()
                    .WithThumbnailUrl(leaderboard.Song.CoverImage)
                    .WithTitle("Leaderboard")
                    .WithUrl("https://beatleader.xyz/leaderboard/global/" + leaderboard.Id)
                    .Build()
            },
            
            tags: ForumChannel.Tags.Where(t => t.Name == "Nominated" || t.Name == leaderboard.Difficulty.DifficultyName).ToArray());

            var discord = mapper.Socials?.FirstOrDefault(s => s.Service == "Discord");
            if (discord != null)
            {
                try {
                    ulong discordId = ulong.Parse(discord.UserId); 
                    var user = await ((IGuild)BotService.Client.GetGuild(BotService.BLServerID)).GetUserAsync(discordId, CacheMode.AllowDownload);
                    
                    await ForumChannel.AddPermissionOverwriteAsync(user, new OverwritePermissions(viewChannel: PermValue.Allow));
                    await post.AddUserAsync(user);
                } catch { }
            }

            return post.Id.ToString();
        }

        public async Task CloseNomination(string id) {
            var channel = await ReturnOrUnarchiveThread(id);
            if (channel != null) {
                await channel.SendMessageAsync("**UN-NOMINATED**");
                await channel.ModifyAsync(props => {
                    props.Archived = true;
                    props.AppliedTags = new List<ulong>();
                });
            }
        }

        public async Task NominationQualified(string id) {
            var ForumChannel = BotService.Client.GetGuild(BotService.BLServerID).GetForumChannel(NominationForumID);

            var channel = await ReturnOrUnarchiveThread(id);
            if (channel != null) {
                await channel.SendMessageAsync("**QUALIFIED**");
                await channel.ModifyAsync(props => {
                    props.AppliedTags = new List<ulong> { ForumChannel.Tags.First(t => t.Name == "Qualified").Id };
                });
            }
        }

        public async Task NominationReuploaded(
            AppContext context,
            RankQualification qualification, 
            string newLeaderboardId) {
            var channel = await ReturnOrUnarchiveThread(qualification.DiscordRTChannelId);

            if (channel != null) {
                string message = "**REUPLOADED**";

                var criteriaCheckers = context.RankQualification
                    .Where(lb => lb.Id == qualification.Id)
                    .Include(q => q.CriteriaComments)
                    .Select(lb => lb.CriteriaComments.Select(v => v.PlayerId))
                    .FirstOrDefault()
                    ?.ToList() ?? new List<string>();

                if (qualification.CriteriaChecker != null) {
                    criteriaCheckers.Add(qualification.CriteriaChecker);
                }

                if (criteriaCheckers.Count > 0) {
                    bool pings = false;
                    foreach (var playerid in criteriaCheckers.Distinct())
                    {
                        var discord = context.PlayerSocial.Where(s => s.PlayerId == playerid && s.Service == "Discord").FirstOrDefault();
                        if (discord != null)
                        {
                            try {
                                ulong discordId = ulong.Parse(discord.UserId); 
                                message += $" <@{discordId}>";
                                pings = true;
                            } catch { }
                        }
                    }
                    
                    if (pings) {
                        message += "<a:wavege:1069819816581546057>";
                    }
                }
                await channel.SendMessageAsync(message, embeds: new Embed []{ 
                new EmbedBuilder()
                    .WithTitle("Leaderboard")
                    .WithUrl("https://beatleader.xyz/leaderboard/global/" + newLeaderboardId)
                    .Build()
            });
            }
        }

        public async Task NominationRanked(string id) {
            var ForumChannel = BotService.Client.GetGuild(BotService.BLServerID).GetForumChannel(NominationForumID);

            var channel = await ReturnOrUnarchiveThread(id);
            if (channel != null) {
                await channel.SendMessageAsync("**RANKED <a:saberege:961310724787929168> **");
                await channel.ModifyAsync(props => {
                    props.AppliedTags = new List<ulong> { ForumChannel.Tags.First(t => t.Name == "Ranked").Id };
                });
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

        public async Task<string> VoteAdded(string forum, Player player, MapQuality vote) {
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
                } catch { }
            }

            return (await channel.SendMessageAsync("New vote from " + playername + ": **" + vote.ToString() + "!**",
                allowedMentions: new AllowedMentions { UserIds = new List<ulong>() })).Id.ToString();
        }

        public async Task VoteRemoved(string forum, string id) {
            var channel = await ReturnOrUnarchiveThread(forum);
            if (channel == null) {
                return;
            }
            await channel.DeleteMessageAsync(ulong.Parse(id));
        }
    }
}

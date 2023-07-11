using BeatLeader_Server.Models;
using CoreHtmlToImage;
using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Reflection;

namespace BeatLeader_Server.Bot
{
    public class NominationsForum
    {
        public const string PositiveEmoteName = "<:pepeYes:970735414325432480>";
        public const string NeutralEmoteName = "<:shrugge:1069820114326786079>";
        public const string NegativeEmoteName = "<:pepeNo:923357263157166110>";

        public const string AgreeEmoteName = "<:Okayge:934225600896438292>";

        public const ulong NominationForumID = 1075436060139597886;

        public const ulong ReviewHubForumID = 1034817071894237194;
        public const ulong NQTRoleId = 1064783598206599258;

        public const ulong ReviewSeekerMessageID = 1115451888096264193;
        public const ulong ReviewSeekerRoleId = 1128065797126881280;

        private readonly RTNominationsForum _rtNominationsForum;

        public NominationsForum(RTNominationsForum rtNominationsForum) {
            _rtNominationsForum = rtNominationsForum;
        }

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

        private async Task AddVoteEmotes(IMessage message) {
            await message.AddReactionAsync(Emote.Parse(PositiveEmoteName));
            await message.AddReactionAsync(Emote.Parse(NeutralEmoteName));
            await message.AddReactionAsync(Emote.Parse(NegativeEmoteName));
        }

        private string NominationMessage(string playerName, Leaderboard leaderboard) {
            string message = "";
            message += " **" + playerName + "** nominated **" + leaderboard.Difficulty.DifficultyName + "** diff of **" + leaderboard.Song.Name + "**! \n";
            message += "\n";
            message += "https://beatleader.xyz/leaderboard/global/" + leaderboard.Id;

            return message;
        }

        public async Task<string> OpenNomination(string playerName, Leaderboard leaderboard) {
            var guild = BotService.Client.GetGuild(BotService.BLServerID);

            var ForumChannel = guild.GetForumChannel(NominationForumID);
            var post = await ForumChannel.CreatePostAsync(leaderboard.Song.Name, ThreadArchiveDuration.OneWeek, null, NominationMessage(playerName, leaderboard), embeds: new Embed []{ 
                new EmbedBuilder()
                    .WithThumbnailUrl(leaderboard.Song.CoverImage)
                    .WithTitle("Leaderboard")
                    .WithUrl("https://beatleader.xyz/leaderboard/global/" + leaderboard.Id)
                    .Build()
            },
            tags: ForumChannel.Tags.Where(t => t.Name == "Nominated").ToArray());

            await AddVoteEmotes(await post.GetMessageAsync(post.Id));

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
            var channel = await ReturnOrUnarchiveThread(qualification.DiscordChannelId);
            if (channel != null) {
                string message = "**REUPLOADED**";

                var voters = context.RankQualification
                    .Where(lb => lb.Id == qualification.Id)
                    .Include(q => q.Votes)
                    .Select(lb => lb.Votes.Select(v => v.PlayerId))
                    .FirstOrDefault()?.ToList() ?? new List<string>();

                if (voters.Count > 0) {
                    bool pings = false;
                    foreach (var playerid in voters.Distinct())
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
                    props.Archived = true;
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

        private MapQuality? EmoteNameToVote(string emoteName) {
            switch (emoteName)
            {
                case PositiveEmoteName: return MapQuality.Good;
                case NeutralEmoteName: return MapQuality.Ok;
                case NegativeEmoteName: return MapQuality.Bad;
            }

            return null;
        }

        private string VoteToEmoteName(MapQuality quality) {
            switch (quality)
            {
                case MapQuality.Good: return PositiveEmoteName;
                case MapQuality.Ok: return NeutralEmoteName;
                case MapQuality.Bad: return NegativeEmoteName;
            }

            return "";
        }

        public async Task<List<QualificationVote>> AddVote(
            AppContext context,
            RankQualification qualification, 
            Player player,
            MapQuality vote)
        {
            Leaderboard leaderboard = await context
                .Leaderboards
                .Include(lb => lb.Difficulty)
                .Include(lb => lb.Song)
                .FirstAsync(lb => lb.Qualification == qualification);

            if (qualification.Votes == null)
            {
                qualification.Votes = new List<QualificationVote>();
            }

            var qualificationVote = qualification.Votes.FirstOrDefault(v => v.PlayerId == player.Id);
            if (qualificationVote == null) {
                qualificationVote = new QualificationVote
                {
                    PlayerId = player.Id,
                    Timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds
                };
                qualification.Votes.Add(qualificationVote);
            }
            else if (qualificationVote.Value == vote)
            {
                if (qualificationVote.Value == MapQuality.Good) {
                    qualification.QualityVote--;
                    leaderboard.PositiveVotes -= 8;
                } else if (qualificationVote.Value == MapQuality.Bad) {
                    qualification.QualityVote++;
                    leaderboard.NegativeVotes -= 8;
                }
                qualification.Votes.Remove(qualificationVote);
                if (qualificationVote.DiscordRTMessageId != null) {
                    await _rtNominationsForum.VoteRemoved(qualification.DiscordRTChannelId, qualificationVote.DiscordRTMessageId);
                }
                qualificationVote = null;
            }
            else
            {
                if (qualificationVote.Value == MapQuality.Good) {
                    qualification.QualityVote--;
                    leaderboard.PositiveVotes -= 8;
                } else if (qualificationVote.Value == MapQuality.Bad) {
                    qualification.QualityVote++;
                    leaderboard.NegativeVotes -= 8;
                }
                qualificationVote.Edited = true;
                qualificationVote.EditTimeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
                if (qualificationVote.DiscordRTMessageId != null) {
                    await _rtNominationsForum.VoteRemoved(qualification.DiscordRTChannelId, qualificationVote.DiscordRTMessageId);
                }
            }

            if (qualificationVote != null) {
                qualificationVote.Value = vote;

                if (vote == MapQuality.Good) {
                    qualification.QualityVote++;
                    leaderboard.PositiveVotes += 8;
                } else if (vote == MapQuality.Bad) {
                    qualification.QualityVote--;
                    leaderboard.NegativeVotes += 8;
                }

                if (qualification.DiscordRTChannelId.Length > 0) {
                    qualificationVote.DiscordRTMessageId = await _rtNominationsForum.VoteAdded(qualification.DiscordRTChannelId, player, vote);
                }
            }

            context.SaveChanges();

            try
            {
                await UpdateVoteResults(context, leaderboard);
            }
            catch {}

            return qualification.Votes.ToList();
        }

        public async Task UpdateVoteResults(AppContext context, Leaderboard leaderboard)
        {
            var forum = leaderboard.Qualification?.DiscordChannelId;
            if (forum == null || forum.Length == 0) {
                return;
            }
            var channel = await ReturnOrUnarchiveThread(forum);
            if (channel == null)
            {
                return;
            }

            var message = await channel.GetMessageAsync(ulong.Parse(forum));
            if (message is IUserMessage userMessage)
            {
                await message.RemoveAllReactionsAsync();
                var nominatorName = context.Players.Where(p => p.Id == leaderboard.Qualification.RTMember).Select(p => p.Name).FirstOrDefault();
                string messageText = NominationMessage(nominatorName ?? "", leaderboard);
                if (leaderboard.Qualification.Votes != null && leaderboard.Qualification.Votes.Count > 0) {
                    messageText += "\n\n**VOTES:**\n";
                    foreach (var vote in leaderboard.Qualification.Votes.OrderBy(v => v.Value))
                    {
                        var voter = context.Players.Include(p => p.Socials).Where(p => p.Id == vote.PlayerId).FirstOrDefault();
                        if (voter != null) {
                            string playername = voter.Name;
                            var discord = voter.Socials?.FirstOrDefault(s => s.Service == "Discord");
                            if (discord != null)
                            {
                                try {
                                    ulong discordId = ulong.Parse(discord.UserId); 
                                    playername = $"<@{discordId}>";
                                } catch { }
                            }

                            messageText += playername + "  " + VoteToEmoteName(vote.Value) + "\n";
                        }
                    }
                }
                await userMessage.ModifyAsync(m => m.Content = messageText);

                var totalValue = leaderboard.Qualification.QualityVote;
                string votePrefix = "";
                if (totalValue != 0) {
                    votePrefix = "(" + (totalValue > 0 ? "+" : "") + totalValue + ") "; 
                }
                await channel.ModifyAsync(c => c.Topic = votePrefix + leaderboard.Song.Name);
                await AddVoteEmotes(message);
            }
        }

        public async Task<RankQualification?> OnReactionAdded(
            AppContext context,
            Cacheable<IUserMessage, ulong> message, 
            Cacheable<IMessageChannel, ulong> channel, 
            SocketReaction reaction)
        {
            SocketThreadChannel? thread;
            if ((thread = await channel.GetOrDownloadAsync() as SocketThreadChannel) != null) {
                if (ReviewHubForumID == thread.ParentChannel.Id) {
                    ulong? userId = reaction.User.GetValueOrDefault()?.Id;
                    if (userId != null) {
                        var user = await ((IGuild)BotService.Client.GetGuild(BotService.BLServerID)).GetUserAsync(userId ?? 0, CacheMode.AllowDownload);
                        if (message.Id == ReviewSeekerMessageID) {
                            try {
                                if (reaction.Emote.ToString() == AgreeEmoteName && !user.RoleIds.Contains(ReviewSeekerRoleId)) {
                                    await user.AddRoleAsync(ReviewSeekerRoleId);
                                }
                            } catch (Exception ex) {
                            }
                        } else {
                            if (!user.RoleIds.Contains(NQTRoleId)) {
                                var fullmessage = await thread.GetMessageAsync(message.Id);
                                await fullmessage.RemoveReactionAsync(reaction.Emote, user);
                            }
                        }
                    }
                    return null;
                }
            }

            var vote = EmoteNameToVote(reaction.Emote.ToString() ?? "");
            if (vote != null) {
                var qualification = await context.RankQualification.Include(q => q.Votes).FirstOrDefaultAsync(q => q.DiscordChannelId == message.Id.ToString());
                if (qualification == null) return null;

                var social = await context.PlayerSocial.FirstOrDefaultAsync(p => p.UserId == reaction.UserId.ToString() && p.Service == "Discord" && p.PlayerId != null);
                if (social == null) return null;

                var player = await context.Players.Include(p => p.Socials).FirstOrDefaultAsync(p => p.Socials.FirstOrDefault(s => s.Id == social.Id) != null);
                if (player == null) return null;

                await AddVote(context, qualification, player, vote ?? MapQuality.Ok);

                return qualification;
            }

            return null;
        }

        public async Task OnReactionRemoved(
            AppContext context,
            Cacheable<IUserMessage, ulong> message, 
            Cacheable<IMessageChannel, ulong> channel, 
            SocketReaction reaction)
        {
            SocketThreadChannel? thread;
            if ((thread = await channel.GetOrDownloadAsync() as SocketThreadChannel) != null) {
                if (ReviewHubForumID == thread.ParentChannel.Id) {
                    ulong? userId = reaction.User.GetValueOrDefault()?.Id;
                    if (userId != null) {
                        var user = await ((IGuild)BotService.Client.GetGuild(BotService.BLServerID)).GetUserAsync(userId ?? 0, CacheMode.AllowDownload);
                        if (message.Id == ReviewSeekerMessageID) {
                            try {
                                if (reaction.Emote.ToString() == AgreeEmoteName && user.RoleIds.Contains(ReviewSeekerRoleId)) {
                                    await user.RemoveRoleAsync(ReviewSeekerRoleId);
                                }
                            } catch (Exception ex) {
                            }
                        }
                    }
                }
            }
        }
    }
}

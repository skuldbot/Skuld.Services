using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Skuld.Core;
using Skuld.Core.Extensions;
using Skuld.Core.Extensions.Verification;
using Skuld.Core.Utilities;
using Skuld.Models;
using Skuld.Services.BotListing;
using Skuld.Services.Guilds.Pinning;
using Skuld.Services.Guilds.Starboard;
using StatsdClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Skuld.Services.Bot
{
    internal static class BotEvents
    {
        public const string Key = "DiscordLog";
        private static readonly List<int> ShardsReady = new List<int>();

        //DiscordLoging
        public static void RegisterEvents()
        {
            /*All Events needed for running Skuld*/
            BotService.DiscordClient.ShardConnected += Bot_ShardConnected;
            BotService.DiscordClient.ShardDisconnected += Bot_ShardDisconnected;
            BotService.DiscordClient.ShardReady += Bot_ShardReady;
        }

        public static void UnRegisterEvents()
        {
            BotService.DiscordClient.ShardConnected -= Bot_ShardConnected;
            BotService.DiscordClient.ShardDisconnected -= Bot_ShardDisconnected;
            BotService.DiscordClient.ShardReady -= Bot_ShardReady;

            foreach (var shard in BotService.DiscordClient.Shards)
            {
                shard.MessageReceived -= BotMessaging.HandleMessageAsync;
                shard.MessagesBulkDeleted -= Shard_MessagesBulkDeleted;
                shard.MessageDeleted -= Shard_MessageDeleted;
                shard.JoinedGuild -= Bot_JoinedGuild;
                shard.RoleDeleted -= Bot_RoleDeleted;
                shard.GuildMemberUpdated -= Bot_GuildMemberUpdated;
                shard.LeftGuild -= Bot_LeftGuild;
                shard.UserJoined -= Bot_UserJoined;
                shard.UserLeft -= Bot_UserLeft;
                shard.ReactionAdded -= Bot_ReactionAdded;
                shard.ReactionRemoved -= Bot_ReactionRemoved;
                shard.ReactionsCleared -= Bot_ReactionsCleared;
                shard.Log -= Bot_Log;
                shard.UserUpdated -= Bot_UserUpdated;
                shard.GuildUpdated -= Bot_GuildUpdated;
            }
        }

        private static Task Bot_Log(LogMessage arg)
        {
            var key = $"{Key} - {arg.Source}";
            switch (arg.Severity)
            {
                case LogSeverity.Info:
                    Log.Info(key, arg.Message);
                    break;

                case LogSeverity.Critical:
                    Log.Critical(key, arg.Message, null, arg.Exception);
                    break;

                case LogSeverity.Warning:
                    Log.Warning(key, arg.Message, null, arg.Exception);
                    break;

                case LogSeverity.Verbose:
                    Log.Verbose(key, arg.Message, null, arg.Exception);
                    break;

                case LogSeverity.Error:
                    Log.Error(key, arg.Message, null, arg.Exception);
                    break;

                case LogSeverity.Debug:
                    Log.Debug(key, arg.Message, null, arg.Exception);
                    break;

                default:
                    break;
            }
            return Task.CompletedTask;
        }

        private static async Task Shard_MessageDeleted(
            Cacheable<IMessage, ulong> arg1,
            ISocketMessageChannel arg2
        )
        {
            using var Database = new SkuldDbContextFactory().CreateDbContext();

            if (arg2 is IGuildChannel guildChannel)
            {
                var gld = await Database.InsertOrGetGuildAsync(guildChannel.Guild).ConfigureAwait(false);
                var feats = Database.Features.FirstOrDefault(x => x.Id == guildChannel.GuildId);

                if (feats.Starboard && gld.StarDeleteIfSourceDelete)
                {
                    var message = await arg1.GetOrDownloadAsync().ConfigureAwait(false);

                    if (Database.StarboardVotes.Any(x => x.MessageId == message.Id))
                    {
                        var vote = Database.StarboardVotes.FirstOrDefault(x => x.MessageId == message.Id);

                        Database.StarboardVotes.RemoveRange(Database.StarboardVotes.ToList().Where(x => x.MessageId == message.Id));

                        var chan = await guildChannel.Guild.GetTextChannelAsync(gld.StarboardChannel).ConfigureAwait(false);
                        var starMessage = await chan.GetMessageAsync(vote.StarboardMessageId).ConfigureAwait(false);

                        await starMessage.DeleteAsync().ConfigureAwait(false);

                        await Database.SaveChangesAsync().ConfigureAwait(false);
                    }
                }
            }
        }

        private static async Task Shard_MessagesBulkDeleted(
            IReadOnlyCollection<Cacheable<IMessage, ulong>> arg1,
            ISocketMessageChannel arg2
        )
        {
            using var Database = new SkuldDbContextFactory().CreateDbContext();
            
            if(arg2 is IGuildChannel guildChannel)
            {
                var gld = await Database.InsertOrGetGuildAsync(guildChannel.Guild).ConfigureAwait(false);
                var feats = Database.Features.FirstOrDefault(x => x.Id == guildChannel.GuildId);

                if(feats.Starboard && gld.StarDeleteIfSourceDelete)
                {
                    foreach (var msg in arg1)
                    {
                        var message = await msg.GetOrDownloadAsync().ConfigureAwait(false);
                        if (Database.StarboardVotes.Any(x => x.MessageId == message.Id))
                        {
                            var vote = Database.StarboardVotes.FirstOrDefault(x => x.MessageId == message.Id);

                            Database.StarboardVotes.RemoveRange(Database.StarboardVotes.ToList().Where(x => x.MessageId == message.Id));

                            var chan = await guildChannel.Guild.GetTextChannelAsync(gld.StarboardChannel).ConfigureAwait(false);
                            var starMessage = await chan.GetMessageAsync(vote.StarboardMessageId).ConfigureAwait(false);

                            await starMessage.DeleteAsync().ConfigureAwait(false);

                            await Database.SaveChangesAsync().ConfigureAwait(false);
                        }
                    }
                }
            }
        }

        #region Reactions

        private static async Task Bot_ReactionAdded(Cacheable<IUserMessage, ulong> arg1, ISocketMessageChannel arg2, SocketReaction arg3)
        {
            DogStatsd.Increment("messages.reactions.added");
            IUser usr;

            var msg = await arg1.GetOrDownloadAsync().ConfigureAwait(false);

            if (msg == null) return;

            ShardedCommandContext context = new ShardedCommandContext(
                BotService.DiscordClient,
                msg as SocketUserMessage
            );

            if (!arg3.User.IsSpecified) return;
            else
            {
                usr = arg3.User.Value;
            }

            if (usr.IsBot || usr.IsWebhook) return;

            if (arg2 is IGuildChannel)
            {
                await PinningService.ExecuteAdditionAsync(context.Message, arg2, arg3).ConfigureAwait(false);

                await StarboardService.ExecuteAdditionAsync(context.Message, arg2, arg3).ConfigureAwait(false);
            }

            if (arg2.Id == BotService.Configuration.IssueChannel)
            {
                using var Database = new SkuldDbContextFactory().CreateDbContext();

                var user = await Database.InsertOrGetUserAsync(usr).ConfigureAwait(false);

                if (user.Flags.IsBitSet(DiscordUtilities.BotCreator))
                {
                    try
                    {
                        if (arg1.HasValue)
                        {
                            var message = Database.Issues.FirstOrDefault(x => x.IssueChannelMessageId == arg1.Id);
                            if (message != null)
                            {
                                var emote = arg3.Emote as Emote;

                                if (emote.Id == DiscordUtilities.Tick_Emote.Id)
                                {
                                    if (!message.HasSent)
                                    {
                                        try
                                        {
                                            var newissue = new Octokit.NewIssue(message.Title)
                                            {
                                                Body = message.Body
                                            };

                                            newissue.Assignees.Add("exsersewo");
                                            newissue.Labels.Add("From Command");

                                            var issue = await BotService.Services.GetRequiredService<Octokit.GitHubClient>().Issue.Create(BotService.Configuration.GithubRepository, newissue).ConfigureAwait(false);

                                            try
                                            {
                                                await BotService.DiscordClient.GetUser(message.SubmitterId).SendMessageAsync("", false,
                                                    new EmbedBuilder()
                                                        .WithTitle("Good News!")
                                                        .AddAuthor(BotService.DiscordClient)
                                                        .WithDescription($"Your issue:\n\"[{newissue.Title}]({issue.HtmlUrl})\"\n\nhas been accepted")
                                                        .WithColor(EmbedExtensions.RandomEmbedColor())
                                                    .Build()
                                                );
                                            }
                                            catch { }

                                            await msg.ModifyAsync(x =>
                                            {
                                                x.Embed = msg.Embeds.ElementAt(0)
                                                .ToEmbedBuilder()
                                                .AddField("Sent", DiscordUtilities.Tick_Emote.ToString())
                                                .Build();
                                            }).ConfigureAwait(false);

                                            message.HasSent = true;

                                            await Database.SaveChangesAsync().ConfigureAwait(false);
                                        }
                                        catch (Exception ex)
                                        {
                                            Log.Error(
                                                "Git-" + SkuldAppContext.GetCaller(),
                                                ex.Message,
                                                context,
                                                ex
                                            );
                                        }
                                    }
                                }
                                else if (emote.Id == DiscordUtilities.Cross_Emote.Id)
                                {
                                    Database.Issues.Remove(message);

                                    await Database.SaveChangesAsync().ConfigureAwait(false);

                                    await msg.DeleteAsync().ConfigureAwait(false);

                                    try
                                    {
                                        await BotService.DiscordClient.GetUser(message.SubmitterId).SendMessageAsync("", false,
                                            new EmbedBuilder()
                                                .WithTitle("Bad News")
                                                .AddAuthor(BotService.DiscordClient)
                                                .WithDescription($"Your issue:\n\"{message.Title}\"\n\nhas been declined. If you would like to know why, send: {arg3.User.Value.FullName()} a message")
                                                .WithColor(EmbedExtensions.RandomEmbedColor())
                                            .Build()
                                        );
                                    }
                                    catch { }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Critical(Key, 
                            ex.Message,
                            context,
                            ex
                        );
                    }
                }
            }
        }

        private static async Task Bot_ReactionsCleared(Cacheable<IUserMessage, ulong> arg1, ISocketMessageChannel arg2)
        {
            DogStatsd.Increment("messages.reactions.cleared");
        }

        private static async Task Bot_ReactionRemoved(Cacheable<IUserMessage, ulong> arg1, ISocketMessageChannel arg2, SocketReaction arg3)
        {
            DogStatsd.Increment("messages.reactions.removed");

            IUser usr;

            if (!arg3.User.IsSpecified) return;
            else
            {
                usr = arg3.User.Value;
            }

            if (usr.IsBot || usr.IsWebhook) return;

            var message = await arg1.GetOrDownloadAsync().ConfigureAwait(false);

            await StarboardService.ExecuteRemovalAsync(message, usr.Id).ConfigureAwait(false);
        }

        #endregion Reactions

        #region Shards

        private static async Task Bot_ShardReady(DiscordSocketClient arg)
        {
            if (!ShardsReady.Contains(arg.ShardId))
            {
                arg.MessageReceived += BotMessaging.HandleMessageAsync;
                arg.MessageDeleted += Shard_MessageDeleted;
                arg.MessagesBulkDeleted += Shard_MessagesBulkDeleted;
                arg.JoinedGuild += Bot_JoinedGuild;
                arg.RoleDeleted += Bot_RoleDeleted;
                arg.GuildMemberUpdated += Bot_GuildMemberUpdated;
                arg.LeftGuild += Bot_LeftGuild;
                arg.UserJoined += Bot_UserJoined;
                arg.UserLeft += Bot_UserLeft;
                arg.ReactionAdded += Bot_ReactionAdded;
                arg.ReactionRemoved += Bot_ReactionRemoved;
                arg.ReactionsCleared += Bot_ReactionsCleared;
                arg.Log += Bot_Log;
                arg.UserUpdated += Bot_UserUpdated;
                arg.GuildUpdated += Bot_GuildUpdated;
                ShardsReady.Add(arg.ShardId);
            }

            await 
                arg
                .SetGameAsync($"{BotService.Configuration.Prefix}help | {arg.ShardId + 1}/{BotService.DiscordClient.Shards.Count}", 
                type: ActivityType.Listening
            ).ConfigureAwait(false);

            Log.Info($"Shard #{arg.ShardId}", "Shard Ready");
        }

        private static async Task Bot_ShardConnected(DiscordSocketClient arg)
        {
            await arg.SetGameAsync($"{BotService.Configuration.Prefix}help | {arg.ShardId + 1}/{BotService.DiscordClient.Shards.Count}", type: ActivityType.Listening).ConfigureAwait(false);
            DogStatsd.Event("shards.connected", $"Shard {arg.ShardId} Connected", alertType: "info");
        }

        private static Task Bot_ShardDisconnected(
            Exception arg1,
            DiscordSocketClient arg2
        )
        {
            DogStatsd.Event($"Shard.disconnected", $"Shard {arg2.ShardId} Disconnected, error: {arg1}", alertType: "error");
            return Task.CompletedTask;
        }

        #endregion Shards

        #region Users

        private static async Task Bot_UserJoined(SocketGuildUser arg)
        {
            DogStatsd.Increment("guild.users.joined");

            //Insert into Database
            {
                using SkuldDbContext database = new SkuldDbContextFactory().CreateDbContext();

                await database.InsertOrGetUserAsync(arg as IUser).ConfigureAwait(false);
            }

            //Persistent Roles
            {
                using SkuldDbContext database = new SkuldDbContextFactory().CreateDbContext();

                if (database.PersistentRoles.ToList().Any(x => x.UserId == arg.Id && x.GuildId == arg.Guild.Id))
                {
                    foreach (var persistentRole in database.PersistentRoles.ToList().Where(x => x.UserId == arg.Id && x.GuildId == arg.Guild.Id))
                    {
                        try
                        {
                            await arg.AddRoleAsync(arg.Guild.GetRole(persistentRole.RoleId)).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            Log.Error("UsrJoin", ex.Message, null, ex);
                        }
                    }
                }
            }

            //Join Message
            {
                using SkuldDbContext database = new SkuldDbContextFactory().CreateDbContext();

                var gld = await database.InsertOrGetGuildAsync(arg.Guild).ConfigureAwait(false);

                if (gld != null)
                {
                    if (gld.JoinRole != 0)
                    {
                        var joinrole = arg.Guild.GetRole(gld.JoinRole);
                        await arg.AddRoleAsync(joinrole).ConfigureAwait(false);
                    }

                    if (gld.JoinChannel != 0 && !string.IsNullOrEmpty(gld.JoinMessage))
                    {
                        var channel = arg.Guild.GetTextChannel(gld.JoinChannel);
                        var message = gld.JoinMessage.ReplaceGuildEventMessage(arg as IUser, arg.Guild as SocketGuild);
                        await channel.SendMessageAsync(message).ConfigureAwait(false);
                    }
                }
            }

            //Experience Roles
            {
                using SkuldDbContext database = new SkuldDbContextFactory().CreateDbContext();

                var feats = database.Features.FirstOrDefault(x => x.Id == arg.Guild.Id);

                if(feats.Experience)
                {
                    var rewards = database.LevelRewards.ToList().Where(x => x.GuildId == arg.Guild.Id);

                    var usrLvl = database.UserXp.FirstOrDefault(x => x.GuildId == arg.Guild.Id && x.UserId == arg.Id);

                    var rolesToGive = rewards.Where(x => x.LevelRequired <= usrLvl.Level).Select(z=>z.RoleId);

                    if(feats.StackingRoles)
                    {
                        var roles = arg.Guild.Roles.Where(z => rolesToGive.Contains(z.Id));

                        try
                        {
                            await arg.AddRolesAsync(roles).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            Log.Error("UsrJoin", ex.Message, null, ex);
                        }
                    }
                    else
                    {
                        var r = rewards
                            .Where(x => x.LevelRequired <= usrLvl.Level)
                            .OrderByDescending(x => x.LevelRequired)
                            .FirstOrDefault();

                        var role = arg.Guild.Roles.FirstOrDefault(z => rolesToGive.Contains(r.Id));

                        try
                        {
                            await arg.AddRoleAsync(role).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            Log.Error("UsrJoin", ex.Message, null, ex);
                        }
                    }
                }
            }

            Log.Verbose(Key, $"{arg} joined {arg.Guild}", null);
        }

        private static async Task Bot_UserLeft(SocketGuildUser arg)
        {
            DogStatsd.Increment("guild.users.left");

            using var db = new SkuldDbContextFactory().CreateDbContext();

            var gld = await db.InsertOrGetGuildAsync(arg.Guild).ConfigureAwait(false);

            if (gld != null)
            {
                if (gld.LeaveChannel != 0 && !string.IsNullOrEmpty(gld.LeaveMessage))
                {
                    var channel = arg.Guild.GetTextChannel(gld.JoinChannel);
                    var message = gld.LeaveMessage.ReplaceGuildEventMessage(arg as IUser, arg.Guild as SocketGuild);
                    await channel.SendMessageAsync(message).ConfigureAwait(false);
                }
            }

            Log.Verbose(Key, $"{arg} left {arg.Guild}", null);
        }

        private static async Task Bot_UserUpdated(
            SocketUser arg1,
            SocketUser arg2
        )
        {
            if (arg1.IsBot || arg1.IsWebhook) return;

            {
                using SkuldDbContext database = new SkuldDbContextFactory().CreateDbContext();

                User suser = await database.InsertOrGetUserAsync(arg2).ConfigureAwait(false);

                if (!suser.IsUpToDate(arg2))
                {
                    suser.AvatarUrl = arg2.GetAvatarUrl() ?? arg2.GetDefaultAvatarUrl();
                    suser.Username = arg2.Username;
                    await database.SaveChangesAsync().ConfigureAwait(false);
                }
            }
        }

        #endregion Users

        #region Guilds

        private static async Task Bot_LeftGuild(SocketGuild arg)
        {
            using var database = new SkuldDbContextFactory().CreateDbContext();

            DogStatsd.Increment("guilds.left");

            await BotService.DiscordClient.SendDataAsync(BotService.Configuration.IsDevelopmentBuild, BotService.Configuration.DiscordGGKey, BotService.Configuration.DBotsOrgKey, BotService.Configuration.B4DToken).ConfigureAwait(false);

            //MessageQueue.CheckForEmptyGuilds = true;

            Log.Verbose(Key, $"Just left {arg}", null);
        }

        private static async Task Bot_JoinedGuild(SocketGuild arg)
        {
            using var database = new SkuldDbContextFactory().CreateDbContext();

            DogStatsd.Increment("guilds.joined");

            await BotService.DiscordClient.SendDataAsync(BotService.Configuration.IsDevelopmentBuild, BotService.Configuration.DiscordGGKey, BotService.Configuration.DBotsOrgKey, BotService.Configuration.B4DToken).ConfigureAwait(false);

            await database.InsertOrGetGuildAsync(arg, BotService.Configuration.Prefix, BotService.MessageServiceConfig.MoneyName, BotService.MessageServiceConfig.MoneyIcon).ConfigureAwait(false);

            //MessageQueue.CheckForEmptyGuilds = true;
            Log.Verbose(Key, $"Just left {arg}", null);
        }

        private static async Task Bot_RoleDeleted(SocketRole arg)
        {
            DogStatsd.Increment("guilds.role.deleted");

            #region LevelRewards

            {
                using var database = new SkuldDbContextFactory().CreateDbContext();

                if (database.LevelRewards.Any(x => x.RoleId == arg.Id))
                {
                    database.LevelRewards.RemoveRange(database.LevelRewards.AsQueryable().Where(x => x.RoleId == arg.Id));

                    await database.SaveChangesAsync().ConfigureAwait(false);
                }
            }

            #endregion LevelRewards

            #region PersistentRoles

            {
                using var database = new SkuldDbContextFactory().CreateDbContext();

                if (database.PersistentRoles.Any(x => x.RoleId == arg.Id))
                {
                    database.PersistentRoles.RemoveRange(database.PersistentRoles.AsQueryable().Where(x => x.RoleId == arg.Id));

                    await database.SaveChangesAsync().ConfigureAwait(false);
                }
            }

            #endregion PersistentRoles

            #region IAmRoles

            {
                using var database = new SkuldDbContextFactory().CreateDbContext();

                if (database.IAmRoles.Any(x => x.RoleId == arg.Id))
                {
                    database.IAmRoles.RemoveRange(database.IAmRoles.AsQueryable().Where(x => x.RoleId == arg.Id));

                    await database.SaveChangesAsync().ConfigureAwait(false);
                }
            }

            {
                using var database = new SkuldDbContextFactory().CreateDbContext();

                if (database.IAmRoles.Any(x => x.RequiredRoleId == arg.Id))
                {
                    foreach (var role in database.IAmRoles.AsQueryable().Where(x => x.RequiredRoleId == arg.Id))
                    {
                        role.RequiredRoleId = 0;
                    }

                    await database.SaveChangesAsync().ConfigureAwait(false);
                }
            }

            #endregion IAmRoles

            Log.Verbose(Key, $"{arg} deleted in {arg.Guild}", null);
        }

        private static async Task Bot_GuildMemberUpdated(
            SocketGuildUser arg1,
            SocketGuildUser arg2
        )
        {
            //Resync Data
            {
                if (!arg1.IsBot && !arg1.IsWebhook)
                {
                    using SkuldDbContext database = new SkuldDbContextFactory().CreateDbContext();

                    User suser = await database.InsertOrGetUserAsync(arg2).ConfigureAwait(false);

                    if (!suser.IsUpToDate(arg2))
                    {
                        suser.AvatarUrl = arg2.GetAvatarUrl() ?? arg2.GetDefaultAvatarUrl();
                        suser.Username = arg2.Username;
                        await database.SaveChangesAsync().ConfigureAwait(false);
                    }
                }
            }

            if (arg1.Roles.Count != arg2.Roles.Count)
            {
                //Add Persistent Role
                {
                    using SkuldDbContext database = new SkuldDbContextFactory().CreateDbContext();

                    var guildPersistentRoles = database.PersistentRoles.AsQueryable().Where(x => x.GuildId == arg2.Guild.Id).DistinctBy(x => x.RoleId).ToList();

                    guildPersistentRoles.ForEach(z =>
                    {
                        arg2.Roles.ToList().ForEach(x =>
                        {
                            if (z.RoleId == x.Id)
                            {
                                if (!database.PersistentRoles.Any(y => y.RoleId == z.RoleId && y.UserId == arg2.Id && y.GuildId == arg2.Guild.Id))
                                {
                                    database.PersistentRoles.Add(new PersistentRole
                                    {
                                        GuildId = arg2.Guild.Id,
                                        RoleId = z.RoleId,
                                        UserId = arg2.Id
                                    });
                                }
                            }
                        });
                    });

                    await database.SaveChangesAsync().ConfigureAwait(false);
                }

                //Remove Persistent Role
                {
                    using SkuldDbContext database = new SkuldDbContextFactory().CreateDbContext();

                    IEnumerable<SocketRole> roleDifference;

                    if (arg1.Roles.Count > arg2.Roles.Count)
                    {
                        roleDifference = arg1.Roles.Except(arg2.Roles);
                    }
                    else
                    {
                        roleDifference = arg2.Roles.Except(arg1.Roles);
                    }

                    var guildPersistentRoles = database.PersistentRoles.AsQueryable().Where(x => x.GuildId == arg2.Guild.Id).DistinctBy(x => x.RoleId).ToList();

                    var roles = new List<ulong>();

                    guildPersistentRoles.ForEach(z =>
                    {
                        if(roleDifference.Any(x=>x.Id == z.RoleId))
                        {
                            roles.Add(z.RoleId);
                        }
                    });

                    if (roles.Any())
                    {
                        database.PersistentRoles.RemoveRange(
                            database.PersistentRoles.ToList()
                            .Where(x => roles.Contains(x.RoleId) && x.UserId == arg2.Id && x.GuildId == arg2.Guild.Id)
                        );
                    }

                    await database.SaveChangesAsync().ConfigureAwait(false);
                }
            }
        }

        private static async Task Bot_GuildUpdated(
            SocketGuild arg1,
            SocketGuild arg2
        )
        {
            using SkuldDbContext Database = new SkuldDbContextFactory().CreateDbContext();

            var sguild = await
                Database.InsertOrGetGuildAsync(arg2)
            .ConfigureAwait(false);

            if (sguild.Name == null || !sguild.Name.Equals(arg2.Name))
            {
                sguild.Name = arg2.Name;
            }

            if (sguild.IconUrl == null || !sguild.IconUrl.Equals(arg2.IconUrl))
            {
                sguild.IconUrl = arg2.IconUrl;
            }

            await Database.SaveChangesAsync().ConfigureAwait(false);
        }


        #endregion Guilds
    }
}
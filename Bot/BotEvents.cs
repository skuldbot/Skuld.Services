﻿using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Skuld.Core;
using Skuld.Core.Extensions;
using Skuld.Core.Extensions.Verification;
using Skuld.Core.Models;
using Skuld.Core.Utilities;
using Skuld.Discord.Extensions;
using Skuld.Services.BotListing;
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
                    Log.Critical(key, arg.Message, arg.Exception);
                    break;

                case LogSeverity.Warning:
                    Log.Warning(key, arg.Message, arg.Exception);
                    break;

                case LogSeverity.Verbose:
                    Log.Verbose(key, arg.Message, arg.Exception);
                    break;

                case LogSeverity.Error:
                    Log.Error(key, arg.Message, arg.Exception);
                    break;

                case LogSeverity.Debug:
                    Log.Debug(key, arg.Message, arg.Exception);
                    break;

                default:
                    break;
            }
            return Task.CompletedTask;
        }

        #region Reactions

        private static async Task Bot_ReactionAdded(Cacheable<IUserMessage, ulong> arg1, ISocketMessageChannel arg2, SocketReaction arg3)
        {
            DogStatsd.Increment("messages.reactions.added");

            using var Database = new SkuldDbContextFactory().CreateDbContext();

            User user = null;

            if (arg3.User.IsSpecified)
            {
                var usr = arg3.User.Value;

                if (usr.IsBot || usr.IsWebhook) return;
                user = await Database.GetOrInsertUserAsync(arg3.User.Value).ConfigureAwait(false);
            }

            if (arg2 is IGuildChannel)
            {
                try
                {
                    var guild = BotService.DiscordClient.Guilds.FirstOrDefault(x => x.TextChannels.FirstOrDefault(z => z.Id == arg2.Id) != null);

                    if (guild != null)
                    {
                        var feats = Database.Features.FirstOrDefault(x => x.Id == guild.Id);

                        if (feats.Pinning)
                        {
                            var pins = await arg2.GetPinnedMessagesAsync();

                            if (pins.Count < 50)
                            {
                                var dldedmsg = await arg1.GetOrDownloadAsync();

                                int pinboardThreshold = BotService.Configuration.PinboardThreshold;
                                int pinboardReactions = dldedmsg.Reactions.FirstOrDefault(x => x.Key.Name == "📌").Value.ReactionCount;

                                if (pinboardReactions >= pinboardThreshold)
                                {
                                    var now = dldedmsg.CreatedAt;
                                    var dt = DateTime.UtcNow.AddDays(-BotService.Configuration.PinboardDateLimit);
                                    if ((now - dt).TotalDays > 0)
                                    {
                                        if (!dldedmsg.IsPinned)
                                        {
                                            await dldedmsg.PinAsync();

                                            Log.Info("PinBoard", "Message reached threshold, pinned a message");
                                        }
                                    }
                                }

                                await dldedmsg.Channel.SendMessageAsync(
                                    guild.Owner.Mention,
                                    false,
                                    new EmbedBuilder()
                                        .AddAuthor(BotService.DiscordClient)
                                        .WithDescription($"Can't pin a message in this channel, please clean out some pins.")
                                        .WithColor(Color.Red)
                                        .Build()
                                ).ConfigureAwait(false);
                            }
                        }

                    }
                }
                catch (Exception ex)
                {
                    Log.Critical(Key, ex.Message, ex);
                }
            }

            if (arg2.Id == BotService.Configuration.IssueChannel && user.Flags.IsBitSet(DiscordUtilities.BotCreator))
            {
                try
                {
                    if (arg1.HasValue)
                    {
                        var msg = await arg1.GetOrDownloadAsync().ConfigureAwait(false);

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
                                        Log.Error("Git-" + SkuldAppContext.GetCaller(), ex.Message, ex);
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
                    Log.Critical(Key, ex.Message, ex);
                }
            }
        }

        private static Task Bot_ReactionsCleared(Cacheable<IUserMessage, ulong> arg1, ISocketMessageChannel arg2)
        {
            DogStatsd.Increment("messages.reactions.cleared");
            return Task.CompletedTask;
        }

        private static Task Bot_ReactionRemoved(Cacheable<IUserMessage, ulong> arg1, ISocketMessageChannel arg2, SocketReaction arg3)
        {
            DogStatsd.Increment("messages.reactions.removed");
            return Task.CompletedTask;
        }

        #endregion Reactions

        #region Shards

        private static async Task Bot_ShardReady(DiscordSocketClient arg)
        {
            if (!ShardsReady.Contains(arg.ShardId))
            {
                arg.MessageReceived += BotMessaging.HandleMessageAsync;
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
                ShardsReady.Add(arg.ShardId);
            }

            await arg.SetGameAsync($"{BotService.Configuration.Prefix}help | {arg.ShardId + 1}/{BotService.DiscordClient.Shards.Count}", type: ActivityType.Listening);

            Log.Info($"Shard #{arg.ShardId}", "Shard Ready");
        }

        private static async Task Bot_ShardConnected(DiscordSocketClient arg)
        {
            await arg.SetGameAsync($"{BotService.Configuration.Prefix}help | {arg.ShardId + 1}/{BotService.DiscordClient.Shards.Count}", type: ActivityType.Listening).ConfigureAwait(false);
            DogStatsd.Event("shards.connected", $"Shard {arg.ShardId} Connected", alertType: "info");
        }

        private static Task Bot_ShardDisconnected(Exception arg1, DiscordSocketClient arg2)
        {
            DogStatsd.Event($"Shard.disconnected", $"Shard {arg2.ShardId} Disconnected, error: {arg1}", alertType: "error");
            return Task.CompletedTask;
        }

        #endregion Shards

        #region Users

        private static async Task Bot_UserJoined(SocketGuildUser arg)
        {
            DogStatsd.Increment("guild.users.joined");

            {
                using SkuldDatabaseContext database = new SkuldDbContextFactory().CreateDbContext();

                await database.InsertOrGetUserAsync(arg as IUser).ConfigureAwait(false);
            }

            {
                using SkuldDatabaseContext database = new SkuldDbContextFactory().CreateDbContext();

                if(database.PersistentRoles.ToList().Any(x => x.UserId == arg.Id && x.GuildId == arg.Guild.Id))
                {
                    foreach (var persistentRole in database.PersistentRoles.ToList().Where(x => x.UserId == arg.Id && x.GuildId == arg.Guild.Id))
                    {
                        try
                        {
                            await arg.AddRoleAsync(arg.Guild.GetRole(persistentRole.RoleId)).ConfigureAwait(false);
                        }
                        catch { }
                    }
                }
            }

            {
                using SkuldDatabaseContext database = new SkuldDbContextFactory().CreateDbContext();

                var gld = await database.GetOrInsertGuildAsync(arg.Guild).ConfigureAwait(false);

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
                        await BotService.DiscordClient.SendChannelAsync(channel, message);
                    }
                }
            }



            Log.Verbose(Key, $"{arg} joined {arg.Guild}");
        }

        private static async Task Bot_UserLeft(SocketGuildUser arg)
        {
            DogStatsd.Increment("guild.users.left");

            using var db = new SkuldDbContextFactory().CreateDbContext();

            var gld = await db.GetOrInsertGuildAsync(arg.Guild).ConfigureAwait(false);

            if (gld != null)
            {
                if (gld.LeaveChannel != 0 && !string.IsNullOrEmpty(gld.LeaveMessage))
                {
                    var channel = arg.Guild.GetTextChannel(gld.JoinChannel);
                    var message = gld.LeaveMessage.ReplaceGuildEventMessage(arg as IUser, arg.Guild as SocketGuild);
                    await BotService.DiscordClient.SendChannelAsync(channel, message);
                }
            }

            Log.Verbose(Key, $"{arg} left {arg.Guild}");
        }

        private static async Task Bot_UserUpdated(SocketUser arg1, SocketUser arg2)
        {
            if (arg1.IsBot || arg1.IsWebhook) return;
            if (arg1.GetAvatarUrl() != arg2.GetAvatarUrl())
            {
                var db = new SkuldDbContextFactory().CreateDbContext();

                var user = await db.InsertOrGetUserAsync(arg2).ConfigureAwait(false);

                user.AvatarUrl = arg2.GetAvatarUrl() ?? arg2.GetDefaultAvatarUrl();

                await db.SaveChangesAsync().ConfigureAwait(false);
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

            Log.Verbose(Key, $"Just left {arg}");
        }

        private static async Task Bot_JoinedGuild(SocketGuild arg)
        {
            using var database = new SkuldDbContextFactory().CreateDbContext();

            DogStatsd.Increment("guilds.joined");

            await BotService.DiscordClient.SendDataAsync(BotService.Configuration.IsDevelopmentBuild, BotService.Configuration.DiscordGGKey, BotService.Configuration.DBotsOrgKey, BotService.Configuration.B4DToken).ConfigureAwait(false);

            await database.GetOrInsertGuildAsync(arg, BotService.Configuration.Prefix, BotService.MessageServiceConfig.MoneyName, BotService.MessageServiceConfig.MoneyIcon);

            //MessageQueue.CheckForEmptyGuilds = true;
            Log.Verbose(Key, $"Just left {arg}");
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

            Log.Verbose(Key, $"{arg} deleted in {arg.Guild}");
        }

        private static async Task Bot_GuildMemberUpdated(SocketGuildUser arg1, SocketGuildUser arg2)
        {   
            using SkuldDatabaseContext database = new SkuldDbContextFactory().CreateDbContext();

            if(arg1.Roles.Count != arg2.Roles.Count)
            {
                var guildPersistentRoles = database.PersistentRoles.AsQueryable().Where(x => x.GuildId == arg2.Guild.Id).DistinctBy(x => x.RoleId).ToList();

                guildPersistentRoles.ForEach(z =>
                {
                    arg2.Roles.ToList().ForEach(x =>
                    {
                        if (z.RoleId == x.Id)
                        {
                            if(!database.PersistentRoles.Any(y=>y.RoleId == z.RoleId && y.UserId == arg2.Id && y.GuildId == arg2.Guild.Id))
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
        }

        #endregion Guilds
    }
}
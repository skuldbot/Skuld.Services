using Discord;
using Discord.WebSocket;
using Skuld.Core;
using Skuld.Core.Extensions;
using Skuld.Core.Utilities;
using Skuld.Models;
using Skuld.Services.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Skuld.Services.Accounts.Experience
{
    public static class ExperienceService
    {
        public static async Task HandleExperienceAsync(IUserMessage message, IUser user, IGuild guild)
        {
            if (user.IsBot || user.IsWebhook) return;

            try
            {
                using var Database = new SkuldDbContextFactory().CreateDbContext();

                var User = await Database.InsertOrGetUserAsync(user).ConfigureAwait(false);

                await User.GrantExperienceAsync((ulong)SkuldRandom.Next(1, 51), guild, message, false, DefaultAction).ConfigureAwait(false);

                var xp = Database.UserXp.FirstOrDefault(x => x.GuildId == guild.Id && x.UserId == user.Id && x.IsVoiceExperience == false);

                xp.MessagesSent = xp.MessagesSent.Add(1);

                await Database.SaveChangesAsync().ConfigureAwait(false);
            }
            catch(Exception ex)
            {
                Log.Error("ExperienceService", ex.Message, ex);
            }
        }

        static string GetMessage(IUser user, IGuild guild, Guild dbGuild, IUserMessage message, ulong level, IEnumerable<LevelRewards> roles, bool showFromVoice = false)
        {
            var msg = dbGuild.LevelUpMessage;

            string rles = "None at this role";

            if (roles.Any())
            {
                if (roles.Count() <= 10)
                {
                    rles = string.Join(", ", roles.Select(x => guild.GetRole(x.RoleId).Name).ToArray());
                }
                else
                {
                    rles = $"{roles.Count().ToFormattedString()} roles";
                }
            }

            if (msg != null && user != null && guild != null)
            {
                msg = msg.ReplaceGuildEventMessage(user, guild as SocketGuild)
                    .ReplaceFirst("-l", level.ToFormattedString())
                    .ReplaceFirst("-r", rles);

                var jumpLink = message.GetJumpUrl();
                if (jumpLink != null)
                {
                    msg.ReplaceFirst("-jl", jumpLink);
                }
                else
                {
                    msg.ReplaceFirst("-jl", "JUMPLINK NOT AVAILABLE");
                }
            }
            else
            {
                msg = $"Congratulations {user.Mention}!! You're now level **{level}**! You currently have access to: `{rles}`";
            }

            if (showFromVoice)
            {
                msg += $"\n**FROM VOICE**";
            }

            return msg;
        }

        static IEnumerable<LevelRewards> GetUnlockedRoles(ulong guildId, bool automatic = false)
        {
            using var Database = new SkuldDbContextFactory().CreateDbContext();

            return Database.LevelRewards.ToList().Where(x => x.GuildId == guildId && x.Automatic == automatic);
        }

        public static Action<IGuildUser, IGuild, Guild, IUserMessage, ulong> DefaultAction = new Action<IGuildUser, IGuild, Guild, IUserMessage, ulong>(
            async (user, guild, dbGuild, message, level) =>
            {
                using var Database = new SkuldDbContextFactory().CreateDbContext();

                var autoRoles = GetUnlockedRoles(guild.Id, true);
                var nonautoRoles = GetUnlockedRoles(guild.Id);
                var bot = await guild.GetCurrentUserAsync().ConfigureAwait(false);
                var highestRole = bot.GetHighestRole();

                var GuildConfig = Database.Features.ToList().FirstOrDefault(x => x.Id == guild.Id);

                List<LevelRewards> roles = new List<LevelRewards>(autoRoles);
                roles.AddRange(nonautoRoles);

                var msg = GetMessage(user, guild, dbGuild, message, level, roles);

                if (autoRoles.Any(x => x.LevelRequired == level))
                {
                    var usr = await guild.GetUserAsync(user.Id).ConfigureAwait(false);

                    if (!GuildConfig.StackingRoles)
                    {
                        var badRoles = autoRoles.Where(x => x.LevelRequired < level);

                        if (usr.RoleIds.Any(x => badRoles.Any(z => z.RoleId == x)))
                        {
                            if (bot.GuildPermissions.ManageRoles)
                            {
                                await usr.RemoveRolesAsync(badRoles.Where(x => usr.RoleIds.Contains(x.RoleId)).Select(x => guild.GetRole(x.RoleId)));
                            }
                        }
                    }

                    var grantRoles = autoRoles.Where(x => x.LevelRequired == level).Select(x => guild.GetRole(x.RoleId));

                    if (bot.GuildPermissions.ManageRoles && grantRoles.All(x=>x.Position < highestRole.Position))
                    {
                        await usr.AddRolesAsync(grantRoles).ConfigureAwait(false);
                    }
                }

                switch (dbGuild.LevelNotification)
                {
                    case LevelNotification.Channel:
                        {
                            if (dbGuild.LevelUpChannel != 0)
                            {
                                var channel = await
                                    guild.GetTextChannelAsync(dbGuild.LevelUpChannel)
                                .ConfigureAwait(false);

                                if (channel != null)
                                {
                                    await
                                        channel.SendMessageAsync(msg)
                                    .ConfigureAwait(false);
                                }
                                else
                                {
                                    var owner = await
                                        guild.GetOwnerAsync()
                                    .ConfigureAwait(false);

                                    await
                                        owner.SendMessageAsync($"Channel with Id **{dbGuild.LevelUpChannel}** no longer exists, please update using `{dbGuild.Prefix}guild channel join #newChannel`")
                                    .ConfigureAwait(false);
                                }
                            }
                            else
                            {
                                await
                                    message.Channel.SendMessageAsync(msg)
                                .ConfigureAwait(false);
                            }
                        }
                        break;

                    case LevelNotification.DM:
                        {
                            try
                            {
                                await
                                    user.SendMessageAsync($"**{guild.Name}:** " + msg)
                                .ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                Log.Error("ExperienceService", "Failed sending level up message to DMs, most likely disabled DMs", ex);
                            }
                        }
                        break;

                    case LevelNotification.None:
                    default:
                        break;
                }
            });

        public static Action<IGuildUser, IGuild, Guild, IUserMessage, ulong> VoiceAction = new Action<IGuildUser, IGuild, Guild, IUserMessage, ulong>(
            async (user, guild, dbGuild, message, level) =>
            {
                using var Database = new SkuldDbContextFactory().CreateDbContext();

                var autoRoles = GetUnlockedRoles(guild.Id, true);
                var nonautoRoles = GetUnlockedRoles(guild.Id);
                var bot = await guild.GetCurrentUserAsync().ConfigureAwait(false);
                var highestRole = bot.GetHighestRole();

                var GuildConfig = Database.Features.ToList().FirstOrDefault(x => x.Id == guild.Id);

                List<LevelRewards> roles = new List<LevelRewards>(autoRoles);
                roles.AddRange(nonautoRoles);

                var msg = GetMessage(user, guild, dbGuild, null, level, roles, true);

                if (autoRoles.Any(x => x.LevelRequired == level))
                {
                    var usr = await guild.GetUserAsync(user.Id).ConfigureAwait(false);

                    if (!GuildConfig.StackingRoles)
                    {
                        var badRoles = autoRoles.Where(x => x.LevelRequired < level);

                        if (usr.RoleIds.Any(x => badRoles.Any(z => z.RoleId == x)))
                        {
                            if (bot.GuildPermissions.ManageRoles)
                            {
                                await usr.RemoveRolesAsync(badRoles.Where(x => usr.RoleIds.Contains(x.RoleId)).Select(x => guild.GetRole(x.RoleId)));
                            }
                        }
                    }

                    var grantRoles = autoRoles.Where(x => x.LevelRequired == level).Select(x => guild.GetRole(x.RoleId));

                    if (bot.GuildPermissions.ManageRoles && grantRoles.All(x => x.Position < highestRole.Position))
                    {
                        await usr.AddRolesAsync(grantRoles).ConfigureAwait(false);
                    }
                }

                switch (dbGuild.LevelNotification)
                {
                    case LevelNotification.Channel:
                        {
                            if (dbGuild.LevelUpChannel != 0)
                            {
                                var channel = await
                                    guild.GetTextChannelAsync(dbGuild.LevelUpChannel)
                                .ConfigureAwait(false);

                                if (channel != null)
                                {
                                    await 
                                        channel.SendMessageAsync(msg)
                                    .ConfigureAwait(false);
                                }
                                else
                                {
                                    var owner = await 
                                        guild.GetOwnerAsync()
                                    .ConfigureAwait(false);

                                    await
                                        owner.SendMessageAsync($"Channel with Id **{dbGuild.LevelUpChannel}** no longer exists, please update using `{dbGuild.Prefix}guild channel join #newChannel`")
                                    .ConfigureAwait(false);
                                }
                            }
                            else
                            {
                                await
                                    message.Channel.SendMessageAsync(msg)
                                .ConfigureAwait(false);
                            }
                        }
                        break;

                    case LevelNotification.DM:
                        {
                            try
                            {
                                await
                                    user.SendMessageAsync($"**{guild.Name}:** " + msg)
                                .ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                Log.Error("ExperienceService", "Failed sending level up message to DMs, most likely disabled DMs", ex);
                            }
                        }
                        break;

                    case LevelNotification.None:
                    default:
                        break;
                }
            });
    }
}
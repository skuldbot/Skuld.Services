using Discord;
using Skuld.Core;
using Skuld.Core.Models;
using Skuld.Core.Utilities;
using Skuld.Services.Extensions;
using StatsdClient;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Skuld.Services.Accounts.Experience
{
    public static class ExperienceService
    {
        public static async Task HandleExperienceAsync(IUserMessage message, IUser user, IGuild guild, Guild sguild, IMessageChannel backupChannel)
        {
            if (user.IsBot || user.IsWebhook) return;

            User User = null;
            {
                using var Database = new SkuldDbContextFactory().CreateDbContext();

                User = await Database.InsertOrGetUserAsync(user).ConfigureAwait(false);
            }

            var result = await User.GrantExperienceAsync((ulong)SkuldRandom.Next(1, 26), guild, message, async (user, guild, dbGuild, dMsg, level) =>
            {
                var msg = dbGuild.LevelUpMessage;
                if (msg != null)
                    msg = msg
                        .Replace("-m", user.Mention)
                        .Replace("-u", user.Username)
                        .Replace("-l", level.ToString("N0"));
                else
                    msg = $"Congratulations {user.Mention}!! You're now level **{level}**";

                switch (dbGuild.LevelNotification)
                {
                    case LevelNotification.Channel:
                        {
                            if (dbGuild.LevelUpChannel != 0)
                            {
                                if (message.Channel.Id != dbGuild.LevelUpChannel)
                                {
                                    msg += $"\nMessage that caused level Up: {dMsg.GetJumpUrl()}";
                                }

                                await (await guild.GetTextChannelAsync(dbGuild.LevelUpChannel).ConfigureAwait(false)).SendMessageAsync(msg).ConfigureAwait(false);
                            }
                            else
                            {
                                await message.Channel.SendMessageAsync(msg).ConfigureAwait(false);
                            }
                        }
                        break;

                    case LevelNotification.DM:
                        {
                            msg += $"\nMessage that caused level Up: {dMsg.GetJumpUrl()}";
                            await user.SendMessageAsync(msg).ConfigureAwait(false);
                        }
                        break;

                    case LevelNotification.None:
                    default:
                        break;
                }
            });

            if (result != null)
            {
                if (result is bool b && b)
                {
                    UserExperience luxp;
                    List<LevelRewards> rewardsForGuild;

                    {
                        using var Database = new SkuldDbContextFactory().CreateDbContext();

                        luxp = Database.UserXp.FirstOrDefault(x => x.UserId == user.Id && x.GuildId == guild.Id);
                        rewardsForGuild = await Database.LevelRewards.AsAsyncEnumerable().Where(x => x.GuildId == guild.Id).ToListAsync();
                    }

                    string msg = sguild.LevelUpMessage;

                    string appendix = null;

                    if ((sguild.LevelUpChannel != 0 && sguild.LevelUpChannel != backupChannel.Id) || sguild.LevelNotification == LevelNotification.DM)
                        appendix = $"\n\nMessage that caused your level up: {message.GetJumpUrl()}";

                    switch (sguild.LevelNotification)
                    {
                        case LevelNotification.Channel:
                            {
                                ulong ChannelID = 0;
                                if (sguild.LevelUpChannel != 0)
                                    ChannelID = sguild.LevelUpChannel;
                                else
                                    ChannelID = backupChannel.Id;

                                if (string.IsNullOrEmpty(sguild.LevelUpMessage))
                                {
                                    await (await guild.GetTextChannelAsync(ChannelID).ConfigureAwait(false))
                                        .SendMessageAsync($"{msg}{appendix}").ConfigureAwait(false);
                                }
                                else
                                {
                                    await (await guild.GetTextChannelAsync(ChannelID).ConfigureAwait(false)).SendMessageAsync(msg).ConfigureAwait(false);
                                }
                            }
                            break;

                        case LevelNotification.DM:
                            {
                                if (string.IsNullOrEmpty(sguild.LevelUpMessage))
                                    await user.SendMessageAsync($"{msg} in {guild.Name}{appendix}").ConfigureAwait(false);
                                else
                                {
                                    await user.SendMessageAsync(msg).ConfigureAwait(false);
                                }
                            }
                            break;
                    }

                    if (rewardsForGuild.Any(x => (ulong)x.LevelRequired <= luxp.Level))
                    {
                        var guser = await guild.GetUserAsync(user.Id).ConfigureAwait(false);
                        foreach (var reward in rewardsForGuild.Where(x => (ulong)x.LevelRequired <= luxp.Level))
                        {
                            await guser.AddRoleAsync(guild.GetRole(reward.RoleId)).ConfigureAwait(false);
                        }
                    }

                    Log.Info("ExpService", "User leveled up");
                    DogStatsd.Increment("user.levels.levelup");
                }
            }

            return;
        }
    }
}
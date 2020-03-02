using Discord;
using Skuld.Core;
using Skuld.Core.Extensions;
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
                        .Replace("-l", level.ToFormattedString());
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
                    Log.Verbose("ExpService", "User leveled up");
                    DogStatsd.Increment("user.levels.levelup");
                }
            }

            return;
        }
    }
}
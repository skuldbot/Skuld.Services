using Discord;
using Discord.WebSocket;
using NodaTime;
using Skuld.Core.Extensions;
using Skuld.Core.Extensions.Formatting;
using Skuld.Core.Utilities;
using Skuld.Models;
using Skuld.Services.Accounts.Banking.Models;
using Skuld.Services.Banking;
using StatsdClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Skuld.Services.Extensions
{
    public static class UserExtensions
    {
        public static async Task<bool> GrantExperienceAsync(
            this User user,
            ulong amount,
            IGuild guild,
            IUserMessage message,
            Action<IGuildUser, IGuild, Guild, IUserMessage, ulong> action,
            bool skipTimeCheck = false)
        {
            using var Database = new SkuldDbContextFactory().CreateDbContext();
            var luxp = Database.UserXp.FirstOrDefault(
                x => x.UserId == user.Id && x.GuildId == guild.Id);

            if(user.PrestigeLevel > 0)
            {
                var addition = (ulong)Math.Floor(amount / user.PrestigeLevel * .25);
                amount = amount.Add(addition);
            }

            bool didLevelUp = false;
            bool wasDatabaseChanged = false;

            if (luxp != null)
            {
                var now = DateTime.UtcNow.ToEpoch();
                var check = now - luxp.LastGranted;
                if (check >= 60 || skipTimeCheck)
                {
                     ulong levelAmount = 0;

                    DogStatsd.Increment("user.levels.xp.granted", (int)amount);

                    DogStatsd.Increment("user.levels.processed");

                    var xptonextlevel = DatabaseUtilities.GetXPLevelRequirement(luxp.Level + 1, DiscordUtilities.PHI);
                    var currXp = luxp.XP.Add(amount);
                    while (currXp >= xptonextlevel)
                    {
                        DogStatsd.Increment("user.levels.levelup");

                        levelAmount++;
                        currXp = currXp.Subtract(xptonextlevel);
                        xptonextlevel = DatabaseUtilities.GetXPLevelRequirement(luxp.Level + 1 + levelAmount, DiscordUtilities.PHI);

                        action.Invoke(await guild.GetUserAsync(user.Id).ConfigureAwait(false),
                                      guild,
                                      await Database.InsertOrGetGuildAsync(guild).ConfigureAwait(false),
                                      message,
                                      luxp.Level + levelAmount);
                    }

                    if (levelAmount > 0)
                    {
                        luxp.XP = currXp;
                        luxp.TotalXP = luxp.TotalXP.Add(amount);
                        luxp.Level = luxp.Level.Add(levelAmount);

                        if (!skipTimeCheck)
                        {
                            luxp.LastGranted = now;
                        }

                        Log.Verbose("XPGrant", $"User leveled up {levelAmount} time{(levelAmount >= 2 ? "s" : (levelAmount < 1 ? "s" : ""))}");

                        didLevelUp = true;
                        wasDatabaseChanged = true;
                    }
                    else
                    {
                        luxp.XP = luxp.XP.Add(amount);
                        luxp.TotalXP = luxp.TotalXP.Add(amount);

                        if (!skipTimeCheck)
                        {
                            luxp.LastGranted = now;
                        }

                        didLevelUp = false;
                        wasDatabaseChanged = true;
                    }
                }
                else
                {
                    didLevelUp = false;
                }
            }
            else
            {
                Database.UserXp.Add(new UserExperience
                {
                    LastGranted = DateTime.UtcNow.ToEpoch(),
                    XP = amount,
                    UserId = user.Id,
                    GuildId = guild.Id,
                    TotalXP = amount,
                    Level = 0
                });

                didLevelUp = false;
                wasDatabaseChanged = true;
            }

            if(wasDatabaseChanged)
            {
                await Database.SaveChangesAsync().ConfigureAwait(false);
            }

            return didLevelUp;
        }

        public static async Task<EmbedBuilder> GetWhoisAsync(this IUser user, IGuildUser guildUser, IReadOnlyCollection<ulong> roles, IDiscordClient Client, SkuldConfig Configuration)
        {
            using var Database = new SkuldDbContextFactory().CreateDbContext();
            var sUser = await Database.InsertOrGetUserAsync(user).ConfigureAwait(false);

            string status;
            if (user.Activity != null)
            {
                if (user.Activity.Type == ActivityType.Streaming) status = DiscordUtilities.Streaming_Emote.ToString();
                else status = user.Status.StatusToEmote();
            }
            else status = user.Status.StatusToEmote();

            var embed = new EmbedBuilder()
                .AddAuthor(Client)
                .WithTitle(guildUser != null ? guildUser.FullNameWithNickname() : user.FullName())
                .WithThumbnailUrl(user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl())
                .WithColor(guildUser?.GetHighestRoleColor(guildUser?.Guild) ?? EmbedExtensions.RandomEmbedColor());

            embed.AddInlineField(":id: User ID", user.Id.ToString() ?? "Unknown");
            embed.AddInlineField(":vertical_traffic_light: Status", status ?? "Unknown");

            if (user.Activity != null)
            {
                embed.AddInlineField(":video_game: Status", user.Activity.ActivityToString());
            }

            embed.AddInlineField("🤖 Bot", user.IsBot ? DiscordUtilities.Tick_Emote : DiscordUtilities.Cross_Emote);

            embed.AddInlineField("👀 Mutual Servers", $"{(user as SocketUser).MutualGuilds.Count}");

            StringBuilder clientString = new StringBuilder();
            foreach (var client in user.ActiveClients)
            {
                clientString = clientString.Append(client.ToEmoji());

                if (user.ActiveClients.Count > 1 && client != user.ActiveClients.LastOrDefault())
                    clientString.Append(", ");
            }

            if (user.ActiveClients.Any())
            {
                embed.AddInlineField($"Active Client{(user.ActiveClients.Count > 1 ? "s" : "")}", $"{clientString}");
            }

            if (roles != null)
            {
                embed.AddField(":shield: Roles", $"[{roles.Count}] Do `{Configuration.Prefix}roles` to see your roles");
            }

            if (sUser.TimeZone != null)
            {
                var time = Instant.FromDateTimeUtc(DateTime.UtcNow).InZone(DateTimeZoneProviders.Tzdb.GetZoneOrNull(sUser.TimeZone)).ToDateTimeUnspecified().ToDMYString();

                embed.AddField("Current Time", $"{time}\t`DD/MM/YYYY HH:mm:ss`");
            }

            var createdatstring = user.CreatedAt.GetStringfromOffset(DateTime.UtcNow);
            embed.AddField(":globe_with_meridians: Discord Join", user.CreatedAt.ToDMYString() + $" ({createdatstring})\t`DD/MM/YYYY`");

            if (guildUser != null)
            {
                var joinedatstring = guildUser.JoinedAt.Value.GetStringfromOffset(DateTime.UtcNow);
                embed.AddField(":inbox_tray: Server Join", guildUser.JoinedAt.Value.ToDMYString() + $" ({joinedatstring})\t`DD/MM/YYYY`");
            }

            if (guildUser.PremiumSince.HasValue)
            {
                var icon = guildUser.PremiumSince.Value.BoostMonthToEmote();

                var offsetString = guildUser.PremiumSince.Value.GetStringfromOffset(DateTime.UtcNow);

                embed.AddField(DiscordUtilities.NitroBoostEmote + " Boosting Since", $"{(icon == null ? "" : icon + " ")}{guildUser.PremiumSince.Value.UtcDateTime.ToDMYString()} ({offsetString})\t`DD/MM/YYYY`");
            }

            return embed;
        }

        public static bool IsStreakReset(this User target, SkuldConfig config)
        {
            var limit = target.LastDaily.FromEpoch().AddDays(target.IsDonator ? config.StreakLimitDays * 2 : config.StreakLimitDays).ToEpoch();

            return DateTime.UtcNow.ToEpoch() > limit;
        }

        public static ulong GetDailyAmount(this User target, SkuldConfig config)
        {
            var daily = config.DailyAmount;

            if (target.Streak > 0)
            {
                var amount = daily * Math.Min(50, target.Streak);

                if (target.IsDonator)
                {
                    return daily + amount * 2;
                }
                else
                {
                    return daily + amount;
                }
            }
            if (target.IsDonator)
            {
                return daily * 2;
            }
            else
            {
                return daily;
            }
        }

        public static bool ProcessDaily(this User target, ulong amount, User donor = null)
        {
            bool wasSuccessful = false;

            if(donor == null)
            {
                donor = target;
            }

            if (donor.LastDaily == 0 || donor.LastDaily < DateTime.UtcNow.Date.ToEpoch())
            {
                TransactionService.DoTransaction(new TransactionStruct
                {
                    Amount = amount,
                    Receiver = target
                });

                donor.LastDaily = DateTime.UtcNow.ToEpoch();

                wasSuccessful = true;
            }

            return wasSuccessful;
        }

        public static IRole GetHighestRole(this IGuildUser user)
        {
            var roles = user.RoleIds.Select(x => user.Guild.GetRole(x));

            return roles.OrderByDescending(x => x.Position).FirstOrDefault();
        }
    }
}
﻿using Discord;
using Discord.WebSocket;
using NodaTime;
using Skuld.Core.Extensions;
using Skuld.Core.Extensions.Formatting;
using Skuld.Core.Extensions.Verification;
using Skuld.Core.Models;
using Skuld.Core.Utilities;
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

        public static async Task<object> GrantExperienceAsync(this User user, ulong amount, IGuild guild, bool skipTimeCheck = false)
        {
            using var Database = new SkuldDbContextFactory().CreateDbContext();
            var luxp = Database.UserXp.FirstOrDefault(x => x.UserId == user.Id && x.GuildId == guild.Id);

            bool didLevelUp = false;

            if (luxp != null)
            {
                ulong levelAmount = 0;

                var xptonextlevel = DatabaseUtilities.GetXPLevelRequirement(luxp.Level + 1, DiscordUtilities.PHI); //get next level xp requirement based on phi
                while ((luxp.XP + amount) >= xptonextlevel)
                {
                    xptonextlevel = DatabaseUtilities.GetXPLevelRequirement(luxp.Level + 1 + levelAmount, DiscordUtilities.PHI);
                    levelAmount++;
                }

                if (luxp.LastGranted < (DateTime.UtcNow.ToEpoch() - 60) || skipTimeCheck)
                {
                    DogStatsd.Increment("user.levels.processed");
                    if (levelAmount > 0) //if over or equal to next level requirement, update accordingly
                    {
                        luxp.XP = 0;
                        luxp.TotalXP += amount;
                        luxp.Level += levelAmount;
                        luxp.LastGranted = DateTime.UtcNow.ToEpoch();

                        didLevelUp = true;
                    }
                    else
                    {
                        luxp.XP += amount;
                        luxp.TotalXP += amount;
                        luxp.LastGranted = DateTime.UtcNow.ToEpoch();

                        didLevelUp = false;
                    }
                }
                else
                {
                    return null;
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
                    Level = DatabaseUtilities.GetLevelFromTotalXP(amount, DiscordUtilities.PHI)
                });

                didLevelUp = false;

                DogStatsd.Increment("user.levels.processed");
            }

            await Database.SaveChangesAsync().ConfigureAwait(false);

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
                .WithTitle($"{user.Username}{(guildUser != null ? (guildUser.Nickname != null ? $" ({guildUser.Nickname})" : "") : "")}#{user.DiscriminatorValue}")
                .WithThumbnailUrl(user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl())
                .WithColor(guildUser?.GetHighestRoleColor(guildUser?.Guild) ?? EmbedExtensions.RandomEmbedColor());

            embed.AddInlineField(":id: User ID", user.Id.ToString() ?? "Unknown");
            embed.AddInlineField(":vertical_traffic_light: Status", status ?? "Unknown");

            if (user.Activity != null)
            {
                embed.AddInlineField(":video_game: Status", user.Activity.ActivityToString());
            }

            embed.AddInlineField("🤖 Is a bot?", user.IsBot ? "✔️" : "❌");

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
            if (target.Flags.IsBitSet(DiscordUtilities.BotDonator))
                return target.Streak > (config.MaxStreak * 2) || target.LastDaily > target.LastDaily.FromEpoch().AddDays((config.StreakLimitDays * 2)).ToEpoch();
            else
                return target.Streak > config.MaxStreak || target.LastDaily > target.LastDaily.FromEpoch().AddDays(config.StreakLimitDays).ToEpoch();
        }

        public static ulong GetDailyAmount(this User target, SkuldConfig config)
        {
            if (target.Flags.IsBitSet(DiscordUtilities.BotDonator))
                return config.DailyAmount + (config.DailyAmount * Math.Min(100, target.Streak)) * 2;
            else
                return config.DailyAmount + (config.DailyAmount * Math.Min(100, target.Streak));
        }

        public static bool ProcessDaily(this User target, SkuldConfig config, User donor = null)
        {
            bool wasSuccessful = false;

            if (donor == null)
            {
                if (target.LastDaily == 0 || target.LastDaily < DateTime.UtcNow.Date.ToEpoch())
                {
                    target.Money += target.GetDailyAmount(config);

                    if (target.IsStreakReset(config))
                    {
                        target.Streak = 0;
                    }

                    target.LastDaily = DateTime.UtcNow.ToEpoch();
                    wasSuccessful = true;
                }
            }
            else
            {
                if (donor.LastDaily == 0 || donor.LastDaily < DateTime.UtcNow.Date.ToEpoch())
                {
                    target.Money += donor.GetDailyAmount(config);

                    if (donor.IsStreakReset(config))
                    {
                        donor.Streak = 0;
                    }

                    donor.LastDaily = DateTime.UtcNow.ToEpoch();
                    wasSuccessful = true;
                }
            }

            return wasSuccessful;
        }
    }
}
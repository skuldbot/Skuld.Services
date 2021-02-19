using Discord;
using Discord.Commands;
using Discord.WebSocket;
using NodaTime;
using Skuld.Core.Extensions;
using Skuld.Core.Extensions.Formatting;
using Skuld.Core.Models;
using Skuld.Core.Utilities;
using Skuld.Models;
using Skuld.Services.Accounts.Banking.Models;
using Skuld.Services.Banking;
using StatsdClient;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
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
			ICommandContext context,
			Action<IGuildUser, IGuild, Guild, ICommandContext, ulong> action,
			bool skipTimeCheck = false)
			=> await GrantExperienceAsync(user, amount, context.Guild, action, skipTimeCheck, context).ConfigureAwait(false);

		public static async Task<bool> GrantExperienceAsync(
			this User user,
			ulong amount,
			IGuild guild,
			Action<IGuildUser, IGuild, Guild, ICommandContext, ulong> action,
			bool skipTimeCheck = false,
			ICommandContext context = null)
		{
			using var Database = new SkuldDbContextFactory().CreateDbContext();
			UserExperience luxp;

			if (guild != null)
			{
				luxp = Database.UserXp.FirstOrDefault(
					x => x.UserId == user.Id
					&& x.GuildId == guild.Id);
			}
			else
			{
				luxp = Database.UserXp.FirstOrDefault(
					x => x.UserId == user.Id
					&& x.GuildId == 0);
			}

			if (user.PrestigeLevel > 0)
			{
				var addition = (ulong)Math.Floor(amount * ((float)user.PrestigeLevel / 2));
				amount = amount.Add(addition);
			}

			bool didLevelUp = false;
			var now = DateTime.UtcNow.ToEpoch();

			if (luxp != null)
			{
				var check = now - luxp.LastGranted;

				if (check >= 60 || skipTimeCheck)
				{
					var result = await
						PerformLevelupCheckAsync(
							luxp,
							amount,
							user,
							guild,
							action,
							skipTimeCheck,
							context
						)
					.ConfigureAwait(false);

					didLevelUp = result.Successful;

					luxp = result.Data;
				}

				if (luxp.Level == 0 && luxp.TotalXP != 0)
				{
					luxp.Level = DatabaseUtilities.GetLevelFromTotalXP(
						luxp.TotalXP,
						DiscordUtilities.LevelModifier
					);
				}
			}
			else
			{
				ulong id = 0;

				if (guild != null)
				{
					id = guild.Id;
				}

				var xp = new UserExperience
				{
					LastGranted = now,
					UserId = user.Id,
					GuildId = id,
					TotalXP = amount,
					Level = 0
				};

				var result = await
					PerformLevelupCheckAsync(
						xp,
						amount,
						user,
						guild,
						action
					)
				.ConfigureAwait(false);

				didLevelUp = result.Successful;

				Database.UserXp.Add(result.Data);
			}

			await Database.SaveChangesAsync().ConfigureAwait(false);

			return didLevelUp;
		}

		public static async Task<EventResult<UserExperience>> PerformLevelupCheckAsync(
			this UserExperience xp,
			ulong amount,
			User user,
			IGuild guild,
			Action<IGuildUser, IGuild, Guild, ICommandContext, ulong> action,
			bool skipTimeCheck = false,
			ICommandContext context = null)
		{
			using var Database = new SkuldDbContextFactory().CreateDbContext();
			var now = DateTime.UtcNow.ToEpoch();
			ulong levelAmount = 0;

			DogStatsd.Increment("user.levels.xp.granted", (int)amount);

			DogStatsd.Increment("user.levels.processed");

			xp.TotalXP = xp.TotalXP.Add(amount);

			ulong xpCurrentLevel = DatabaseUtilities
				.GetStackedXPLevelRequirement(
					xp.Level,
					DiscordUtilities.LevelModifier
			);

			ulong xpNextLevel = DatabaseUtilities.GetXPLevelRequirement(
				xp.Level + 1,
				DiscordUtilities.LevelModifier
			);

			var currXp = xp.TotalXP - xpCurrentLevel;

			while (currXp >= xpNextLevel)
			{
				DogStatsd.Increment("user.levels.levelup");

				levelAmount++;
				currXp = currXp.Subtract(xpNextLevel);
				xpNextLevel = DatabaseUtilities.GetXPLevelRequirement(
					xp.Level + 1 + levelAmount,
					DiscordUtilities.LevelModifier
				);
			}

			try
			{
				if (levelAmount > 0)
				{
					if (action != null)
					{
						action.Invoke(await guild.GetUserAsync(user.Id).ConfigureAwait(false),
									  guild,
									  await Database.InsertOrGetGuildAsync(guild).ConfigureAwait(false),
									  context,
									  xp.Level + levelAmount);
					}
				}
			}
			catch (Exception ex)
			{
				Log.Error("ExpSrvc", ex.Message, context ?? null, ex);
			}

			if (levelAmount != 0)
			{
				xp.Level = xp.Level.Add(levelAmount);
			}

			xp.MessagesSent = xp.MessagesSent.Add(1);

			if (!skipTimeCheck)
			{
				xp.LastGranted = now;
			}

			if (levelAmount > 0)
			{
				var suffix = "";

				if (levelAmount >= 2 || levelAmount < 1)
				{
					suffix = "s";
				}

				Log.Verbose("XPGrant",
					$"User leveled up {levelAmount} time{suffix}{(action != null ? "" : " globally")}",
					context);

				return EventResult<UserExperience>.FromSuccess(xp);
			}
			else
			{
				return new EventResult<UserExperience>
				{
					Successful = false,
					Data = xp
				};
			}
		}

		public static async Task<EmbedBuilder> GetWhoisAsync(
			this IUser user,
			IGuildUser guildUser,
			IReadOnlyCollection<ulong> roles,
			IDiscordClient Client,
			SkuldConfig Configuration)
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

			embed.AddInlineField(":id: User ID", Convert.ToString(user.Id, CultureInfo.InvariantCulture) ?? "Unknown");
			embed.AddInlineField(":vertical_traffic_light: Status", status ?? "Unknown");

			if (user.Activity != null)
			{
				embed.AddInlineField(":video_game: Status", user.Activity.ActivityToString());
			}

			embed.AddInlineField("🤖 Bot", user.IsBot ? DiscordUtilities.Tick_Emote : DiscordUtilities.Cross_Emote);

			embed.AddInlineField("👀 Mutual Servers", $"{(user as SocketUser).MutualGuilds.Count}");

			StringBuilder clientString = new();
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

			var createdatstring = user.CreatedAt.GetStringFromOffset(DateTime.UtcNow);
			embed.AddField(":globe_with_meridians: Discord Join", user.CreatedAt.ToDMYString() + $" ({createdatstring})\t`DD/MM/YYYY`");

			if (guildUser != null)
			{
				var joinedatstring = guildUser.JoinedAt.Value.GetStringFromOffset(DateTime.UtcNow);
				embed.AddField(":inbox_tray: Server Join", guildUser.JoinedAt.Value.ToDMYString() + $" ({joinedatstring})\t`DD/MM/YYYY`");
			}

			if (guildUser.PremiumSince.HasValue)
			{
				var icon = guildUser.PremiumSince.Value.BoostMonthToEmote();

				var offsetString = guildUser.PremiumSince.Value.GetStringFromOffset(DateTime.UtcNow);

				embed.AddField(DiscordUtilities.NitroBoostEmote + " Boosting Since", $"{(icon == null ? "" : icon + " ")}{guildUser.PremiumSince.Value.UtcDateTime.ToDMYString()} ({offsetString})\t`DD/MM/YYYY`");
			}

			return embed;
		}

		public static bool IsStreakReset(
			this User target,
			SkuldConfig config)
		{
			var days = target.IsDonator ? config.StreakLimitDays * 2 : config.StreakLimitDays;

			var limit = target.LastDaily.FromEpoch().Date.AddDays(days).ToEpoch();

			return DateTime.UtcNow.Date.ToEpoch() > limit;
		}

		public static ulong GetDailyAmount(
			[NotNull] this User target,
			[NotNull] SkuldConfig config
		)
		{
			var daily = config.DailyAmount;

			var mod = target.IsDonator ? 2U : 1;

			if (target.Streak > 0)
			{
				var amount = daily * Math.Min(50, target.Streak);

				return amount * mod;
			}

			return daily * mod;
		}

		public static bool ProcessDaily(
			this User target,
			ulong amount,
			User donor = null
		)
		{
			bool wasSuccessful = false;

			if (donor == null)
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
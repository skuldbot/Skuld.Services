using Discord;
using Discord.Commands;
using Skuld.Core.Extensions;
using Skuld.Core.Models;
using Skuld.Core.Utilities;
using Skuld.Models;
using StatsdClient;
using System;
using System.Linq;
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

			if (guild is not null)
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

			if (luxp is not null)
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

					luxp = result.Data as UserExperience;
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

				if (guild is not null)
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

				Database.UserXp.Add(result.Data as UserExperience);
			}

			await Database.SaveChangesAsync().ConfigureAwait(false);

			return didLevelUp;
		}

		public static async Task<EventResult> PerformLevelupCheckAsync(
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
					if (action is not null)
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
					$"User leveled up {levelAmount} time{suffix}{(action is not null ? "" : " globally")}",
					context);

				return EventResult.FromSuccess(xp);
			}
			else
			{
				return new EventResult
				{
					Successful = false,
					Data = xp
				};
			}
		}
	}
}
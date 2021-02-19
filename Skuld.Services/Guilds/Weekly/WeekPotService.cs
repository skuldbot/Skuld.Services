using Discord;
using Skuld.Core.Extensions;
using Skuld.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Skuld.Services.Guilds.Weekly
{
	public static class WeekPotService
	{
		private static async Task ExecuteAsync()
		{
			using var Database = new SkuldDbContextFactory().CreateDbContext();

			while (true)
			{
				var currentTime = DateTime.UtcNow;

				if (currentTime.DayOfWeek == DayOfWeek.Tuesday)
				{
					var listified = Database.GuildWeeklyPots.ToList();

					if (listified.Any())
					{
						Database.GuildWeeklyPots.RemoveRange(listified);
					}

					await Database.SaveChangesAsync().ConfigureAwait(false);
				}

				await Task.Delay(50).ConfigureAwait(false);
			}
		}

		public static async Task AddToPotAsync(IGuildUser user, ulong amount)
		{
			using var Database = new SkuldDbContextFactory().CreateDbContext();

			if (Database.GuildWeeklyPots.Any(x => x.UserId == user.Id && x.GuildId == user.GuildId))
			{
				var pot = Database.GuildWeeklyPots.FirstOrDefault(x => x.UserId == user.Id && x.GuildId == user.GuildId);

				pot.Amount = pot.Amount.Add(amount);
				pot.LastGranted = DateTime.UtcNow.ToEpoch();
			}
			else
			{
				Database.GuildWeeklyPots.Add(new WeeklyPot
				{
					Amount = amount,
					GuildId = user.GuildId,
					LastGranted = DateTime.UtcNow.ToEpoch(),
					UserId = user.Id
				});
			}

			await Database.SaveChangesAsync().ConfigureAwait(false);
		}

		public static ulong? GetMoneyFromPot(IGuildUser user)
		{
			using var Database = new SkuldDbContextFactory().CreateDbContext();

			if (Database.GuildWeeklyPots.Any(x => x.UserId == user.Id && x.GuildId == user.GuildId))
			{
				var pot = Database.GuildWeeklyPots.FirstOrDefault(x => x.UserId == user.Id && x.GuildId == user.GuildId);

				return pot.Amount;
			}
			else
			{
				return null;
			}
		}

		public static void Run()
			=> Task.Run(async () => await ExecuteAsync().ConfigureAwait(false));
	}
}

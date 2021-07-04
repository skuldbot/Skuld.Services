using Discord;
using Skuld.Core.Extensions;
using Skuld.Core.Utilities;
using Skuld.Models;
using StatsdClient;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Skuld.Services.Reminders
{
	public class ReminderService : BackgroundService, IBackgroundService
	{
		IDiscordClient DiscordClient;
		const string Key = "ReminderService";

		public ReminderService(IDiscordClient discordClient)
		{
			TimeoutDelay = 1000;
			DiscordClient = discordClient;
			Log.Verbose(Key, "Configured Reminders", null);
		}

		public override async Task RunAsync(CancellationToken cancelToken)
		{
			if (cancelToken.IsCancellationRequested)
			{
				cancelToken.ThrowIfCancellationRequested();
			}

			if (!DiscordClient.IsFullyConnected()) return;

			var currentTime = DateTime.UtcNow.ToEpoch();

			using var Database = new SkuldDbContextFactory().CreateDbContext();

			if (Database.Reminders.Any())
			{
				foreach(var reminder in Database.Reminders)
				{
					if (reminder.Timeout <= currentTime)
					{
						if (await ProcessReminderAsync(reminder))
						{
							Database.Reminders.Remove(reminder);
						}

						await Database.SaveChangesAsync()
							.ConfigureAwait(false);
					}
				}
			}
		}

		private async Task<bool> ProcessReminderAsync(ReminderObject reminder)
		{
			bool result = true;

			try
			{
				IUser creator = await DiscordClient.GetUserAsync(reminder.UserId);

				Embed embed = new EmbedBuilder()
					.WithTitle("⏰ Reminders")
					.WithDescription($"{reminder.Content}\n\n[Message Link]({reminder.MessageLink})")
					.WithTimestamp(reminder.Created.FromEpoch())
					.WithAuthor(creator)
					.WithFooter("Reminder Created")
					.Build();

				await
					(
						await creator
							.CreateDMChannelAsync()
						.ConfigureAwait(false)
					)
					.SendMessageAsync("", embed: embed)
				.ConfigureAwait(false);

				if (reminder.Repeats)
				{
					result = false;

					var diff = reminder.Timeout.Subtract(
						reminder.Created
					);

					reminder.Timeout += diff;

					DogStatsd.Increment("reminders.repeat");
				}

				DogStatsd.Increment("reminders.processed");
			}
			catch (Exception ex)
			{
				Log.Critical("Reminders", ex.Message, null, ex);
				DogStatsd.Increment("reminders.error");
			}

			return result;
		}
	}
}
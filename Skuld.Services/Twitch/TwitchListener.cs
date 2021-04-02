using Discord;
using Discord.WebSocket;
using Skuld.Core.Extensions;
using Skuld.Core.Extensions.Verification;
using Skuld.Core.Utilities;
using Skuld.Models;
using Skuld.Services.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TwitchLib.Api.Interfaces;
using TwitchLib.Api.V5.Models.Streams;
using User = TwitchLib.Api.V5.Models.Users.User;

namespace Skuld.Services.Twitch
{
	public static class TwitchListener
	{
		static DiscordShardedClient DiscordClient;
		static ITwitchAPI APIClient;
		static List<IGrouping<string, TwitchFollow>> streamers;
		const string Key = "Twitch";

		public static void Configure(DiscordShardedClient client)
		{
			DiscordClient = client;
		}

		static async Task BackgroundTickAsync()
		{
			while (true)
			{
				if (!DiscordClient.Shards.All(z => z.ConnectionState == ConnectionState.Connected)) continue;

				try
				{
					using SkuldDbContext database = new SkuldDbContextFactory().CreateDbContext();

					if (database.TwitchFollows.ToList().Any())
					{
						streamers = database.TwitchFollows.ToList().GroupBy(x => x.Streamer).ToList();
					}

					if (streamers is not null && streamers.Count > 0)
					{
						bool anyChange = false;

						foreach (var kvp in streamers)
						{
							var usrs = await APIClient.V5.Users.GetUserByNameAsync(kvp.Key).ConfigureAwait(false);
							var user = usrs.Matches[0];

							bool isLive = await APIClient.V5.Streams.BroadcasterOnlineAsync(user.Id).ConfigureAwait(false);

							var streams = await APIClient.V5.Streams.GetStreamByUserAsync(user.Id).ConfigureAwait(false);
							var stream = streams.Stream;

							List<ITextChannel> destinations = new();

							foreach (var destination in kvp.ToList())
							{
								destinations.Add(DiscordClient.GetGuild(destination.GuildId).GetTextChannel(destination.ChannelId));
							}

							foreach (var dest in kvp.ToList())
							{
								if (isLive)
								{
									if (!dest.IsLive)
									{
										await BroadcastLiveAsync(user, stream, destinations).ConfigureAwait(false);
									}

									dest.IsLive = true;

									anyChange = true;
								}
								else
								{
									if (dest.IsLive)
									{
										dest.IsLive = false;

										anyChange = true;
									}
								}
							}
						}

						if (anyChange)
						{
							await database.SaveChangesAsync().ConfigureAwait(false);
						}
					}
				}
				catch (Exception ex)
				{
					Log.Error(Key, ex.Message, null, ex);
				}

				Log.Verbose(Key, "Tried getting twitch channels", null);
				await Task.Delay(TimeSpan.FromMinutes(1)).ConfigureAwait(false);
			}
		}

		public static void Run(ITwitchAPI apiClient)
		{
			APIClient = apiClient;
			Task.Run(async () =>
			{
				Thread.CurrentThread.IsBackground = true;
				await BackgroundTickAsync().ConfigureAwait(false);
			});
		}

		async static Task BroadcastLiveAsync(User user, Stream stream, List<ITextChannel> destinations)
		{
			var channel = await APIClient.V5.Channels.GetChannelByIDAsync(user.Id).ConfigureAwait(false);

			using var database = new SkuldDbContextFactory().CreateDbContext();

			destinations.ForEach(async x =>
			{
				var gld = await database.InsertOrGetGuildAsync(x.Guild).ConfigureAwait(false);

				string msg = $"{user.DisplayName} is live!";

				if (!gld.TwitchLiveMessage.IsNullOrWhiteSpace())
				{
					msg = gld.TwitchLiveMessage.ReplaceSocialEventMessage(user.DisplayName, new(channel.Url));
				}

				await x.SendMessageAsync(msg, embed: (user.GetEmbed(channel, stream)).Build()).ConfigureAwait(false);
			});
		}
	}
}

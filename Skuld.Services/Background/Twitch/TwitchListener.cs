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
	public class TwitchListener : BackgroundService, IBackgroundService
	{
		IDiscordClient DiscordClient;
		ITwitchAPI APIClient;
		List<IAsyncGrouping<string, TwitchFollow>> streamers;
		const string Key = "TwitchService";

		public TwitchListener(IDiscordClient client, ITwitchAPI apiClient)
		{
			DiscordClient = client;
			APIClient = apiClient;
			Log.Verbose(Key, "Started Twitch", null);
		}

		public override async Task RunAsync(CancellationToken cancelToken)
		{
			if (cancelToken.IsCancellationRequested)
			{
				cancelToken.ThrowIfCancellationRequested();
			}

			if (!DiscordClient.IsFullyConnected()) return;

			try
			{
				using SkuldDbContext database = new SkuldDbContextFactory().CreateDbContext();

				var twitchFollowData = database.TwitchFollows.AsAsyncEnumerable();

				if (await twitchFollowData.AnyAsync())
				{
					streamers = await twitchFollowData.GroupBy(x => x.Streamer).ToListAsync();
				}

				if (streamers is not null && streamers.Count > 0)
				{
					bool anyChange = false;

					foreach (var kvpAsync in streamers)
					{
						var kvp = await kvpAsync.ToListAsync();

						var usrs = await APIClient.V5.Users.GetUserByNameAsync(kvpAsync.Key).ConfigureAwait(false);
						var user = usrs.Matches[0];

						bool isLive = await APIClient.V5.Streams.BroadcasterOnlineAsync(user.Id).ConfigureAwait(false);

						var streams = await APIClient.V5.Streams.GetStreamByUserAsync(user.Id).ConfigureAwait(false);
						var stream = streams.Stream;

						List<ITextChannel> destinations = new();

						foreach (var destination in kvp)
						{
							var guild = await DiscordClient.GetGuildAsync(destination.GuildId);
							var channel = await guild.GetTextChannelAsync(destination.ChannelId);

							destinations.Add(channel);
						}

						foreach(var target in kvp)
						{
							if (isLive)
							{
								if (!target.IsLive)
								{
									await BroadcastLiveAsync(user, stream, destinations).ConfigureAwait(false);
								}

								target.IsLive = true;

								anyChange = true;
							}
							else
							{
								if (target.IsLive)
								{
									target.IsLive = false;

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
		}

		async Task BroadcastLiveAsync(User user, Stream stream, List<ITextChannel> destinations)
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

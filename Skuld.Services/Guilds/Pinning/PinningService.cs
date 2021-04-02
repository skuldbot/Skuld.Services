using Discord;
using Discord.WebSocket;
using Skuld.Core.Utilities;
using Skuld.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Skuld.Services.Guilds.Pinning
{
	public static class PinningService
	{
		const string Key = "PinService";

		static async Task<bool> CanPinAsync(IMessage message, bool isGuild)
		{
			if (!isGuild) return true;
			if (message.Channel is IDMChannel) return true;
			else
			{
				if (message is IGuildChannel guildChannel)
				{
					var botUser = await guildChannel.Guild.GetCurrentUserAsync().ConfigureAwait(false);

					return botUser.GetPermissions(guildChannel).ManageMessages;
				}
			}
			return true;
		}

		public static async Task ExecuteAdditionAsync(DiscordShardedClient client, SkuldConfig configuration, IMessage message, ISocketMessageChannel channel)
		{
			using var Database = new SkuldDbContextFactory().CreateDbContext();
			try
			{
				var guild = client.Guilds.FirstOrDefault(x => x.TextChannels.FirstOrDefault(z => z.Id == channel.Id) is not null);

				if (guild is not null)
				{
					var feats = Database.Features.Find(guild.Id);

					if (feats.Pinning)
					{
						var pins = await channel.GetPinnedMessagesAsync().ConfigureAwait(false);

						if (pins.Count < 50)
						{
							int pinboardThreshold = configuration.PinboardThreshold;
							int pinboardReactions = message.Reactions.FirstOrDefault(x => x.Key.Name is "📌").Value.ReactionCount;

							if (pinboardReactions >= pinboardThreshold)
							{
								var now = message.CreatedAt;
								var dt = DateTime.UtcNow.AddDays(-configuration.PinboardDateLimit);
								if ((now - dt).TotalDays > 0)
								{
									if (!message.IsPinned && await CanPinAsync(message, channel is IGuildChannel).ConfigureAwait(false))
									{
										if (message is IUserMessage msg)
											await msg.PinAsync().ConfigureAwait(false);

										Log.Info(Key, "Message reached threshold, pinned a message");

										return;
									}
								}
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				Log.Critical(Key, ex.Message, null, ex);
			}
		}
	}
}

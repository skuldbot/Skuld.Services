using Discord.WebSocket;
using Skuld.Core.Extensions;
using Skuld.Models;
using Skuld.Services.Accounts.Experience;
using Skuld.Services.Extensions;
using Skuld.Services.VoiceExperience.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Skuld.Services.VoiceExperience
{
	public static class VoiceExpService
	{
		private static DiscordShardedClient DiscordClient;
		private static SkuldConfig Configuration;

		private static ConcurrentBag<VoiceStateTrackable> Trackables;
		private static ConcurrentBag<VoiceEvent> Targets;

		public static void Configure(
			DiscordShardedClient client,
			SkuldConfig config
		)
		{
			Configuration = config;

			DiscordClient = client ??
				throw new ArgumentNullException(
					$"{typeof(DiscordShardedClient).Name} cannot be null"
				);

			Targets = new ConcurrentBag<VoiceEvent>();

			Trackables = new ConcurrentBag<VoiceStateTrackable>();

			DiscordClient.UserVoiceStateUpdated += UserVoiceStateUpdated;
		}

		private static SocketVoiceChannel GetVoiceChannel(
			SocketVoiceState previousState,
			SocketVoiceState currentState
		)
		{
			if (previousState.VoiceChannel is not null && currentState.VoiceChannel is not null)
			{
				if (previousState.VoiceChannel.Guild == currentState.VoiceChannel.Guild)
					return currentState.VoiceChannel;
			}

			if (previousState.VoiceChannel is null && currentState.VoiceChannel is not null)
				return currentState.VoiceChannel;

			return previousState.VoiceChannel;
		}

		private static async Task DoLeaveXpGrantAsync(
			VoiceStateTrackable state
		)
		{
			if (state.User.IsBot || state.User.IsWebhook) return;

			var userEvents = Targets.Where(x => x.User.Id == state.User.Id &&
				x.VoiceChannel.Id == state.Channel.Id)
			.ToList();

			{ // Remove All Events that exist for this user and channel
				ConcurrentBag<VoiceEvent> events = new();

				foreach (var Target in Targets)
				{
					if (Target.User.Id != state.User.Id)
					{
						events.Add(Target);
					}
				}

				Targets.Clear();

				foreach (var e in events)
				{
					Targets.Add(e);
				}
			}

			var connect = userEvents.FirstOrDefault(x => x.IsValid);

			if (connect is null)
			{
				return;
			}

			var disconn = DateTime.UtcNow.ToEpoch();

			var timeDiff = disconn - connect.Time;

			var disallowedPoints = userEvents.Where(x => !x.IsValid).ToList();

			ulong totalTime = 0;

			if (disallowedPoints.Any() && disallowedPoints.Count() >= 2)
			{
				var disallowedTime =
					disallowedPoints.LastOrDefault().Time -
					disallowedPoints.FirstOrDefault().Time;

				totalTime = timeDiff.Subtract(disallowedTime);
			}
			else
			{
				totalTime = timeDiff;
			}

			totalTime /= 60;

			{
				using var Database = new SkuldDbContextFactory()
					.CreateDbContext();

				var skUser = await
					Database.InsertOrGetUserAsync(state.User)
				.ConfigureAwait(false);

				var skGuild = await
					Database.InsertOrGetGuildAsync(state.Guild)
				.ConfigureAwait(false);

				ulong maxAmount = (ulong)Math.Round(Configuration.VoiceExpMaxGrant * skGuild.XPModifier, 0);

				var xpToGrant = DatabaseUtilities.GetExpMultiFromMinutesInVoice(
					Configuration.VoiceExpDeterminate,
					Configuration.VoiceExpMinMinutes,
					maxAmount,
					totalTime
				);

				await skUser.GrantExperienceAsync(xpToGrant,
					state.Channel.Guild,
					ExperienceService.VoiceAction
				).ConfigureAwait(false);

				var voiceXp = Database.UserXp.FirstOrDefault(x =>
					x.UserId == skUser.Id &&
					x.GuildId == state.Channel.Guild.Id
				);

				voiceXp.TimeInVoiceM = totalTime.Subtract(
					Configuration.VoiceExpMinMinutes
				);

				await Database.SaveChangesAsync().ConfigureAwait(false);

				var baseXpToGrant = DatabaseUtilities.GetExpMultiFromMinutesInVoice(
					Configuration.VoiceExpDeterminate,
					Configuration.VoiceExpMinMinutes,
					Configuration.VoiceExpMaxGrant,
					totalTime
				);

				await skUser.GrantExperienceAsync(baseXpToGrant, guild: null, null).ConfigureAwait(false);

				voiceXp = Database.UserXp.FirstOrDefault(x =>
					x.UserId == skUser.Id &&
					x.GuildId == 0
				);

				voiceXp.TimeInVoiceM = totalTime.Subtract(
					Configuration.VoiceExpMinMinutes
				);

				await Database.SaveChangesAsync().ConfigureAwait(false);
			}
		}

		private static async Task UserVoiceStateUpdated(SocketUser user,
			SocketVoiceState previousState,
			SocketVoiceState currentState
		)
		{
			if (user.IsBot || user.IsWebhook) return;

			SocketVoiceChannel channel = GetVoiceChannel(
				previousState,
				currentState
			);

			SocketGuild guild = channel.Guild;

			SocketGuildUser guildUser = guild.GetUser(user.Id);

			VoiceStateTrackable state = new()
			{
				User = guildUser,
				Channel = channel,
				Guild = guild,
				VoiceState = currentState
			};

			if (!Trackables.Contains(state))
			{
				Trackables.Add(state);
			}

			GuildFeatures feats = null;

			{
				using var Database = new SkuldDbContextFactory().CreateDbContext();
				feats = Database.Features.Find(guild.Id);
			}

			if (feats is not null && feats.Experience)
			{
				await
					ProcessUser(state,
					VoiceStateDifference
						.SetStateDifference(previousState, currentState)
				).ConfigureAwait(false);
			}
		}

		private static async Task ProcessUser(
			VoiceStateTrackable state,
			VoiceStateDifference difference)
		{


			if (difference.DidConnect) // Connect
			{
				if (difference.IsAlone ||
					difference.IsAloneWithBot ||
					difference.DidMoveToAFKChannel ||
					(difference.DidMute || difference.DidDeafen) is true)
				{
					Targets.Add(
						new VoiceEvent(
							state.Channel,
							state.Guild,
							state.User,
							DateTime.UtcNow.ToEpoch(),
							false
						)
					);
					return;
				}
				else
				{
					Targets.Add(
						new VoiceEvent(
							state.Channel,
							state.Guild,
							state.User,
							DateTime.UtcNow.ToEpoch(),
							true
						)
					);
					return;
				}
			}

			if (difference.DidDisconnect)
			{
				if (Targets.Any(x => x.User.Id == state.User.Id))
				{
					await
						DoLeaveXpGrantAsync(state)
					.ConfigureAwait(false);
				}
				return;
			}

			if (difference.IsAlone ||
				difference.IsAloneWithBot ||
				difference.DidMoveToAFKChannel ||
				(difference.DidMute || difference.DidDeafen) is true)
			{
				Targets.Add(
					new VoiceEvent(
						state.Channel,
						state.Guild,
						state.User,
						DateTime.UtcNow.ToEpoch(),
						false
					)
				);
				return;
			}
			else
			{
				Targets.Add(
					new VoiceEvent(
						state.Channel,
						state.Guild,
						state.User,
						DateTime.UtcNow.ToEpoch(),
						true
					)
				); return;
			}
		}

		private struct VoiceStateTrackable
		{
			public SocketGuildUser User;
			public SocketVoiceChannel Channel;
			public SocketGuild Guild;
			public SocketVoiceState VoiceState;
		}

		private struct VoiceStateDifference
		{
			public bool DidSelfMute { get; private set; }
			public bool DidSelfDeafen { get; private set; }
			public bool DidServerMute { get; private set; }
			public bool DidServerDeafen { get; private set; }
			public bool DidDeafen { get => DidSelfDeafen || DidServerDeafen; }
			public bool DidMute { get => DidSelfMute || DidServerMute; }
			public bool IsAloneWithBot
			{
				get
				{
					return UserDifference.Count is 2 &&
						UserDifference.Any(x => x.IsBot);
				}
			}
			public bool IsAlone { get => UserDifference.Count is 1; }
			public bool DidMoveToAFKChannel { get; private set; }
			public bool DidDisconnect { get; private set; }
			public bool DidConnect { get; private set; }
			public IList<SocketGuildUser> UserDifference { get; private set; }

			public static VoiceStateDifference SetStateDifference(
				SocketVoiceState previousState,
				SocketVoiceState newState
			)
			{
				var difference = new VoiceStateDifference
				{
					DidSelfMute = !previousState.IsSelfMuted &&
								   newState.IsSelfMuted,

					DidSelfDeafen = !previousState.IsSelfDeafened &&
									 newState.IsSelfDeafened,

					DidServerMute = !previousState.IsMuted &&
									 newState.IsMuted,

					DidServerDeafen = !previousState.IsDeafened &&
									   newState.IsDeafened,

					DidDisconnect = newState.VoiceChannel is null,

					DidConnect = previousState.VoiceChannel is null &&
								 newState.VoiceChannel is not null
				};

				if (newState.VoiceChannel is not null)
				{
					SocketVoiceChannel afkChannel =
						newState.VoiceChannel.Guild.AFKChannel;

					difference.DidMoveToAFKChannel =
						afkChannel is not null && newState.VoiceChannel == afkChannel;
				}
				else
				{
					difference.DidMoveToAFKChannel = false;
				}

				if (newState.VoiceChannel is null &&
					previousState.VoiceChannel is not null)
				{
					difference.UserDifference =
						previousState.VoiceChannel.Users.ToList();
				}
				else if (newState.VoiceChannel is not null &&
					previousState.VoiceChannel is not null)
				{
					difference.UserDifference =
						newState.VoiceChannel.Users.Where(
							x => !previousState.VoiceChannel.Users.Contains(x)
						).ToList();
				}
				else if (newState.VoiceChannel is not null &&
					previousState.VoiceChannel is null)
				{
					difference.UserDifference =
						newState.VoiceChannel.Users.ToList();
				}

				return difference;
			}
		}
	}
}
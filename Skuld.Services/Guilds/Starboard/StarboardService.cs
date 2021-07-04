using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Skuld.Core.Extensions;
using Skuld.Core.Extensions.Conversion;
using Skuld.Core.Extensions.Verification;
using Skuld.Core.Utilities;
using Skuld.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Skuld.Services.Guilds.Starboard
{
	public static class StarboardService
	{
		const string Key = "StarboardService";
		private static readonly Color StarboardNewEntry = new(255, 85, 0);
		private static readonly Color StarboardColor = new(255, 255, 0);

		static Color StarColour(double current, ulong max)
		{
			double value = (current - 0) / (max - 0);

			value = Math.Clamp(value, 0, 1);

			return StarboardNewEntry.Lerp(StarboardColor, value);
		}

		#region Events
		public static async Task ExecuteAdditionAsync(IMessage message, ISocketMessageChannel channel, SocketReaction reaction)
		{
			if (message is null) return;
			if (channel is null) return;
			try
			{
				if (channel is ITextChannel guildChannel)
				{
					var guild = guildChannel.Guild;
					var botUser = await guild.GetCurrentUserAsync().ConfigureAwait(false);

					using var Database = new SkuldDbContextFactory().CreateDbContext();
					var feats = Database.Features.Find(guild.Id);
					var blacklist = Database.StarboardBlacklist.ToList().Where(x => x.GuildId == guild.Id);
					var whitelist = Database.StarboardWhitelist.ToList().Where(x => x.GuildId == guild.Id);

					if (feats.Starboard)
					{
						var gld = await Database.InsertOrGetGuildAsync(guild).ConfigureAwait(false);
						if (!await IsWhitelistedAsync(gld, message, guildChannel, reaction.UserId).ConfigureAwait(false)) return;

						if (gld.StarEmote == reaction.Emote.ToString())
						{
							IEmote emote;
							if (gld.StarEmote.Contains(":"))
							{
								emote = Emote.Parse(gld.StarEmote);
							}
							else
							{
								emote = new Emoji(gld.StarEmote);
							}
							if (message.Reactions.TryGetValue(emote, out ReactionMetadata reactions))
							{
								if (Database.StarboardVotes.Any(x => x.MessageId == message.Id) ||
									Database.StarboardVotes.Any(x => x.StarboardMessageId == message.Id)
								)
								{
									if (!Database.StarboardVotes.Any(x => x.MessageId == message.Id && x.VoterId == reaction.UserId) ||
										!Database.StarboardVotes.Any(x => x.StarboardMessageId == message.Id && x.VoterId == reaction.UserId))
									{
										if (Database.StarboardVotes.Any(x => x.MessageId == message.Id && x.StarboardMessageId != 0))
										{
											if (message.Timestamp >= DateTime.UtcNow.AddDays(-7))
											{
												var starboardedMessage = Database.StarboardVotes.FirstOrDefault(x => x.MessageId == message.Id && x.StarboardMessageId != 0);

												Database.StarboardVotes.Add(new StarboardVote
												{
													MessageId = starboardedMessage.MessageId,
													VoterId = reaction.UserId,
													StarboardMessageId = starboardedMessage.StarboardMessageId,
													MessageAuthorId = starboardedMessage.MessageAuthorId,
													ChannelId = starboardedMessage.ChannelId,
													GuildId = starboardedMessage.GuildId,
													WasSourceMessageReaction = true
												});

												await Database.SaveChangesAsync().ConfigureAwait(false);

												await UpdateMessageAsync(guild, gld, starboardedMessage.StarboardMessageId, Database.StarboardVotes.Count(x => x.MessageId == message.Id), message).ConfigureAwait(false);
											}
										}
										else if (Database.StarboardVotes.Any(x => x.MessageId == message.Id && x.StarboardMessageId == 0))
										{
											if (message.Timestamp >= DateTime.UtcNow.AddDays(-7))
											{
												var vote = new StarboardVote
												{
													MessageId = message.Id,
													VoterId = reaction.UserId,
													MessageAuthorId = message.Author.Id,
													ChannelId = message.Channel.Id,
													GuildId = guild.Id,
													WasSourceMessageReaction = true
												};

												var reactionCount = Database.StarboardVotes.Count(x => x.MessageId == message.Id) + 1;

												if (reactionCount >= gld.StarReactAmount)
												{
													var sentMessage = await SendMessageAsync(message, guild, gld, reactionCount).ConfigureAwait(false);

													vote.StarboardMessageId = sentMessage.Id;

													await Database.StarboardVotes.ForEachAsync(x =>
													{
														if (x.MessageId == message.Id)
														{
															x.StarboardMessageId = sentMessage.Id;
														}
													}).ConfigureAwait(false);
												}

												Database.StarboardVotes.Add(vote);

												await Database.SaveChangesAsync().ConfigureAwait(false);
											}
										}
										else if (Database.StarboardVotes.Any(x => x.StarboardMessageId == message.Id))
										{
											if (!Database.StarboardVotes.Any(x => x.StarboardMessageId == message.Id && x.VoterId == reaction.UserId))
											{
												var starboardedMessage = Database.StarboardVotes.FirstOrDefault(x => x.StarboardMessageId == message.Id);
												if (!gld.SelfStarring && reaction.UserId == starboardedMessage.MessageAuthorId) return;

												Database.StarboardVotes.Add(new StarboardVote
												{
													MessageId = starboardedMessage.MessageId,
													VoterId = reaction.UserId,
													StarboardMessageId = starboardedMessage.StarboardMessageId,
													MessageAuthorId = starboardedMessage.MessageAuthorId,
													ChannelId = starboardedMessage.ChannelId,
													GuildId = starboardedMessage.GuildId,
													WasSourceMessageReaction = false
												});

												await Database.SaveChangesAsync().ConfigureAwait(false);

												await UpdateMessageAsync(guild, gld, starboardedMessage.StarboardMessageId, Database.StarboardVotes.Count(x => x.StarboardMessageId == message.Id), message).ConfigureAwait(false);
											}
										}
									}
								}
								else
								{
									if (message.Timestamp >= DateTime.UtcNow.AddDays(-7))
									{
										Database.StarboardVotes.Add(new StarboardVote
										{
											MessageId = message.Id,
											VoterId = reaction.UserId,
											MessageAuthorId = message.Author.Id,
											ChannelId = message.Channel.Id,
											GuildId = guild.Id,
											WasSourceMessageReaction = true
										});

										await Database.SaveChangesAsync().ConfigureAwait(false);
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

		public static async Task ExecuteRemovalAsync(IMessage message, ulong reactorId)
		{
			if (message is null) return;
			try
			{
				if (message.Channel is IGuildChannel guildChannel)
				{
					var guild = guildChannel.Guild;

					using var Database = new SkuldDbContextFactory().CreateDbContext();
					var gld = Database.Guilds.Find(guild.Id);

					StarboardVote starboardVote = Database.StarboardVotes.FirstOrDefault(x => x.MessageId == message.Id && x.VoterId == reactorId);

					if (starboardVote is null)
					{
						starboardVote = Database.StarboardVotes.FirstOrDefault(x => x.StarboardMessageId == message.Id && x.VoterId == reactorId);
					}

					bool didChange = false;

					if (starboardVote is not null)
					{
						bool isStarboardMessage = starboardVote.StarboardMessageId == message.Id;

						if (starboardVote.WasSourceMessageReaction && starboardVote.MessageId == message.Id)
						{
							if (message.Timestamp >= DateTime.UtcNow.AddDays(-7))
							{
								Database.StarboardVotes.Remove(starboardVote);
								didChange = true;
							}
						}
						else if (!starboardVote.WasSourceMessageReaction && starboardVote.StarboardMessageId == message.Id)
						{
							isStarboardMessage = true;
							Database.StarboardVotes.Remove(starboardVote);
							didChange = true;
						}

						if (didChange)
						{
							await Database.SaveChangesAsync().ConfigureAwait(false);

							int totalCount = 0;

							if (Database.StarboardVotes.Any(x => x.MessageId == message.Id))
							{
								totalCount = Database.StarboardVotes.Count(x => x.MessageId == message.Id);
							}
							else if (Database.StarboardVotes.Any(x => x.StarboardMessageId == message.Id))
							{
								totalCount = Database.StarboardVotes.Count(x => x.StarboardMessageId == message.Id);
							}

							if (totalCount is 0 && isStarboardMessage)
							{
								var chan = message.Channel as ITextChannel;

								var txtChan = await chan.Guild.GetTextChannelAsync(gld.StarboardChannel).ConfigureAwait(false);

								IMessage msg = await txtChan.GetMessageAsync(starboardVote.StarboardMessageId).ConfigureAwait(false);

								await msg.DeleteAsync().ConfigureAwait(false);
							}
							else
							{
								if (starboardVote.StarboardMessageId != 0)
								{
									await UpdateMessageAsync(guild, gld, starboardVote.StarboardMessageId, totalCount, message).ConfigureAwait(false);
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
		#endregion

		static async Task<bool> IsWhitelistedAsync(Guild dbGuild, IMessage message, ITextChannel channel, ulong reactorId)
		{
			if (!dbGuild.SelfStarring && reactorId == message.Author.Id) return false;

			using var Database = new SkuldDbContextFactory().CreateDbContext();
			var blacklist = Database.StarboardBlacklist.ToList().Where(x => x.GuildId == channel.Guild.Id);
			var whitelist = Database.StarboardWhitelist.ToList().Where(x => x.GuildId == channel.Guild.Id);

			if (dbGuild.StarboardChannel is 0) return false;

			var chan = await channel.Guild.GetTextChannelAsync(dbGuild.StarboardChannel).ConfigureAwait(false);
			if (chan is null) return false;

			if (channel.IsNsfw && !chan.IsNsfw) return false;

			if (!blacklist.Any()) return true;
			if (blacklist.Any(x => x.TargetId == reactorId)) return false;

			if (channel.CategoryId is not null)
			{
				if (blacklist.Any(x => x.TargetId == channel.CategoryId))
				{
					if (whitelist.Any(x => x.TargetId == channel.Id)) return true;

					return false;
				}
			}
			else
			{
				if (blacklist.Any(x => x.TargetId == channel.Id)) return false;
			}

			return true;
		}

		#region Messages
		static async Task<IUserMessage> SendMessageAsync(IMessage message, IGuild guild, Guild dbGuild, int Reactions)
		{
			var embed = new EmbedBuilder()
					.WithAuthor(message.Author.FullName(), message.Author.GetAvatarUrl() ?? message.Author.GetDefaultAvatarUrl())
					.WithDescription(message.Content)
					.WithColor(StarColour(Reactions, 30))
					.WithFooter($"MessageID: {message.Id}")
					.WithTimestamp(message.CreatedAt);

			if (message.Attachments.Any())
			{
				var attachment = message.Attachments.FirstOrDefault();

				if (attachment.Url.IsImageExtension())
				{
					embed.WithImageUrl(attachment.Url);
				}
				else
				{
					embed.WithDescription(embed.Description + $"\n[With Attachment]({attachment.Url})");
				}
			}
			else if (message.Embeds.Any())
			{
				var emb = message.Embeds.FirstOrDefault();

				if (string.IsNullOrEmpty(message.Content) && !string.IsNullOrEmpty(emb.Description))
				{
					embed = embed.WithDescription(emb.Description);
				}

				if (emb.Image.HasValue)
				{
					embed.WithImageUrl(emb.Image.Value.Url);
				}
				else if (emb.Video.HasValue)
				{
					embed.WithDescription(embed.Description + $"\n[With Video]({emb.Video.Value.Url})");
				}
				else if (emb.Url.IsImageExtension())
				{
					embed.WithImageUrl(emb.Url);
				}
			}

			embed.WithDescription(embed.Description);

			embed.AddField("View Original", $"[Message Link]({message.GetJumpUrl()})", true);

			string reactionRange = dbGuild.StarEmote;

			if (Reactions >= 10)
			{
				reactionRange = dbGuild.StarRange1;
			}
			if (Reactions >= 20)
			{
				reactionRange = dbGuild.StarRange2;
			}
			if (Reactions >= 30)
			{
				reactionRange = dbGuild.StarRange3;
			}

			var chan = await guild.GetTextChannelAsync(dbGuild.StarboardChannel).ConfigureAwait(false);

			var msg = await chan.SendMessageAsync($"{reactionRange} {Reactions} | <#{message.Channel.Id}>", embed: embed.Build()).ConfigureAwait(false);

			if (Emote.TryParse(dbGuild.StarEmote, out var emote))
			{
				await msg.AddReactionAsync(emote).ConfigureAwait(false);
			}
			else
			{
				await msg.AddReactionAsync(new Emoji(dbGuild.StarEmote)).ConfigureAwait(false);
			}

			return msg;
		}

		static async Task<bool> UpdateMessageAsync(IGuild guild, Guild dbGuild, ulong starboardedMessage, int Reactions, IMessage fallbackMessage)
		{
			var starboard = await guild.GetTextChannelAsync(dbGuild.StarboardChannel).ConfigureAwait(false);

			var msg = await starboard.GetMessageAsync(starboardedMessage).ConfigureAwait(false);

			if (msg is null && fallbackMessage is not IUserMessage)
			{
				msg = await SendMessageAsync(fallbackMessage, guild, dbGuild, Reactions).ConfigureAwait(false);
			}

			if (msg is not IUserMessage) return false;

			if (Reactions < dbGuild.StarRemoveAmount)
			{
				using var database = new SkuldDbContextFactory().CreateDbContext();

				database.StarboardVotes.ToList().Where(x => x.StarboardMessageId == starboardedMessage && !x.WasSourceMessageReaction).ToList().ForEach(x =>
				  {
					  x.StarboardMessageId = 0;
				  });

				await database.SaveChangesAsync().ConfigureAwait(false);

				await msg.DeleteAsync().ConfigureAwait(false);

				return false;
			}
			else
			{
				string reactionRange = dbGuild.StarEmote;

				if (Reactions >= 10)
				{
					reactionRange = dbGuild.StarRange1;
				}
				if (Reactions >= 20)
				{
					reactionRange = dbGuild.StarRange2;
				}
				if (Reactions >= 30)
				{
					reactionRange = dbGuild.StarRange3;
				}

				var splitSection = msg.Content.Split(" | ");

				await (msg as IUserMessage).ModifyAsync(x =>
				{
					x.Content = $"{reactionRange} {Reactions} | {splitSection[1]}";
					if (x.Embed.IsSpecified)
					{
						x.Embed = x.Embed.GetValueOrDefault()
										.ToEmbedBuilder()
										.WithColor(StarColour(Reactions, 30))
										.Build();
					}
				}).ConfigureAwait(false);

				return true;
			}
		}
		#endregion Messages
	}
}

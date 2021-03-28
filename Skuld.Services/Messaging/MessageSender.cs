using Discord;
using Skuld.Core.Extensions;
using Skuld.Core.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Skuld.Services.Messaging
{
	internal static class MessageSender
	{
		private const string Key = "MsgDisp";

		public static async Task<IUserMessage> ReplyToAsync(this IUserMessage replyMessage,
			string message,
			Embed embed = null,
			string[] addReactions = null,
			Stream file = null,
			string fileName = "filename.file",
			int timeout = -1
		)
		{
			try
			{
				await replyMessage.Channel.TriggerTypingAsync().ConfigureAwait(false);

				if (replyMessage.Channel is IGuildChannel)
				{
					if ((replyMessage.Channel as IGuildChannel).Guild != null)
					{
						Log.Info(Key, $"Dispatched message to {(replyMessage.Channel as IGuildChannel).Guild} in {(replyMessage.Channel as IGuildChannel).Name}");
					}
				}
				else
				{
					Log.Info(Key, $"Dispatched message to {replyMessage.Channel.Name}");
				}

				IUserMessage createdMessage;

				if (file == null)
				{
					createdMessage =
						await
							replyMessage.ReplyAsync(message, false, embed)
						.ConfigureAwait(false);

					if (timeout != -1)
					{
						await createdMessage.DeleteAfterSecondsAsync(timeout).ConfigureAwait(false);
					}
				}
				else
				{
					createdMessage =
						await
							replyMessage.Channel.SendFileAsync(file, fileName, message, false, embed, null, false, null, replyMessage.Reference)
						.ConfigureAwait(false);

					if (timeout != -1)
					{
						await createdMessage.DeleteAfterSecondsAsync(timeout).ConfigureAwait(false);
					}
				}

				if (createdMessage is not null)
				{
					List<IEmote> formattedAddReactions = new();

					foreach (string reaction in addReactions)
					{
						if (reaction.StartsWith(":") || reaction.StartsWith("<"))
						{
							formattedAddReactions.Add(Emote.Parse(reaction));
						}
						else
						{
							formattedAddReactions.Add(new Emoji(EmojiOne.EmojiOne.AsciiToUnicode(reaction)));
						}
					}

					await createdMessage.AddReactionsAsync(formattedAddReactions.ToArray());
				}

				return createdMessage;
			}
			catch (Exception ex)
			{
				Log.Error(Key, $"Failed sending message to {replyMessage.Channel.Name}", null, ex);
				return null;
			}
		}

		public static async Task<IUserMessage> SendMessageTo(
			IMessageChannel channel,
			string message,
			Embed embed = null,
			string[] addReactions = null,
			Stream file = null,
			string fileName = "filename.file",
			int timeout = -1)
		{
			if (channel == null) { return null; }

			try
			{
				await channel.TriggerTypingAsync().ConfigureAwait(false);

				if (channel is IGuildChannel)
				{
					if ((channel as IGuildChannel).Guild != null)
					{
						Log.Info(Key, $"Dispatched message to {(channel as IGuildChannel).Guild} in {(channel as IGuildChannel).Name}");
					}
				}
				else
				{
					Log.Info(Key, $"Dispatched message to {channel.Name}");
				}

				IUserMessage createdMessage;

				if (file == null)
				{
					createdMessage =
						await
							channel.SendMessageAsync(message, false, embed)
						.ConfigureAwait(false);

					if (timeout != -1)
					{
						await createdMessage.DeleteAfterSecondsAsync(timeout).ConfigureAwait(false);
					}
				}
				else
				{
					createdMessage =
						await
							channel.SendFileAsync(file, fileName, message, false, embed)
						.ConfigureAwait(false);

					if (timeout != -1)
					{
						await createdMessage.DeleteAfterSecondsAsync(timeout).ConfigureAwait(false);
					}
				}

				if (createdMessage is not null)
				{
					List<IEmote> formattedAddReactions = new();

					foreach (string reaction in addReactions)
					{
						if (reaction.StartsWith(":") || reaction.StartsWith("<"))
						{
							formattedAddReactions.Add(Emote.Parse(reaction));
						}
						else
						{
							formattedAddReactions.Add(new Emoji(EmojiOne.EmojiOne.AsciiToUnicode(reaction)));
						}
					}

					await createdMessage.AddReactionsAsync(formattedAddReactions.ToArray());
				}

				return createdMessage;
			}
			catch (Exception ex)
			{
				Log.Error(Key, $"Failed sending message to {channel.Name}", null, ex);
				return null;
			}
		}

		public static async Task<IUserMessage> SendMessageToUser(
			IUser user,
			string message,
			Embed embed = null,
			string[] addReactions = null,
			Stream file = null,
			string fileName = "filename.file",
			int timeout = -1
		)
			=> await SendMessageTo(await user.GetOrCreateDMChannelAsync().ConfigureAwait(false), message, embed, addReactions, file, fileName, timeout).ConfigureAwait(false);
	}
}
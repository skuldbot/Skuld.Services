using Discord;
using Discord.Commands;
using Skuld.Core.Utilities;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Skuld.Services.Messaging.Extensions
{
	public static class MessageQueueExtensions
	{
		private static async Task<IUserMessage> SendMessageAsync(string content,
											 ICommandContext context,
											 bool reply = true,
											 Embed embed = null,
											 Stream fileStream = null,
											 string fileName = "filename.file",
											 Models.MessageType type = Models.MessageType.Standard,
											 string[] addReactions = null,
											 Exception exception = null,
											 int timeout = -1)
		{
			if (exception is not null)
			{
				Log.Debug("MQEx", exception.Message, context, exception);
			}

			switch (type)
			{
				case Models.MessageType.Standard:
					if (!reply)
						return await MessageSender.SendMessageTo(context.Channel, content, embed, addReactions).ConfigureAwait(false);
					else
						return await context.Message.ReplyToAsync(content, embed, addReactions, fileStream, fileName, timeout);

				case Models.MessageType.Mention:
					if (!reply)
						return await MessageSender.SendMessageTo(context.Channel, $"{context.User.Mention} {content}", embed, addReactions).ConfigureAwait(false);
					else
						return await context.Message.ReplyToAsync(content, embed, addReactions, fileStream, fileName, timeout);

				case Models.MessageType.DMS:
					if (!reply)
						return await MessageSender.SendMessageToUser(context.User, content, embed: embed, addReactions).ConfigureAwait(false);
					else
						return await context.Message.ReplyToAsync(content, embed, addReactions, fileStream, fileName, timeout);

				case Models.MessageType.Timed:
					if (!reply)
						await MessageSender.SendMessageTo(context.Channel, content, embed, addReactions, timeout: timeout).ConfigureAwait(false);
					else
						await context.Message.ReplyToAsync(content, embed, addReactions, fileStream, fileName, timeout);
					return null;

				default:
					return null;
			}
		}

		#region String
		public static async Task<IUserMessage> QueueMessageAsync(this string content,
											 ICommandContext context,
											 bool reply = true,
											 Embed embed = null,
											 Stream fileStream = null,
											 string fileName = "filename.file",
											 Models.MessageType type = Models.MessageType.Standard,
											 string[] addReactions = null,
											 Exception exception = null,
											 int timeout = -1)
			=> await SendMessageAsync(content, context, reply, embed, fileStream, fileName, type, addReactions, exception, timeout).ConfigureAwait(false);

		public static async Task<IUserMessage> QueueMessageAsync(this StringBuilder content,
											 ICommandContext context,
											 bool reply = true,
											 Embed embed = null,
											 Stream fileStream = null,
											 string fileName = "filename.file",
											 Models.MessageType type = Models.MessageType.Standard,
											 string[] addReactions = null,
											 Exception exception = null,
											 int timeout = -1)
			=> await SendMessageAsync(content.ToString(), context, reply, embed, fileStream, fileName, type, addReactions, exception, timeout).ConfigureAwait(false);
		#endregion String

		#region Embeds
		public static async Task<IUserMessage> QueueMessageAsync(this Embed embed,
											 ICommandContext context,
											 bool reply = true,
											 string content = "",
											 Stream fileStream = null,
											 string fileName = "filename.file",
											 Models.MessageType type = Models.MessageType.Standard,
											 string[] addReactions = null,
											 Exception exception = null,
											 int timeout = -1)
			=> await SendMessageAsync(content, context, reply, embed, fileStream, fileName, type, addReactions, exception, timeout).ConfigureAwait(false);


		public static async Task<IUserMessage> QueueMessageAsync(this EmbedBuilder embed,
											 ICommandContext context,
											 bool reply = true,
											 string content = "",
											 Stream fileStream = null,
											 string fileName = "filename.file",
											 Models.MessageType type = Models.MessageType.Standard,
											 string[] addReactions = null,
											 Exception exception = null,
											 int timeout = -1)
			=> await SendMessageAsync(content, context, reply, embed.Build(), fileStream, fileName, type, addReactions, exception, timeout).ConfigureAwait(false);
		#endregion Embeds

		public static async Task<IUserMessage> QueueMessageAsync(this object obj,
											 ICommandContext context,
											 bool reply = true,
											 Stream filestream = null,
											 string filename = "filename.file",
											 Models.MessageType type = Models.MessageType.Standard,
											 string[] addReactions = null,
											 Exception exception = null,
											 int timeout = -1)
			=> obj switch
			{
				EmbedBuilder content => await SendMessageAsync("", context, reply, content.Build(), filestream, filename, type, addReactions, exception, timeout).ConfigureAwait(false),
				Embed content => await SendMessageAsync("", context, reply, content, filestream, filename, type, addReactions, exception, timeout).ConfigureAwait(false),
				string content => await SendMessageAsync(content, context, reply, null, filestream, filename, type, addReactions, exception, timeout).ConfigureAwait(false),
				StringBuilder content => await SendMessageAsync(content.ToString(), context, reply, null, filestream, filename, type, addReactions, exception, timeout).ConfigureAwait(false),
				_ => throw new NotSupportedException($"Can't support type: {obj.GetType()}")
			};
	}
}
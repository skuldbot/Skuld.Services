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
                                             Embed embed = null,
                                             Stream filestream = null,
                                             string filename = "filename.file",
                                             Models.MessageType type = Models.MessageType.Standard,
                                             Exception exception = null,
                                             int timeout = -1)
        {
            if(exception != null)
            {
                Log.Debug("MQEx", exception.Message, context, exception);
            }

            switch (type)
            {
                case Models.MessageType.Standard:
                    return await MessageSender.ReplyAsync(context.Channel, content, embed).ConfigureAwait(false);

                case Models.MessageType.Mention:
                    return await MessageSender.ReplyAsync(context.Channel, $"{context.User.Mention} {content}", embed).ConfigureAwait(false);

                case Models.MessageType.DMS:
                    return await MessageSender.ReplyAsync(context.User, content, embed).ConfigureAwait(false);

                case Models.MessageType.Timed:
                    await MessageSender.ReplyAsync(context.Channel, content, embed, timeout: timeout).ConfigureAwait(false);
                    return null;

                case Models.MessageType.File:
                    {
                        var msg = await MessageSender.ReplyAsync(context.Channel, content, embed, filestream, filename).ConfigureAwait(false);

                        if (filestream != null)
                            await filestream.DisposeAsync().ConfigureAwait(false);

                        return msg;
                    }

                case Models.MessageType.MentionFile:
                    {
                        var msg = await MessageSender.ReplyAsync(context.Channel, $"{context.User.Mention} {content}", embed, filestream, filename).ConfigureAwait(false);

                        if (filestream != null)
                            await filestream.DisposeAsync().ConfigureAwait(false);

                        return msg;
                    }

                default:
                    return null;
            }
        }

        #region String
        public static async Task<IUserMessage> QueueMessageAsync(this string content,
                                             ICommandContext context,
                                             Embed embed = null,
                                             Stream fileStream = null,
                                             string fileName = "filename.file",
                                             Models.MessageType type = Models.MessageType.Standard,
                                             Exception exception = null,
                                             int timeout = -1)
            => await SendMessageAsync(content, context, embed, fileStream, fileName, type, exception, timeout).ConfigureAwait(false);

        public static async Task<IUserMessage> QueueMessageAsync(this StringBuilder content,
                                             ICommandContext context,
                                             Embed embed = null,
                                             Stream fileStream = null,
                                             string fileName = "filename.file",
                                             Models.MessageType type = Models.MessageType.Standard,
                                             Exception exception = null,
                                             int timeout = -1)
            => await SendMessageAsync(content.ToString(), context, embed, fileStream, fileName, type, exception, timeout).ConfigureAwait(false);
        #endregion String

        #region Embeds
        public static async Task<IUserMessage> QueueMessageAsync(this Embed embed,
                                             ICommandContext context,
                                             string content = "",
                                             Stream fileStream = null,
                                             string fileName = "filename.file",
                                             Models.MessageType type = Models.MessageType.Standard,
                                             Exception exception = null,
                                             int timeout = -1)
            => await SendMessageAsync(content, context, embed, fileStream, fileName, type, exception, timeout).ConfigureAwait(false);
        

        public static async Task<IUserMessage> QueueMessageAsync(this EmbedBuilder embed,
                                             ICommandContext context,
                                             string content = "",
                                             Stream fileStream = null,
                                             string fileName = "filename.file",
                                             Models.MessageType type = Models.MessageType.Standard,
                                             Exception exception = null,
                                             int timeout = -1)
            => await SendMessageAsync(content, context, embed.Build(), fileStream, fileName, type, exception, timeout).ConfigureAwait(false);
        #endregion Embeds

        public static async Task<IUserMessage> QueueMessageAsync(this object obj,
                                             ICommandContext context,
                                             Stream filestream = null,
                                             string filename = "filename.file",
                                             Models.MessageType type = Models.MessageType.Standard,
                                             Exception exception = null,
                                             int timeout = -1)
            => obj switch
            {
                EmbedBuilder content => await SendMessageAsync("", context, content.Build(), filestream, filename, type, exception, timeout).ConfigureAwait(false),
                Embed content => await SendMessageAsync("", context, content, filestream, filename, type, exception, timeout).ConfigureAwait(false),
                string content => await SendMessageAsync(content, context, null, filestream, filename, type, exception, timeout).ConfigureAwait(false),
                StringBuilder content => await SendMessageAsync(content.ToString(), context, null, filestream, filename, type, exception, timeout).ConfigureAwait(false),
                _ => throw new NotSupportedException($"Can't support type: {obj.GetType()}")
            };
    }
}
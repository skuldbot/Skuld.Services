using Discord;
using Discord.Commands;
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
                                             Stream fileStream = null,
                                             string fileName = "image.png",
                                             Models.MessageType type = Models.MessageType.Standard,
                                             Exception exception = null,
                                             double timeout = 0.0)
        {
            switch (type)
            {
                case Models.MessageType.Standard:
                    return await MessageSender.ReplyAsync(context.Channel, content, embed).ConfigureAwait(false);

                case Models.MessageType.Mention:
                    return await MessageSender.ReplyWithMentionAsync(context.Channel, context.User, content, embed).ConfigureAwait(false);

                case Models.MessageType.DMS:
                    return await MessageSender.ReplyDMsAsync(await context.User.GetOrCreateDMChannelAsync().ConfigureAwait(false), context.Channel, content, embed).ConfigureAwait(false);

                case Models.MessageType.DMFail:
                    return await MessageSender.ReplyDMFailableAsync(await context.User.GetOrCreateDMChannelAsync().ConfigureAwait(false), content, embed).ConfigureAwait(false);

                case Models.MessageType.Timed:
                    await MessageSender.ReplyWithTimedMessage(context.Channel, content, embed, timeout).ConfigureAwait(false);
                    return null;

                case Models.MessageType.File:
                    {
                        var msg = await MessageSender.ReplyWithFileAsync(context.Channel, content, fileStream, fileName, embed).ConfigureAwait(false);

                        if (fileStream != null)
                            await fileStream.DisposeAsync().ConfigureAwait(false);

                        return msg;
                    }

                case Models.MessageType.MentionFile:
                    {
                        var msg = await MessageSender.ReplyWithFileAsync(context.Channel, content, fileStream, fileName, embed).ConfigureAwait(false);

                        if (fileStream != null)
                            await fileStream.DisposeAsync().ConfigureAwait(false);

                        return msg;
                    }
            }

            return null;
        }

        #region String
        public static async Task<IUserMessage> QueueMessageAsync(this string content,
                                             ICommandContext context,
                                             Embed embed = null,
                                             Stream fileStream = null,
                                             string fileName = "image.png",
                                             Models.MessageType type = Models.MessageType.Standard,
                                             Exception exception = null,
                                             double timeout = 0.0)
            => await SendMessageAsync(content, context, embed, fileStream, fileName, type, exception, timeout).ConfigureAwait(false);

        public static async Task<IUserMessage> QueueMessageAsync(this StringBuilder content,
                                             ICommandContext context,
                                             Embed embed = null,
                                             Stream fileStream = null,
                                             string fileName = "image.png",
                                             Models.MessageType type = Models.MessageType.Standard,
                                             Exception exception = null,
                                             double timeout = 0.0)
            => await SendMessageAsync(content.ToString(), context, embed, fileStream, fileName, type, exception, timeout).ConfigureAwait(false);
        #endregion String

        #region Embeds
        public static async Task<IUserMessage> QueueMessageAsync(this Embed embed,
                                             ICommandContext context,
                                             string content = "",
                                             Stream fileStream = null,
                                             string fileName = "image.png",
                                             Models.MessageType type = Models.MessageType.Standard,
                                             Exception exception = null,
                                             double timeout = 0.0)
            => await SendMessageAsync(content, context, embed, fileStream, fileName, type, exception, timeout).ConfigureAwait(false);
        

        public static async Task<IUserMessage> QueueMessageAsync(this EmbedBuilder embed,
                                             ICommandContext context,
                                             string content = "",
                                             Stream fileStream = null,
                                             string fileName = "image.png",
                                             Models.MessageType type = Models.MessageType.Standard,
                                             Exception exception = null,
                                             double timeout = 0.0)
            => await SendMessageAsync(content, context, embed.Build(), fileStream, fileName, type, exception, timeout).ConfigureAwait(false);
        #endregion Embeds

        public static async Task<IUserMessage> QueueMessageAsync(this object obj,
                                             ICommandContext context,
                                             Stream fileStream = null,
                                             string fileName = "image.png",
                                             Models.MessageType type = Models.MessageType.Standard,
                                             Exception exception = null,
                                             double timeout = 0.0)
            => obj switch
            {
                EmbedBuilder content => await SendMessageAsync("", context, content.Build(), fileStream, fileName, type, exception, timeout).ConfigureAwait(false),
                Embed content => await SendMessageAsync("", context, content, fileStream, fileName, type, exception, timeout).ConfigureAwait(false),
                string content => await SendMessageAsync(content, context, null, fileStream, fileName, type, exception, timeout).ConfigureAwait(false),
                StringBuilder content => await SendMessageAsync(content.ToString(), context, null, fileStream, fileName, type, exception, timeout).ConfigureAwait(false),
                _ => throw new NotSupportedException($"Can't support type: {obj.GetType()}")
            };
    }
}
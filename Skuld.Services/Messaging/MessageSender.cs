using Discord;
using Skuld.Core.Extensions;
using Skuld.Core.Utilities;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Skuld.Services.Messaging
{
    internal static class MessageSender
    {
        private const string Key = "MsgDisp";

        public static async Task<IUserMessage> ReplyAsync(IMessageChannel channel, string message, Embed embed = null, Stream file = null, string fileName = "filename.file", int timeout = -1)
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

                if (file == null)
                {
                    var msg = 
                        await 
                            channel.SendMessageAsync(message, false, embed)
                        .ConfigureAwait(false);

                    if (timeout != -1)
                    {
                        await msg.DeleteAfterSecondsAsync(timeout).ConfigureAwait(false);
                    }

                    return msg;
                }
                else
                {
                    var msg = 
                        await
                            channel.SendFileAsync(file, fileName, message, false, embed)
                        .ConfigureAwait(false);

                    if(timeout != -1)
                    {
                        await msg.DeleteAfterSecondsAsync(timeout).ConfigureAwait(false);
                    }

                    return msg;
                }
            }
            catch (Exception ex)
            {
                Log.Error(Key, $"Failed sending message to {channel.Name}", null, ex);
                return null;
            }
        }

        public static async Task<IUserMessage> ReplyAsync(IUser user, string message, Embed embed = null, Stream file = null, string fileName = "filename.file", int timeout = -1)
            => await ReplyAsync(await user.GetOrCreateDMChannelAsync().ConfigureAwait(false), message, embed, file, fileName, timeout).ConfigureAwait(false);
    }
}
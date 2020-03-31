using Discord;
using Discord.WebSocket;
using Skuld.Core.Extensions;
using Skuld.Core.Utilities;
using Skuld.Models;
using Skuld.Services.Bot;
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
                if(message is IGuildChannel guildChannel)
                {
                    var botUser = await guildChannel.Guild.GetCurrentUserAsync().ConfigureAwait(false);

                    return botUser.GetPermissions(guildChannel).ManageMessages;
                }
            }
            return true;
        }

        public static async Task ExecuteAdditionAsync(IMessage message, ISocketMessageChannel channel, SocketReaction reaction)
        {
            using var Database = new SkuldDbContextFactory().CreateDbContext();
            try
            {
                var guild = BotService.DiscordClient.Guilds.FirstOrDefault(x => x.TextChannels.FirstOrDefault(z => z.Id == channel.Id) != null);

                if (guild != null)
                {
                    var feats = Database.Features.FirstOrDefault(x => x.Id == guild.Id);

                    if (feats.Pinning)
                    {
                        var pins = await channel.GetPinnedMessagesAsync().ConfigureAwait(false);

                        if (pins.Count < 50)
                        {
                            int pinboardThreshold = BotService.Configuration.PinboardThreshold;
                            int pinboardReactions = message.Reactions.FirstOrDefault(x => x.Key.Name == "📌").Value.ReactionCount;

                            if (pinboardReactions >= pinboardThreshold)
                            {
                                var now = message.CreatedAt;
                                var dt = DateTime.UtcNow.AddDays(-BotService.Configuration.PinboardDateLimit);
                                if ((now - dt).TotalDays > 0)
                                {
                                    if (!message.IsPinned && await CanPinAsync(message, channel is IGuildChannel).ConfigureAwait(false))
                                    {
                                        if(message is IUserMessage msg)
                                            await msg.PinAsync().ConfigureAwait(false);

                                        Log.Info(Key, "Message reached threshold, pinned a message");

                                        return;
                                    }
                                }
                            }

                            await message.Channel.SendMessageAsync(
                                guild.Owner.Mention,
                                embed: 
                                new EmbedBuilder()
                                    .WithDescription($"Can't pin a message in this channel, please clean out some pins")
                                    .AddAuthor(BotService.DiscordClient)
                                    .WithTitle("Pinboard")
                                    .WithColor(Color.Red)
                                .Build()
                            ).ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Critical(Key, ex.Message, ex);
            }
        }
    }
}

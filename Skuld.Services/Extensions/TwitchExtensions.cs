using Discord;
using Skuld.Core.Extensions;
using Skuld.Core.Extensions.Formatting;
using Skuld.Core.Utilities;
using System;
using System.Threading.Tasks;
using TwitchLib.Api.V5.Models.Channels;
using TwitchLib.Api.V5.Models.Streams;
using TwitchLib.Api.V5.Models.Users;

namespace Skuld.Services.Extensions
{
	public static class TwitchExtensions
	{

        public static EmbedBuilder GetEmbed(this User user, Channel channel, Stream stream)
        {
            var name = user.DisplayName ?? user.Id;
            var iconurl = user.Logo ?? "";

            string twitchStatus = "";
            string channelIcon = "";

            switch (user.Type)
            {
                case "staff":
                    twitchStatus = DiscordUtilities.TwitchStaff.ToString();
                    break;

                case "admin":
                    twitchStatus = DiscordUtilities.TwitchAdmins.ToString();
                    break;

                case "mod":
                    twitchStatus = DiscordUtilities.TwitchGlobalMod.ToString();
                    break;
            }

            switch (channel.BroadcasterType)
            {
                case "partner":
                    channelIcon = DiscordUtilities.TwitchVerified.ToString();
                    break;

                case "affiliate":
                    channelIcon = DiscordUtilities.TwitchAffiliate.ToString();
                    break;
            }

            var embed = new EmbedBuilder
            {
                Author = new EmbedAuthorBuilder
                {
                    Name = name,
                    IconUrl = iconurl
                },
                Color = EmbedExtensions.RandomEmbedColor()
            };

            embed.Url = channel.Url;

            string channelBadges = null;

            if (twitchStatus is not null)
            {
                channelBadges += twitchStatus;
            }
            if (channelIcon is not null)
            {
                channelBadges += channelIcon;
            }

            if (channelBadges is not null)
            {
                embed.AddInlineField("Channel Badges", channelBadges);
            }

            if (stream is not null)
            {
                embed.Title = channel.Status;

                if (stream.Game is not null)
                {
                    embed.AddInlineField("Streaming", stream.Game);
                }
                embed.AddInlineField("Viewers 👀", stream.Viewers.ToFormattedString());

                if (stream.Preview is not null)
                {
                    embed.ImageUrl = stream.Preview.Large;
                }

                var uptime = DateTime.UtcNow.Subtract(stream.CreatedAt);

                embed.AddInlineField("Uptime", $"{uptime.ToDifferenceString()}");
            }
            else
            {
                embed.AddInlineField("Total Views", channel.Views.ToFormattedString());
            }

            return embed;
        }
    }
}

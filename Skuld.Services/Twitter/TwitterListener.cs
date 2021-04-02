﻿using Discord;
using Discord.WebSocket;
using Skuld.Core;
using Skuld.Core.Extensions;
using Skuld.Core.Extensions.Verification;
using Skuld.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using Tweetinvi;
using Tweetinvi.Core.Extensions;
using Tweetinvi.Events;
using Tweetinvi.Models;
using Tweetinvi.Streaming;

namespace Skuld.Services.Twitter
{
	public static class TwitterListener
	{
		static DiscordShardedClient DiscordClient;
		static readonly List<IFilteredStream> listeningStreams = new();

		public static void Configure(DiscordShardedClient client)
		{
			DiscordClient = client;
		}

		public static void Stop()
		{
			foreach (var stream in listeningStreams)
			{
				stream.StopStream();
				stream.FollowingUserIds.ForEach(z => stream.RemoveFollow(z.Key.Value));
			}

			listeningStreams.Clear();
		}

		public static void Run()
		{
			if (listeningStreams.Any())
			{
				Stop();
			}

			using SkuldDbContext database = new SkuldDbContextFactory().CreateDbContext();

			var twitterAccounts = database.TwitterFollows
				.AsNoTracking()
				.ToList()
				.GroupBy(x => x.ChannelId)
				.ToList();

			var config = database.Configurations.AsNoTracking().FirstOrDefault(c => c.Id == SkuldAppContext.ConfigurationId);

			Auth.SetUserCredentials(config.TwitterConKey, config.TwitterConSec, config.TwitterAccessTok, config.TwitterAccessSec);

			if (twitterAccounts.Any())
			{
				foreach (var group in twitterAccounts)
				{
					var stream = Stream.CreateFilteredStream();

					foreach (var entry in group.ToList())
					{
						stream.AddFollow(entry.TwitterAccId);
					}

					stream.MatchingTweetReceived += async (sender, e) => await NewTweetFromFilterAsync(e, group);

					Task.Run(() => stream.StartStreamMatchingAllConditions());

					listeningStreams.Add(stream);
				}
			}
		}

		static async Task NewTweetFromFilterAsync(MatchedTweetReceivedEventArgs args, IGrouping<ulong, GuildTwitterAccounts> group)
		{
			using SkuldDbContext database = new SkuldDbContextFactory().CreateDbContext();

			var gld = database.Guilds.AsNoTracking().FirstOrDefault(g => g.Id == group.ToList()[0].GuildId);

			string msg = $"New tweet from: {args.Tweet.CreatedBy.Name ?? args.Tweet.CreatedBy.ScreenName}";

			if (!gld.NewTweetMessage.IsNullOrWhiteSpace())
			{
				msg = gld.NewTweetMessage.ReplaceSocialEventMessage(args.Tweet.CreatedBy.Name ?? args.Tweet.CreatedBy.ScreenName, new Uri(args.Tweet.Url));
			}

			await DiscordClient.GetGuild(group.ToList()[0].GuildId).GetTextChannel(group.Key).SendMessageAsync(
				msg,
				embed: GetEmbedFromTweet(args.Tweet)
			).ConfigureAwait(false);
		}

		static Embed GetEmbedFromTweet(ITweet tweet)
			=> new EmbedBuilder()
				.WithAuthor(
					tweet.CreatedBy.Name ?? tweet.CreatedBy.ScreenName,
					tweet.CreatedBy.ProfileImageUrl400x400,
					$"https://twitter.com/{tweet.CreatedBy.ScreenName}"
				)
				.WithTitle($"{tweet.Text.Substring(0, 32)}...")
				.WithDescription(tweet.Text)
				.WithColor($"#{tweet.CreatedBy.ProfileBackgroundColor}".FromHex())
				.WithTimestamp(tweet.CreatedAt)
				.WithFooter("Tweeted at")
				.WithImageUrl(tweet.Media.Any() ? tweet.Media.FirstOrDefault(x => x.VideoDetails is null).MediaURLHttps : "")
				.WithUrl(tweet.Url)
				.Build();
	}
}

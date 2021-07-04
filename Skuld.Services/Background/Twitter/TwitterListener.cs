using Discord;
using Microsoft.EntityFrameworkCore;
using Skuld.Core;
using Skuld.Core.Extensions;
using Skuld.Core.Extensions.Verification;
using Skuld.Core.Utilities;
using Skuld.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tweetinvi;
using Tweetinvi.Core.Extensions;
using Tweetinvi.Events;
using Tweetinvi.Models;
using Tweetinvi.Streaming;

namespace Skuld.Services.Twitter
{
	public class TwitterListener : BackgroundService, IBackgroundService
	{
		IDiscordClient DiscordClient;
		readonly List<IFilteredStream> listeningStreams = new();
		const string Key = "TwitterService";
		ITwitterClient twitterClient;

		public TwitterListener(IDiscordClient discordClient, ITwitterClient tClient)
		{
			DiscordClient = discordClient;

			using var database = new SkuldDbContextFactory().CreateDbContext();

			var config = database.Configurations.AsNoTracking().FirstOrDefault(c => c.Id == SkuldAppContext.ConfigurationId);

			twitterClient = tClient;
			
			Log.Verbose(Key, "Configured Twitter", null);
		}

		public void Stop()
		{
			foreach (var stream in listeningStreams)
			{
				stream.Stop();
				stream.FollowingUserIds.ForEach(z => stream.RemoveFollow(z.Key.Value));
			}

			listeningStreams.Clear();
		}

		public override Task RunAsync(CancellationToken cancelToken)
		{
			if (cancelToken.IsCancellationRequested)
			{
				cancelToken.ThrowIfCancellationRequested();
			}

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

			if (twitterAccounts.Any())
			{
				foreach (var group in twitterAccounts)
				{
					var stream = twitterClient.Streams.CreateFilteredStream();

					foreach (var entry in group.ToList())
					{
						stream.AddFollow(entry.TwitterAccId);
					}

					stream.MatchingTweetReceived += async (sender, e) => await NewTweetFromFilterAsync(e, group);

					Task.Run(() => stream.StartMatchingAnyConditionAsync());

					listeningStreams.Add(stream);
				}
			}

			return Task.CompletedTask;
		}

		async Task NewTweetFromFilterAsync(MatchedTweetReceivedEventArgs args, IGrouping<ulong, GuildTwitterAccounts> group)
		{
			using SkuldDbContext database = new SkuldDbContextFactory().CreateDbContext();

			var gld = database.Guilds.AsNoTracking().FirstOrDefault(g => g.Id == group.ToList()[0].GuildId);

			string msg = $"New tweet from: {args.Tweet.CreatedBy.Name ?? args.Tweet.CreatedBy.ScreenName}";

			if (!gld.NewTweetMessage.IsNullOrWhiteSpace())
			{
				msg = gld.NewTweetMessage.ReplaceSocialEventMessage(args.Tweet.CreatedBy.Name ?? args.Tweet.CreatedBy.ScreenName, new Uri(args.Tweet.Url));
			}

			var guild = await DiscordClient.GetGuildAsync(group.ToList()[0].GuildId);

			var channel = await guild.GetTextChannelAsync(group.Key);
			
			await channel.SendMessageAsync(
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

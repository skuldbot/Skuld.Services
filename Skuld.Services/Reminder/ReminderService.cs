using Discord;
using Skuld.Core.Extensions;
using Skuld.Core.Utilities;
using Skuld.Models;
using Skuld.Services.Bot;
using StatsdClient;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Skuld.Services.Reminders
{
    public static class ReminderService
    {
        private static async Task ExecuteAsync()
        {
            using var Database = new SkuldDbContextFactory().CreateDbContext();

            while (true)
            {
                var currentTime = DateTime.UtcNow.ToEpoch();

                if (BotService.DiscordClient.Shards
                    .All(x=>x.ConnectionState == ConnectionState.Connected)
                )
                {
                    var reminders = Database.Reminders.ToList();

                    if (reminders.Any())
                    {
                        bool hasChanged = false;

                        reminders.ForEach(async reminder =>
                        {
                            if (reminder.Timeout <= currentTime)
                            {
                                try
                                {
                                    IUser creator =
                                        BotService.DiscordClient.GetUser(
                                            reminder.UserId
                                    );

                                    Embed embed = new EmbedBuilder()
                                        .WithTitle("⏰ Reminders")
                                        .WithDescription(
                                            $"{reminder.Content}\n\n" +
                                            "[Message Link]" +
                                            $"({reminder.MessageLink})"
                                        )
                                        .WithTimestamp(
                                            reminder.Created.FromEpoch()
                                        )
                                        .WithAuthor(creator)
                                        .WithFooter("Reminder Created")
                                        .Build();

                                    await
                                        (
                                            await creator
                                                .GetOrCreateDMChannelAsync()
                                            .ConfigureAwait(false)
                                        )
                                        .SendMessageAsync("", embed: embed)
                                    .ConfigureAwait(false);

                                    if (!reminder.Repeats)
                                    {
                                        Database.Reminders.Remove(reminder);
                                    }
                                    else
                                    {
                                        var diff = reminder.Timeout.Subtract(
                                            reminder.Created
                                        );

                                        reminder.Timeout += diff;
                                        DogStatsd.Increment(
                                            "reminders.repeat"
                                        );
                                    }
                                    DogStatsd.Increment(
                                        "reminders.processed"
                                    );
                                }
                                catch (Exception ex)
                                {
                                    Log.Critical("Reminders",
                                        ex.Message,
                                        null,
                                        ex
                                    );
                                    DogStatsd.Increment(
                                        "reminders.error"
                                    );
                                    Database.Reminders.RemoveRange(
                                        Database.Reminders.ToList().Where(
                                            x => x.UserId == reminder.UserId
                                        )
                                    );
                                }

                                hasChanged = true;
                            }
                        });

                        if (hasChanged)
                        {
                            await Database.SaveChangesAsync()
                                .ConfigureAwait(false);

                            hasChanged = false;
                        }
                    }
                }

                await Task.Delay(1000).ConfigureAwait(false);
            }
        }

        public static void Run()
        {
            Task.Run(async () => await ExecuteAsync().ConfigureAwait(false));
        }
    }
}
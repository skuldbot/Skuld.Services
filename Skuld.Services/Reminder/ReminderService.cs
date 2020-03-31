using Discord;
using Skuld.Core.Extensions;
using Skuld.Core.Extensions.Formatting;
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

                if (BotService.DiscordClient.Shards.All(x=>x.ConnectionState == ConnectionState.Connected))
                {
                    if (Database.Reminders.Any())
                    {
                        bool hasChanged = false;

                        foreach(var reminder in Database.Reminders.ToList())
                        {
                            if (reminder.Timeout <= currentTime)
                            {
                                try
                                {
                                    await
                                        (await BotService.DiscordClient.GetUser(reminder.UserId).GetOrCreateDMChannelAsync().ConfigureAwait(false))
                                        .SendMessageAsync($"On {reminder.Created.FromEpoch().ToDMYString()} you asked me to remind you: {reminder.Content}\n\n<{reminder.MessageLink}>")
                                    .ConfigureAwait(false);

                                    if (!reminder.Repeats)
                                    {
                                        Database.Reminders.Remove(reminder);
                                    }
                                    else
                                    {
                                        var diff = reminder.Timeout - reminder.Created;

                                        reminder.Timeout += diff;
                                        DogStatsd.Increment("reminders.repeat");
                                    }
                                    DogStatsd.Increment("reminders.processed");
                                }
                                catch (Exception ex)
                                {
                                    Log.Critical("Reminders", ex.Message, ex);
                                    DogStatsd.Increment("reminders.error");
                                    Database.Reminders.RemoveRange(Database.Reminders.ToList().Where(x => x.UserId == reminder.UserId));
                                }

                                hasChanged = true;
                            }
                        }

                        if (hasChanged)
                        {
                            await Database.SaveChangesAsync().ConfigureAwait(false);
                            hasChanged = false;
                        }
                    }
                }

                await Task.Delay(50).ConfigureAwait(false);
            }
        }

        public static void Run()
            => Task.Run(async () => await ExecuteAsync().ConfigureAwait(false));
    }
}
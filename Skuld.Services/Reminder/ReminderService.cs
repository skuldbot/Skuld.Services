using Discord;
using Skuld.Core.Extensions;
using Skuld.Core.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Skuld.Services.Reminders
{
    public class ReminderService
    {
        private static async Task ExecuteAsync()
        {
            while (true)
            {
                var currentTime = DateTime.UtcNow.ToEpoch();

                using var Database = new SkuldDbContextFactory().CreateDbContext();

                if(Database.Reminders.Any())
                {
                    bool hasChanged = false;

                    Database.Reminders.ToList().ForEach(async reminder =>
                    {
                        if (reminder.Timeout <= currentTime)
                        {
                            await
                                (await Bot.BotService.DiscordClient.GetDMChannelAsync(reminder.UserId) ?? Bot.BotService.DiscordClient.GetChannel(reminder.ChannelId) as IMessageChannel)
                                .SendMessageAsync($"On {reminder.Created.ToString("yyyy'/'MM'/'dd HH:mm:ss")} you asked me to remind you: {reminder.Content}\n\n<{reminder.MessageLink}>")
                            .ConfigureAwait(false);

                            Database.Reminders.Remove(reminder);

                            hasChanged = true;
                        }
                    });

                    if(hasChanged)
                    {
                        await Database.SaveChangesAsync().ConfigureAwait(false);
                    }
                }

                await Task.Delay(50).ConfigureAwait(false);
            }
        }

        public static void Run()
            => Task.Run(async () => await ExecuteAsync().ConfigureAwait(false));
    }
}
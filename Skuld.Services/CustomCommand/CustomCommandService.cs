using Discord.Commands;
using Skuld.Core.Utilities;
using Skuld.Models;
using StatsdClient;
using System.Linq;
using System.Threading.Tasks;

namespace Skuld.Services.CustomCommands
{
    public static class CustomCommandService
    {
        private static System.Diagnostics.Stopwatch watch = new System.Diagnostics.Stopwatch();

        public static async Task HandleCustomCommandAsync(ShardedCommandContext context, SkuldConfig config)
        {
            using var Database = new SkuldDbContextFactory().CreateDbContext();

            var prefix = MessageTools.GetPrefixFromCommand(context.Message.Content, config.Prefix, config.AltPrefix, (await Database.InsertOrGetGuildAsync(context.Guild).ConfigureAwait(false)).Prefix);
            if(prefix != null)
            {
                var name = MessageTools.GetCommandName(prefix, context.Message);
                var customcommand = Database.CustomCommands.FirstOrDefault(x => x.GuildId == context.Guild.Id && x.Name.ToLower() == name.ToLower());

                if (customcommand != null)
                {
                    await DispatchCommandAsync(context, customcommand).ConfigureAwait(false);
                    return;
                }
            }

            return;
        }

        public static async Task DispatchCommandAsync(ShardedCommandContext context, CustomCommand command)
        {
            using var Database = new SkuldDbContextFactory().CreateDbContext();

            watch.Start();
            await context.Channel.SendMessageAsync(command.Content).ConfigureAwait(false);
            watch.Stop();

            DogStatsd.Histogram("commands.latency", watch.ElapsedMilliseconds, 0.5, new[] { $"module:custom", $"cmd:{command.Name.ToLowerInvariant()}" });

            var usr = await Database.InsertOrGetUserAsync(context.User).ConfigureAwait(false);

            await InsertCommandAsync(command, usr).ConfigureAwait(false);

            DogStatsd.Increment("commands.processed", 1, 1, new[] { $"module:custom", $"cmd:{command.Name.ToLowerInvariant()}" });

            watch = new System.Diagnostics.Stopwatch();
        }

        private static async Task InsertCommandAsync(CustomCommand command, User user)
        {
            using var Database = new SkuldDbContextFactory().CreateDbContext();

            var experience = Database.UserCommandUsage.FirstOrDefault(x => x.UserId == user.Id && x.Command.ToLower() == command.Name.ToLower());
            if (experience != null)
            {
                experience.Usage += 1;
            }
            else
            {
                Database.UserCommandUsage.Add(new UserCommandUsage
                {
                    Command = command.Name.ToLower(),
                    UserId = user.Id,
                    Usage = 1
                });
            }

            await Database.SaveChangesAsync().ConfigureAwait(false);
        }
    }
}
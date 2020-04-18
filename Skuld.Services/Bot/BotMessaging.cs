using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Skuld.Core.Extensions;
using Skuld.Core.Extensions.Verification;
using Skuld.Core.Utilities;
using Skuld.Models;
using Skuld.Services.Accounts.Experience;
using Skuld.Services.CustomCommands;
using Skuld.Services.Messaging.Extensions;
using StatsdClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Skuld.Services.Bot
{
    internal static class BotMessaging
    {
        private const string Key = "MsgHand";
        private static Stopwatch watch;

        internal static void Configure()
        {
            watch = new Stopwatch();
        }

        #region CommandService Logs

        internal static Task CommandService_Log(LogMessage arg)
        {
            var key = $"{Key}-{arg.Source}";
            switch (arg.Severity)
            {
                case LogSeverity.Error:
                    Log.Error(key, arg.Message, arg.Exception);
                    break;

                case LogSeverity.Debug:
                    Log.Debug(key, arg.Message, arg.Exception);
                    break;

                case LogSeverity.Critical:
                    Log.Critical(key, arg.Message, arg.Exception);
                    break;

                case LogSeverity.Info:
                    Log.Info(key, arg.Message);
                    break;

                case LogSeverity.Verbose:
                    Log.Verbose(key, arg.Message, arg.Exception);
                    break;

                case LogSeverity.Warning:
                    Log.Warning(key, arg.Message, arg.Exception);
                    break;
            }
            return Task.CompletedTask;
        }

        internal static async Task CommandService_CommandExecuted(Optional<CommandInfo> arg1, ICommandContext arg2, IResult arg3)
        {
            CommandInfo cmd = null;

            if (arg1.IsSpecified)
            {
                cmd = arg1.Value;
            }

            var name = cmd.Module.GetModulePath();

            if (cmd.Name != null)
            {
                name += "." + cmd.Name;
            }

            name = name.ToLowerInvariant();

            if (arg3.IsSuccess)
            {
                using var Database = new SkuldDbContextFactory().CreateDbContext();

                if (arg1.IsSpecified)
                {
                    var cont = arg2 as ShardedCommandContext;

                    DogStatsd.Increment("commands.total.threads", 1, 1, new[] { $"module:{cmd.Module.Name.ToLowerInvariant()}", $"cmd:{name}" });

                    DogStatsd.Histogram("commands.latency", watch.ElapsedMilliseconds(), 0.5, new[] { $"module:{cmd.Module.Name.ToLowerInvariant()}", $"cmd:{name}" });

                    var usr = await Database.InsertOrGetUserAsync(cont.User).ConfigureAwait(false);

                    await InsertCommandAsync(cmd, usr).ConfigureAwait(false);

                    DogStatsd.Increment("commands.processed", 1, 1, new[] { $"module:{cmd.Module.Name.ToLowerInvariant()}", $"cmd:{name}" });
                }
            }
            else
            {
                bool displayerror = true;
                if (arg3.ErrorReason.Contains("few parameters"))
                {
                    var prefix = MessageTools.GetPrefixFromCommand(arg2.Message.Content, BotService.Configuration.Prefix, BotService.Configuration.AltPrefix);

                    string cmdName = "";

                    if (arg2.Guild != null)
                    {
                        using var Database = new SkuldDbContextFactory().CreateDbContext();

                        prefix = MessageTools.GetPrefixFromCommand(arg2.Message.Content, 
                            BotService.Configuration.Prefix, 
                            BotService.Configuration.AltPrefix, 
                            (await Database.InsertOrGetGuildAsync(arg2.Guild).ConfigureAwait(false)).Prefix
                        );
                    }

                    if(cmd.Module.Group != null)
                    {
                        string pfx = "";

                        ModuleInfo mod = cmd.Module;
                        while(mod.Group != null)
                        {
                            pfx += $"{mod.Group} ";

                            if (mod.IsSubmodule)
                            {
                                mod = cmd.Module.Parent;
                            }
                        }

                        cmdName = $"{pfx}{cmd.Name}";
                    }

                    var cmdembed = await BotService.CommandService.GetCommandHelpAsync(arg2, cmdName, prefix).ConfigureAwait(false);
                    await arg2.Channel.SendMessageAsync("You seem to be missing a parameter or 2, here's the help", embed: cmdembed.Build()).ConfigureAwait(false);
                    displayerror = false;
                }

                if (arg3.ErrorReason.Contains("Timeout"))
                {
                    var hourglass = new Emoji("⏳");
                    if (!arg2.Message.Reactions.Any(x => x.Key == hourglass && x.Value.IsMe))
                    {
                        await arg2.Message.AddReactionAsync(hourglass).ConfigureAwait(false);
                    }
                    displayerror = false;
                }

                if (arg3.Error != CommandError.UnknownCommand && displayerror)
                {
                    Log.Error(Key, "Error with command, Error is: " + arg3);

                    await EmbedExtensions.FromError(arg3.ErrorReason, arg2)
                        .QueueMessageAsync(arg2)
                        .ConfigureAwait(false);
                }

                switch (arg3.Error)
                {
                    case CommandError.UnmetPrecondition:
                        DogStatsd.Increment("commands.errors", 1, 1, new[] { "err:unm-precon" });
                        break;

                    case CommandError.Unsuccessful:
                        DogStatsd.Increment("commands.errors", 1, 1, new[] { "err:generic" });
                        break;

                    case CommandError.MultipleMatches:
                        DogStatsd.Increment("commands.errors", 1, 1, new[] { "err:multiple" });
                        break;

                    case CommandError.BadArgCount:
                        DogStatsd.Increment("commands.errors", 1, 1, new[] { "err:incorr-args" });
                        break;

                    case CommandError.ParseFailed:
                        DogStatsd.Increment("commands.errors", 1, 1, new[] { "err:parse-fail" });
                        break;

                    case CommandError.Exception:
                        DogStatsd.Increment("commands.errors", 1, 1, new[] { "err:exception" });
                        break;

                    case CommandError.UnknownCommand:
                        DogStatsd.Increment("commands.errors", 1, 1, new[] { "err:unk-cmd" });
                        break;
                }
            }
            watch = new Stopwatch();
        }

        #endregion CommandService Logs

        #region HandleProcessing

        public static async Task HandleMessageAsync(SocketMessage arg)
        {
            DogStatsd.Increment("messages.recieved");
            if (arg.Author.IsBot || arg.Author.IsWebhook || arg.Author.Discriminator.Equals("0000") || arg.Author.DiscriminatorValue == 0 || !(arg is SocketUserMessage message)) return;

            try
            {
                using var Database = new SkuldDbContextFactory().CreateDbContext();
                if (message.Channel is ITextChannel)
                {
                    if (!await CheckPermissionToSendMessageAsync(message.Channel as ITextChannel).ConfigureAwait(false)) return;

                    var gldtemp = (message.Channel as ITextChannel).Guild;
                    if (gldtemp != null)
                    {
                        if (BotService.DiscordClient.GetShardFor(gldtemp).ConnectionState != ConnectionState.Connected) return;

                        var guser = await gldtemp.GetCurrentUserAsync().ConfigureAwait(false);

                        if (!guser.GetPermissions(message.Channel as IGuildChannel).SendMessages) return;
                        if (!MessageTools.IsEnabledChannel(await (message.Channel as ITextChannel).Guild.GetUserAsync(message.Author.Id).ConfigureAwait(false), (ITextChannel)message.Channel)) return;
                    }
                }

                User suser = await Database.InsertOrGetUserAsync(arg.Author).ConfigureAwait(false);
                Guild sguild = null;

                if (suser != null && suser.Flags.IsBitSet(DiscordUtilities.Banned) && (!suser.Flags.IsBitSet(DiscordUtilities.BotCreator) || !suser.Flags.IsBitSet(DiscordUtilities.BotAdmin))) return;
                if (!suser.IsUpToDate(message.Author))
                {
                    suser.AvatarUrl = message.Author.GetAvatarUrl() ?? message.Author.GetDefaultAvatarUrl();
                    suser.Username = message.Author.Username;
                    await Database.SaveChangesAsync().ConfigureAwait(false);
                }

                {
                    var keys = Database.DonatorKeys.ToList().Where(x => x.Redeemer == suser.Id).ToList();

                    if (keys.Any())
                    {
                        var current = DateTime.Now.ToEpoch();
                        keys.ForEach(x =>
                        {
                            if (current > x.RedeemedWhen.FromEpoch().AddDays(365).ToEpoch())
                            {
                                keys.Remove(x);
                            }
                        });

                        if (keys.Count <= 0)
                        {
                            await Database.SaveChangesAsync().ConfigureAwait(false);
                            suser.Flags -= DiscordUtilities.BotDonator;
                            await Database.SaveChangesAsync().ConfigureAwait(false);
                        }
                    }
                }

                if (message.Channel is ITextChannel textChannel)
                {
                    var gld = textChannel.Guild;

                    sguild = await Database.InsertOrGetGuildAsync(gld).ConfigureAwait(false);
                    
                    if (sguild.Name == null || !sguild.Name.Equals(textChannel.Guild.Name))
                    {
                        sguild.Name = textChannel.Guild.Name;

                        await Database.SaveChangesAsync().ConfigureAwait(false);
                    }

                    if (sguild.IconUrl == null || !sguild.IconUrl.Equals(textChannel.Guild.IconUrl))
                    {
                        sguild.IconUrl = textChannel.Guild.IconUrl;

                        await Database.SaveChangesAsync().ConfigureAwait(false);
                    }
                }

                if (sguild != null)
                {
                    if (!Database.Features.Any(x => x.Id == sguild.Id))
                    {
                        Database.Features.Add(new GuildFeatures
                        {
                            Id = sguild.Id
                        });
                        await Database.SaveChangesAsync().ConfigureAwait(false);
                    }

                    GuildFeatures features = Database.Features.FirstOrDefault(x => x.Id == sguild.Id);
                    if (features.Experience)
                    {
                        _ = ExperienceService.HandleExperienceAsync(message, message.Author, ((message.Channel as ITextChannel).Guild));
                    }
                }

                if (!MessageTools.HasPrefix(message, BotService.Configuration.Prefix, BotService.Configuration.AltPrefix, sguild?.Prefix)) return;

                ShardedCommandContext context = new ShardedCommandContext(BotService.DiscordClient, message);

                if (sguild != null)
                {
                    if (!Database.Modules.Any(x => x.Id == sguild.Id))
                    {
                        Database.Modules.Add(new GuildModules
                        {
                            Id = sguild.Id
                        });
                        await Database.SaveChangesAsync().ConfigureAwait(false);
                    }

                    GuildModules modules;
                    if (!Database.Modules.Any(x=>x.Id == sguild.Id))
                    {
                        Database.Modules.Add(new GuildModules
                        {
                            Id = sguild.Id
                        });
                        await Database.SaveChangesAsync().ConfigureAwait(false);
                    }

                    modules = Database.Modules.FirstOrDefault(x => x.Id == sguild.Id);

                    if (modules.Custom)
                    {
                        var _ = CustomCommandService.HandleCustomCommandAsync(context, BotService.Configuration);
                    }
                }

                var prefix = MessageTools.GetPrefixFromCommand(context.Message.Content, BotService.Configuration.Prefix, BotService.Configuration.AltPrefix, sguild?.Prefix);

                if(prefix != null)
                {
                    await DispatchCommandAsync(context, prefix).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Log.Critical(Key, ex.Message, ex);
            }
        }

        #endregion HandleProcessing

        #region Dispatching

        public static async Task DispatchCommandAsync(ShardedCommandContext context, string prefix)
        {
            try
            {
                watch.Start();
                await BotService.CommandService.ExecuteAsync(context, prefix.Length, BotService.Services, MultiMatchHandling.Best).ConfigureAwait(false);
                watch.Stop();
            }
            catch (Exception ex)
            {
                Log.Critical("CmdService", ex.Message, ex);
            }
        }

        #endregion Dispatching

        #region HandleInsertion

        private static async Task InsertCommandAsync(CommandInfo command, User user)
        {
            var name = command.Module.GetModulePath();

            if(command.Name != null)
            {
                name += "." + command.Name;
            }

            name = name.ToLowerInvariant();

            using var Database = new SkuldDbContextFactory().CreateDbContext();

            var experience = Database.UserCommandUsage.FirstOrDefault(x => x.UserId == user.Id && x.Command.ToLower() == name.ToLower());
            if (experience != null)
            {
                experience.Usage += 1;
            }
            else
            {
                Database.UserCommandUsage.Add(new UserCommandUsage
                {
                    Command = name,
                    UserId = user.Id,
                    Usage = 1
                });
            }

            await Database.SaveChangesAsync().ConfigureAwait(false);
        }

        #endregion HandleInsertion

        public static async Task<bool> CheckPermissionToSendMessageAsync(ITextChannel channel)
        {
            if (channel.Guild != null)
            {
                var currentuser = await channel.Guild.GetCurrentUserAsync();
                var chan = await channel.Guild.GetChannelAsync(channel.Id);
                var po = chan.GetPermissionOverwrite(currentuser);

                if (po.HasValue)
                {
                    if (po.Value.SendMessages != PermValue.Deny)
                    {
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
﻿using Discord.Commands;
using Skuld.Core.Utilities;
using Skuld.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Skuld.Services.Discord.Preconditions
{
    public class RequireBotAdmin : PreconditionAttribute
    {
        public RequireBotAdmin()
        {
        }

        public override Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services)
        {
            using var Database = new SkuldDbContextFactory().CreateDbContext();

            if (Database.Users.FirstOrDefault(x => x.Id == context.User.Id).Flags >= DiscordUtilities.BotAdmin || (context.Client.GetApplicationInfoAsync().Result).Owner.Id == context.User.Id)
                return Task.FromResult(PreconditionResult.FromSuccess());
            else
                return Task.FromResult(PreconditionResult.FromError("Not a bot owner/developer"));
        }
    }
}
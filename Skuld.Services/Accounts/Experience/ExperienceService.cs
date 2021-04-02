using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Skuld.Core;
using Skuld.Core.Extensions;
using Skuld.Core.Utilities;
using Skuld.Models;
using Skuld.Services.Extensions;
using Skuld.Services.Messaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Skuld.Services.Accounts.Experience
{
	public static class ExperienceService
	{
		public static async Task HandleExperienceAsync(
			ICommandContext context
		)
		{
			if (context.User.IsBot || context.User.IsWebhook || context.Guild is null) return;

			try
			{
				using var Database = new SkuldDbContextFactory().CreateDbContext();

				var user = await Database.InsertOrGetUserAsync(context.User).ConfigureAwait(false);

				if (context.Guild is not null)
				{
					var guild = await Database.InsertOrGetGuildAsync(context.Guild).ConfigureAwait(false);

					int maxAmount = (int)Math.Round(51 * guild.XPModifier, 0);

					await user.GrantExperienceAsync((ulong)SkuldRandom.Next(1, maxAmount), context, DefaultAction).ConfigureAwait(false);
				}

				await user.GrantExperienceAsync((ulong)SkuldRandom.Next(1, 51), null, null, false, context).ConfigureAwait(false);

				await Database.SaveChangesAsync().ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				Log.Error("ExperienceService", ex.Message, context, ex);
			}
		}

		static string GetMessage(IUser user, IGuild guild, Guild dbGuild, IUserMessage message, ulong level, IEnumerable<LevelRewards> roles, bool showFromVoice = false)
		{
			if (guild is null || dbGuild is null || user is null) return null;

			try
			{
				var msg = dbGuild.LevelUpMessage;

				string rles = "";

				if (guild is not null)
				{
					if (roles is not null && roles.Any())
					{
						if (roles.Count() <= 10)
						{
							rles = string.Join(", ", roles.Select(x => guild.GetRole(x.RoleId)?.Name).ToArray());
						}
						else
						{
							rles = $"{roles.Count().ToFormattedString()} roles";
						}
					}
					else
					{
						rles = "None at this role";
					}
				}

				if (msg is not null && user is not null && guild is not null)
				{
					msg = msg.ReplaceGuildEventMessage(user, guild as SocketGuild)
						.ReplaceFirst("-l", level.ToFormattedString())
						.ReplaceFirst("-r", rles);

					if (msg.Contains("-jl"))
					{
						try
						{
							msg = msg.ReplaceFirst("-jl", message.GetJumpUrl());
						}
						catch (Exception ex)
						{
							msg = msg.ReplaceFirst("-jl", "");
							Log.Error("ExperienceService", ex.Message, null, ex);
						}
					}
				}
				else
				{
					msg = $"Congratulations {user.Mention}!! You're now level **{level}**! You currently have access to: `{rles}`";
				}

				if (showFromVoice)
				{
					msg += $"\n**FROM VOICE**";
				}

				return msg;
			}
			catch (Exception ex)
			{
				Log.Error("ExperienceService", ex.Message, null, ex);
				return null;
			}
		}

		static IEnumerable<LevelRewards> GetUnlockedRoles(ulong guildId, bool automatic = false)
		{
			using var Database = new SkuldDbContextFactory().CreateDbContext();

			return Database.LevelRewards.ToList().Where(x => x.GuildId == guildId && x.Automatic == automatic);
		}

		public static Action<IGuildUser, IGuild, Guild, ICommandContext, ulong> DefaultAction = new(
			async (user, guild, dbGuild, context, level) =>
			{
				if (guild is null || dbGuild is null || context is null || user is null) return;

				using var Database = new SkuldDbContextFactory().CreateDbContext();

				var autoRoles = GetUnlockedRoles(guild.Id, true);
				var nonautoRoles = GetUnlockedRoles(guild.Id);
				var bot = await guild.GetCurrentUserAsync().ConfigureAwait(false);
				var highestRole = bot.GetHighestRole();

				var GuildConfig = Database.Features.Find(guild.Id);

				List<LevelRewards> roles = new(autoRoles);
				roles.AddRange(nonautoRoles);

				var msg = GetMessage(user, guild, dbGuild, context.Message, level, roles);

				if (autoRoles.Any() && autoRoles.Any(x => x.LevelRequired == level))
				{
					var usr = await guild.GetUserAsync(user.Id).ConfigureAwait(false);

					if (!GuildConfig.StackingRoles)
					{
						var badRoles = autoRoles.Where(x => x.LevelRequired < level);

						if (usr.RoleIds.Any(x => badRoles.Any(z => z.RoleId == x)))
						{
							if (bot.GuildPermissions.ManageRoles)
							{
								await usr.RemoveRolesAsync(badRoles.Where(x => usr.RoleIds.Contains(x.RoleId)).Select(x => guild.GetRole(x.RoleId)));
							}
						}
					}

					var grantRoles = autoRoles.Where(x => x.LevelRequired == level).Select(x => guild.GetRole(x.RoleId));

					if (bot.GuildPermissions.ManageRoles && grantRoles.All(x => x.Position < highestRole.Position))
					{
						await usr.AddRolesAsync(grantRoles).ConfigureAwait(false);
					}
				}

				await
					HandleMessageSend(user, guild, dbGuild, context, msg)
				.ConfigureAwait(false);
			});

		static async Task HandleMessageSend(IGuildUser user, IGuild guild, Guild dbGuild, ICommandContext context, string msg)
		{
			if (guild is null || dbGuild is null || context is null || user is null) return;

			switch (dbGuild.LevelNotification)
			{
				case LevelNotification.Channel:
					{
						if (dbGuild.LevelUpChannel != 0)
						{
							var channel = await
								guild.GetTextChannelAsync(dbGuild.LevelUpChannel)
							.ConfigureAwait(false);

							if (channel is not null)
							{
								await
									MessageSender.SendMessageTo(channel, msg)
								.ConfigureAwait(false);
							}
							else
							{
								var owner = await
									guild.GetOwnerAsync()
								.ConfigureAwait(false);

								await
									MessageSender.SendMessageToUser(owner,
									$"Channel with Id **{dbGuild.LevelUpChannel}** no longer exists, please update using `{dbGuild.Prefix}guild channel join #newChannel`"
								)
								.ConfigureAwait(false);
							}
						}
						else
						{
							await
								context.Message.ReplyToAsync(msg)
							.ConfigureAwait(false);
						}
					}
					break;

				case LevelNotification.DM:
					{
						try
						{
							await
								MessageSender.SendMessageToUser(user, $"**{guild.Name}:** " + msg)
							.ConfigureAwait(false);
						}
						catch (Exception ex)
						{
							Log.Error(
								"ExperienceService",
								"Failed sending level up message to DMs, " +
								"most likely disabled DMs",
								context,
								ex
							);
						}
					}
					break;

				case LevelNotification.None:
				default:
					break;
			}
		}

		public static Action<IGuildUser, IGuild, Guild, ICommandContext, ulong> VoiceAction = new(
			async (user, guild, dbGuild, context, level) =>
			{
				if (guild is null || dbGuild is null || context is null || user is null) return;

				using var Database = new SkuldDbContextFactory().CreateDbContext();

				var autoRoles = GetUnlockedRoles(guild.Id, true);
				var nonautoRoles = GetUnlockedRoles(guild.Id);
				var bot = await guild.GetCurrentUserAsync().ConfigureAwait(false);
				var highestRole = bot.GetHighestRole();

				var GuildConfig = Database.Features.ToList().FirstOrDefault(x => x.Id == guild.Id);

				List<LevelRewards> roles = new(autoRoles);
				roles.AddRange(nonautoRoles);

				var msg = GetMessage(user, guild, dbGuild, null, level, roles, true);

				if (autoRoles.Any(x => x.LevelRequired == level))
				{
					var usr = await guild.GetUserAsync(user.Id).ConfigureAwait(false);

					if (!GuildConfig.StackingRoles)
					{
						var badRoles = autoRoles.Where(x => x.LevelRequired < level);

						if (usr.RoleIds.Any(x => badRoles.Any(z => z.RoleId == x)))
						{
							if (bot.GuildPermissions.ManageRoles)
							{
								await usr.RemoveRolesAsync(badRoles.Where(x => usr.RoleIds.Contains(x.RoleId)).Select(x => guild.GetRole(x.RoleId)));
							}
						}
					}

					var grantRoles = autoRoles.Where(x => x.LevelRequired == level).Select(x => guild.GetRole(x.RoleId));

					if (bot.GuildPermissions.ManageRoles && grantRoles.All(x => x.Position < highestRole.Position))
					{
						await usr.AddRolesAsync(grantRoles).ConfigureAwait(false);
					}
				}

				await
					HandleMessageSend(user, guild, dbGuild, context, msg)
				.ConfigureAwait(false);
			});
	}
}
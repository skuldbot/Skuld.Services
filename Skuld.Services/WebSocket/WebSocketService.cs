﻿using Discord.WebSocket;
using Fleck;
using Newtonsoft.Json;
using Skuld.Bot.Models.Services.WebSocket;
using Skuld.Core;
using Skuld.Core.Models;
using Skuld.Core.Utilities;
using Skuld.Models;
using Skuld.Services.Bot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Skuld.Services.WebSocket
{
    public class WebSocketService : IDisposable
    {
        public DiscordShardedClient Client;
        private readonly WebSocketServer _server;

        public WebSocketService(DiscordShardedClient client, SkuldConfig configuration)
        {
            Client = client;
            _server = new WebSocketServer($"{(configuration.WebsocketSecure ? "wss" : "ws")}://{configuration.WebsocketHost}:{configuration.WebsocketPort}");
            _server.Start(x =>
            {
                x.OnMessage = async (message) => await HandleMessageAsync(x, message).ConfigureAwait(false);
            });

            Log.Info("WebSocketService - Ctr", "New WebSocketServer started on: " + _server.Location);
        }

        public void ShutdownServer()
            => Dispose();

        public async Task HandleMessageAsync(IWebSocketConnection conn, string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            if (message.StartsWith("user:", StringComparison.InvariantCultureIgnoreCase))
            {
                ulong.TryParse(message.Replace("user:", ""), out var userid);

                var usr = Client.GetUser(userid);
                if (usr != null)
                {
                    var wsuser = new WebSocketUser
                    {
                        Username = usr.Username,
                        Id = usr.Id,
                        Discriminator = usr.Discriminator,
                        UserIconUrl = usr.GetAvatarUrl() ?? usr.GetDefaultAvatarUrl(),
                        Status = usr.Status.ToString()
                    };

                    var res = EventResult<WebSocketUser>.FromSuccess(wsuser);

                    var cnv = JsonConvert.SerializeObject(res);

                    await conn.Send(cnv).ConfigureAwait(false);
                }
                else
                {
                    await conn.Send(JsonConvert.SerializeObject(EventResult<WebSocketUser>.FromFailure("User not found"))).ConfigureAwait(false);
                }
            }
            if (message.StartsWith("guild:", StringComparison.InvariantCultureIgnoreCase))
            {
                ulong.TryParse(message.Replace("guild:", ""), out var guildid);

                var gld = Client.GetGuild(guildid);
                if (gld != null)
                {
                    var wsgld = new WebSocketGuild
                    {
                        Name = gld.Name,
                        GuildIconUrl = gld.IconUrl,
                        Id = gld.Id
                    };

                    var res = EventResult<WebSocketGuild>.FromSuccess(wsgld);

                    var cnv = JsonConvert.SerializeObject(res);

                    await conn.Send(cnv).ConfigureAwait(false);
                }
                else
                {
                    await conn.Send(JsonConvert.SerializeObject(EventResult<WebSocketGuild>.FromFailure("Guild not found"))).ConfigureAwait(false);
                }
            }
            if (message.StartsWith("roles:", StringComparison.InvariantCultureIgnoreCase))
            {
                ulong.TryParse(message.Replace("roles:", ""), out var guildid);

                var gld = Client.GetGuild(guildid);
                if (gld != null)
                {
                    List<WebSocketSnowFlake> snowflakes = new List<WebSocketSnowFlake>();

                    foreach (var role in gld.Roles)
                    {
                        snowflakes.Add(new WebSocketSnowFlake
                        {
                            Name = role.Name,
                            ID = role.Id
                        });
                    }

                    var wsgld = new WebSocketSnowFlakes
                    {
                        Type = "roles",
                        GuildID = guildid,
                        Data = snowflakes
                    };

                    var res = EventResult<WebSocketSnowFlakes>.FromSuccess(wsgld);

                    var cnv = JsonConvert.SerializeObject(res);

                    await conn.Send(cnv).ConfigureAwait(false);
                }
                else
                {
                    await conn.Send(JsonConvert.SerializeObject(EventResult<WebSocketSnowFlakes>.FromFailure("Guild not found"))).ConfigureAwait(false);
                }
            }
            if (message.StartsWith("tchannels:", StringComparison.InvariantCultureIgnoreCase))
            {
                ulong.TryParse(message.Replace("tchannels:", ""), out var guildid);

                var gld = Client.GetGuild(guildid);
                if (gld != null)
                {
                    List<WebSocketSnowFlake> snowflakes = new List<WebSocketSnowFlake>();

                    foreach (var role in gld.TextChannels)
                    {
                        snowflakes.Add(new WebSocketSnowFlake
                        {
                            Name = role.Name,
                            ID = role.Id
                        });
                    }

                    var wsgld = new WebSocketSnowFlakes
                    {
                        Type = "tchannels",
                        GuildID = guildid,
                        Data = snowflakes
                    };

                    var res = EventResult<WebSocketSnowFlakes>.FromSuccess(wsgld);

                    var cnv = JsonConvert.SerializeObject(res);

                    await conn.Send(cnv).ConfigureAwait(false);
                }
                else
                {
                    await conn.Send(JsonConvert.SerializeObject(EventResult<WebSocketSnowFlakes>.FromFailure("Guild not found"))).ConfigureAwait(false);
                }
            }
            if (message.StartsWith("cchannels:", StringComparison.InvariantCultureIgnoreCase))
            {
                ulong.TryParse(message.Replace("cchannels:", ""), out var guildid);

                var gld = Client.GetGuild(guildid);
                if (gld != null)
                {
                    List<WebSocketSnowFlake> snowflakes = new List<WebSocketSnowFlake>();

                    foreach (var role in gld.CategoryChannels)
                    {
                        snowflakes.Add(new WebSocketSnowFlake
                        {
                            Name = role.Name,
                            ID = role.Id
                        });
                    }

                    var wsgld = new WebSocketSnowFlakes
                    {
                        Type = "cchannels",
                        GuildID = guildid,
                        Data = snowflakes
                    };

                    var res = EventResult<WebSocketSnowFlakes>.FromSuccess(wsgld);

                    var cnv = JsonConvert.SerializeObject(res);

                    await conn.Send(cnv).ConfigureAwait(false);
                }
                else
                {
                    await conn.Send(JsonConvert.SerializeObject(EventResult<WebSocketSnowFlakes>.FromFailure("Guild not found"))).ConfigureAwait(false);
                }
            }
            if (message.StartsWith("vchannels:", StringComparison.InvariantCultureIgnoreCase))
            {
                ulong.TryParse(message.Replace("vchannels:", "", StringComparison.InvariantCultureIgnoreCase), System.Globalization.NumberStyles.Integer, null, out var guildid);

                var gld = Client.GetGuild(guildid);
                if (gld != null)
                {
                    List<WebSocketSnowFlake> snowflakes = new List<WebSocketSnowFlake>();

                    foreach (var role in gld.VoiceChannels)
                    {
                        snowflakes.Add(new WebSocketSnowFlake
                        {
                            Name = role.Name,
                            ID = role.Id
                        });
                    }

                    var wsgld = new WebSocketSnowFlakes
                    {
                        Type = "vchannels",
                        GuildID = guildid,
                        Data = snowflakes
                    };

                    var res = EventResult<WebSocketSnowFlakes>.FromSuccess(wsgld);

                    var cnv = JsonConvert.SerializeObject(res);

                    await conn.Send(cnv).ConfigureAwait(false);
                }
                else
                {
                    await conn.Send(JsonConvert.SerializeObject(EventResult<WebSocketSnowFlakes>.FromFailure("Guild not found"))).ConfigureAwait(false);
                }
            }
            if (message.ToLowerInvariant() == "stats" || message.ToLowerInvariant() == "status")
            {
                string mem;
                if (SkuldAppContext.Memory.GetMBUsage > 1024)
                    mem = SkuldAppContext.Memory.GetGBUsage + "GB";
                else
                    mem = SkuldAppContext.Memory.GetMBUsage + "MB";

                var rawjson = $"{{\"Skuld\":\"{SkuldAppContext.Skuld.Key.Version.ToString()}\"," +
                    $"\"Uptime\":\"{$"{DateTime.Now.Subtract(Process.GetCurrentProcess().StartTime):dd}d {DateTime.Now.Subtract(Process.GetCurrentProcess().StartTime):hh}:{DateTime.Now.Subtract(Process.GetCurrentProcess().StartTime):mm}"}\"," +
                    $"\"Ping\":{Client.Latency}," +
                    $"\"Guilds\":{Client.Guilds.Count}," +
                    //$"\"Users\":{BotService.Users}," +
                    $"\"Shards\":{Client.Shards.Count}," +
                    $"\"Commands\":{BotService.CommandService.Commands.Count()}," +
                    $"\"MemoryUsed\":\"{mem}\"}}";

                await conn.Send(JsonConvert.SerializeObject(rawjson)).ConfigureAwait(false);
            }
        }

        public void Dispose()
        {
            _server.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
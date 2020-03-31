using Akitaux.Twitch.Helix;
using Booru.Net;
using Discord;
using Discord.Addons.Interactive;
using Discord.Commands;
using Discord.WebSocket;
using IqdbApi;
using Microsoft.Extensions.DependencyInjection;
using Miki.API.Images;
using NodaTime;
using Octokit;
using Skuld.APIS;
using Skuld.Core;
using Skuld.Core.Models;
using Skuld.Core.Utilities;
using Skuld.Discord.TypeReaders;
using Skuld.Models;
using Skuld.Services.Bot.Discord;
using Skuld.Services.Discord.Models;
using Skuld.Services.Globalization;
using Skuld.Services.Reminders;
using Skuld.Services.VoiceExperience;
using Skuld.Services.WebSocket;
using StatsdClient;
using SteamWebAPI2.Interfaces;
using SysEx.Net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Voltaic;
using Emoji = Discord.Emoji;

namespace Skuld.Services.Bot
{
    public static class BotService
    {
        public static DiscordShardedClient DiscordClient;
        public static CommandService CommandService;
        public static CommandServiceConfig CommandServiceConfig;
        public static MessageServiceConfig MessageServiceConfig;
        public static IServiceProvider Services;

        internal static SkuldConfig Configuration;

        public static WebSocketService WebSocket;
        private static VoiceExpService voiceService;

        public static async Task ConfigureBotAsync(SkuldConfig inConfig, DiscordSocketConfig config, CommandServiceConfig cmdConfig, MessageServiceConfig msgConfig)
        {
            Configuration = inConfig;

            CommandServiceConfig = cmdConfig;

            MessageServiceConfig = msgConfig;

            DiscordClient = new DiscordShardedClient(config);

            InstallServices(Configuration);

            InitializeServices(Configuration);

            BotMessaging.Configure();

            await ConfigureCommandServiceAsync().ConfigureAwait(false);
        }

        public static async Task StartBotAsync()
        {
            BackgroundTasks();

            BotEvents.RegisterEvents();

            await DiscordClient.LoginAsync(TokenType.Bot, Configuration.DiscordToken).ConfigureAwait(false);

            await DiscordClient.StartAsync().ConfigureAwait(false);
        }

        public static async Task StopBotAsync(string source)
        {
            BotEvents.UnRegisterEvents();

            await DiscordClient.SetStatusAsync(UserStatus.Offline).ConfigureAwait(false);
            await DiscordClient.StopAsync().ConfigureAwait(false);
            await DiscordClient.LogoutAsync().ConfigureAwait(false);

            Log.Info(source, "Skuld is shutting down");
            DogStatsd.Event("FrameWork", $"Bot Stopped", alertType: "info", hostname: "Skuld");

            Log.FlushNewLine();

            await Console.Out.WriteLineAsync("Bot shutdown").ConfigureAwait(false);
            Console.ReadLine();
            Environment.Exit(0);
        }

        #region Services

        internal static async Task<EventResult<IEnumerable<ModuleInfo>>> ConfigureCommandServiceAsync()
        {
            try
            {
                CommandService = new CommandService(CommandServiceConfig);

                CommandService.CommandExecuted += BotMessaging.CommandService_CommandExecuted;
                CommandService.Log += BotMessaging.CommandService_Log;

                CommandService.AddTypeReader<Uri>(new UriTypeReader());
                CommandService.AddTypeReader<Guid>(new GuidTypeReader());
                CommandService.AddTypeReader<Emoji>(new EmojiTypeReader());
                CommandService.AddTypeReader<Emote>(new EmoteTypeReader());
                CommandService.AddTypeReader<IPAddress>(new IPAddressTypeReader());
                CommandService.AddTypeReader<RoleConfig>(new RoleConfigTypeReader());
                CommandService.AddTypeReader<DateTimeZone>(new DateTimeZoneTypeReader());
                CommandService.AddTypeReader<GuildRoleConfig>(new GuildRoleConfigTypeReader());

                IEnumerable<ModuleInfo> modules = await CommandService.AddModulesAsync(Assembly.GetEntryAssembly(), Services).ConfigureAwait(false);

                return EventResult<IEnumerable<ModuleInfo>>.FromSuccess(modules);
            }
            catch (Exception ex)
            {
                return EventResult<IEnumerable<ModuleInfo>>.FromFailureException(ex.Message, ex);
            }
        }

        private static void InstallServices(SkuldConfig Configuration)
        {
            try
            {
                var locale = new Locale();
                locale.InitialiseLocales();

                var localServices = new ServiceCollection()
                    .AddSingleton(Configuration)
                    .AddSingleton(locale)
                    .AddSingleton<ISSClient>()
                    .AddSingleton<SocialAPIS>()
                    .AddSingleton<SteamStore>()
                    .AddSingleton<IqdbClient>()
                #region Booru
                    .AddSingleton<E621Client>()
                    .AddSingleton<Rule34Client>()
                    .AddSingleton<YandereClient>()
                    .AddSingleton<KonaChanClient>()
                    .AddSingleton<DanbooruClient>()
                    .AddSingleton<GelbooruClient>()
                    .AddSingleton<RealbooruClient>()
                    .AddSingleton<SafebooruClient>()
                #endregion
                    .AddSingleton<GiphyClient>()
                    .AddSingleton<YNWTFClient>()
                    .AddSingleton<SysExClient>()
                    .AddSingleton<AnimalClient>()
                    .AddSingleton<ImghoardClient>()
                    .AddSingleton<NekosLifeClient>()
                    .AddSingleton<WikipediaClient>()
                    .AddSingleton<WebComicClients>()
                    .AddSingleton<UrbanDictionaryClient>();

                // Github
                {
                    if (!string.IsNullOrEmpty(Configuration.GithubClientUsername) && !string.IsNullOrEmpty(Configuration.GithubClientPassword) && Configuration.GithubRepository != 0)
                    {
                        var github = new GitHubClient(new ProductHeaderValue("Skuld", SkuldAppContext.Skuld.Key.Version.ToString()));
                        github.Connection.Credentials = new Credentials(Configuration.GithubClientUsername, Configuration.GithubClientPassword);

                        localServices.AddSingleton(github);
                    }
                }

                // NASA
                {
                    if (!string.IsNullOrEmpty(Configuration.NASAApiKey))
                    {
                        localServices.AddSingleton(new NASAClient(Configuration.NASAApiKey));
                    }
                }

                //Stands4Client
                {
                    if (Configuration.STANDSUid != 0 && !string.IsNullOrEmpty(Configuration.STANDSToken))
                    {
                        localServices.AddSingleton(new Stands4Client(Configuration.STANDSUid, Configuration.STANDSToken));
                    }
                }
                //Twitch
                {
                    if (!string.IsNullOrEmpty(Configuration.TwitchClientID))
                    {
                        localServices.AddSingleton(new TwitchHelixClient
                        {
                            ClientId = new Utf8String(Configuration.TwitchClientID)
                        });
                    }
                }

                localServices.AddSingleton(new InteractiveService(DiscordClient, TimeSpan.FromSeconds(60)));

                Services = localServices.BuildServiceProvider();

                Log.Info("Framework", "Successfully built service provider");
            }
            catch (Exception ex)
            {
                Log.Critical("Framework", ex.Message, ex);
            }
        }

        private static void InitializeServices(SkuldConfig Configuration)
        {
            voiceService = new VoiceExpService(DiscordClient, Configuration);

            WebSocket = new WebSocketService(DiscordClient, Configuration);

            APIS.SearchClient.Configure(Configuration.GoogleAPI, Configuration.GoogleCx, Configuration.ImgurClientID, Configuration.ImgurClientSecret);

            ConfigureStatsCollector();
        }

        #endregion Services

        #region Statistics

        private static void ConfigureStatsCollector()
        {
            DogStatsd.Configure(new StatsdConfig
            {
                StatsdServerName = Configuration.DataDogHost,
                StatsdPort = Configuration.DataDogPort ?? 8125,
                Prefix = "skuld"
            });
        }

        private static Task SendDataToDataDog()
        {
            while (true)
            {
                DogStatsd.Gauge("shards.count", DiscordClient.Shards.Count);
                DogStatsd.Gauge("shards.connected", DiscordClient.Shards.Count(x => x.ConnectionState == ConnectionState.Connected));
                DogStatsd.Gauge("shards.disconnected", DiscordClient.Shards.Count(x => x.ConnectionState == ConnectionState.Disconnected));
                DogStatsd.Gauge("commands.count", CommandService.Commands.Count());
                if (DiscordClient.Shards.All(x => x.ConnectionState == ConnectionState.Connected))
                {
                    DogStatsd.Gauge("guilds.total", DiscordClient.Guilds.Count);
                }
                Thread.Sleep(TimeSpan.FromSeconds(5));
            }
        }

        public static void BackgroundTasks()
        {
            new Thread(
                async () =>
                {
                    Thread.CurrentThread.IsBackground = true;
                    await SendDataToDataDog().ConfigureAwait(false);
                }
            ).Start();
            new Thread(
                () =>
                {
                    Thread.CurrentThread.IsBackground = true;
                    ReminderService.Run();
                }
            ).Start();
        }

        #endregion Statistics
    }
}
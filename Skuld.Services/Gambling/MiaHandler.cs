using Discord;
using Skuld.Bot.Models.GamblingModule;
using Skuld.Core.Extensions;
using Skuld.Services.Exceptions;
using Skuld.Services.Gambling.Models;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Skuld.Services.Gambling
{
    public static class MiaHandler
    {
        static readonly List<MiaSession> Sessions = new List<MiaSession>();
        public const ushort MaxReRolls = 5;
        private static readonly List<ushort[]> miaValues
                = new List<ushort[]>
            {
                new ushort[] { 2, 1 },
                new ushort[] { 6, 6 },
                new ushort[] { 5, 5 },
                new ushort[] { 4, 4 },
                new ushort[] { 3, 3 },
                new ushort[] { 2, 2 },
                new ushort[] { 1, 1 },
                new ushort[] { 6, 5 },
                new ushort[] { 6, 4 },
                new ushort[] { 6, 3 },
                new ushort[] { 6, 2 },
                new ushort[] { 6, 1 },
                new ushort[] { 5, 4 },
                new ushort[] { 5, 3 },
                new ushort[] { 5, 2 },
                new ushort[] { 5, 1 },
                new ushort[] { 4, 3 },
                new ushort[] { 4, 2 },
                new ushort[] { 4, 1 },
                new ushort[] { 3, 2 },
                new ushort[] { 3, 1 }
            };

        public static MiaSession NewSession(IUser user, 
            ulong betAmount, 
            Dice playerDice, 
            Dice botDice,
            IUserMessage previousMessage)
        {
            if(IsSessionInProgress(user))
            {
                throw new DuplicateSessionException(
                    "Cannot have duplicate sessions"
                );
            }
            else
            {
                var session = new MiaSession
                {
                    Target = user.Id,
                    Amount = betAmount,
                    BotDice = botDice,
                    PlayerDice = playerDice,
                    PreviousMessage = previousMessage,
                    ReRolls = 0
                };

                Sessions.Add(session);

                return session;
            }
        }

        public static MiaSession GetSession(IUser user)
        {
            if(IsSessionInProgress(user))
            {
                return Sessions.FirstOrDefault(x => x.Target == user.Id);
            }
            else
            {
                throw new SessionNotFoundException(
                    $"Could not find a session for User: {user.Id}"
                );
            }
        }

        public static void EndSession(IUser user)
        {
            if (IsSessionInProgress(user))
            {
                Sessions.Remove(
                    Sessions.FirstOrDefault(x => x.Target == user.Id)
                );
            }
            else
            {
                throw new SessionNotFoundException(
                    $"Could not find a session for User: {user.Id}"
                );
            }
        }

        public static void UpdateSession(MiaSession session)
        {
            if (Sessions.Any(x => x.Target == session.Target))
            {
                Sessions.Remove(
                    Sessions.FirstOrDefault(x => x.Target == session.Target)
                );

                Sessions.Add(session);
            }
            else
            {
                throw new SessionNotFoundException(
                    "Session Not Found"
                );
            }
        }

        public static bool IsSessionInProgress(IUser user)
            => Sessions.Any(x => x.Target == user.Id);

        public static ulong GetAmountFromReRolls(ulong Amount, ulong ReRolls)
            => (ulong)(Amount * (ReRolls * 0.25));

        public static string GetRollString(
                Dice dies,
                bool isHidden
            )
        {
            StringBuilder str = new StringBuilder(isHidden ? "||" : "");

            Die[] dieArr = dies
                .GetDies()
                .OrderByDescending(x => x.Face)
                .ToArray();

            str.Append(
                string.Join(
                    ", ", 
                    dieArr.Select(x => isHidden ? x.Face.As("?") : x.Face.ToString()
                ).ToArray()
                )
            );

            str.Append(isHidden ? "||" : "");

            return str.ToString();
        }

        public static WinResult DidPlayerWin(Die[] bot, Die[] player)
        {
            var botDies = bot
                .OrderByDescending(x => x.Face)
                .ToArray();
            var playerDies = player
                .OrderByDescending(x => x.Face)
                .ToArray();

            var botIndex = miaValues
                .FindIndex(x =>
                    x[0] == botDies[0] && x[1] == botDies[1]
                );
            var playerIndex = miaValues
                .FindIndex(x =>
                    x[0] == playerDies[0] && x[1] == playerDies[1]
                );

            if (playerIndex < botIndex) return WinResult.PlayerWin;
            else if (botIndex < playerIndex) return WinResult.BotWin;
            else return WinResult.Draw;
        }
    }
}

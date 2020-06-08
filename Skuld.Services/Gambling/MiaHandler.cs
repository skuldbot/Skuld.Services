using Discord;
using Skuld.Services.Exceptions;
using Skuld.Services.Gambling.Models;
using System.Collections.Generic;
using System.Linq;

namespace Skuld.Services.Gambling
{
    public static class MiaHandler
    {
        static List<MiaSession> Sessions = new List<MiaSession>();
        public const ushort MaxReRolls = 5;

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
                    PreviousMessage = previousMessage
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
    }
}

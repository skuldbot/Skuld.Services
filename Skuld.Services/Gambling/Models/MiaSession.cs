using Discord;

namespace Skuld.Services.Gambling.Models
{
    public class MiaSession
    {
        public ulong Target;
        public ulong Amount;
        public Dice PlayerDice;
        public Dice BotDice;
        public IUserMessage PreviousMessage;
    }
}

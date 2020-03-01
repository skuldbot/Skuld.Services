namespace Skuld.Bot.Models.GamblingModule
{
    public enum RPSThrow
    {
        Rock = 0,
        Paper = 1,
        Scissors = 2,
        Invalid = 3
    }

    public static class RockPaperScissorsHelper
    {
        public static RPSThrow FromString(string input)
            => input.ToLowerInvariant() switch
            {
                "rock" => RPSThrow.Rock,
                "r" => RPSThrow.Rock,
                "paper" => RPSThrow.Paper,
                "p" => RPSThrow.Paper,
                "scissors" => RPSThrow.Scissors,
                "s" => RPSThrow.Scissors,
                _ => RPSThrow.Invalid
            };
    }
}
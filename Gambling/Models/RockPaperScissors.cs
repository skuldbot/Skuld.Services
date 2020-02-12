﻿namespace Skuld.Bot.Models.GamblingModule
{
    public enum RockPaperScissors
    {
        Rock = 0,
        Paper = 1,
        Scissors = 2,
        Invalid = 3
    }

    public static class RockPaperScissorsHelper
    {
        public static RockPaperScissors FromString(string input)
            => input.ToLowerInvariant() switch
            {
                "rock" => RockPaperScissors.Rock,
                "r" => RockPaperScissors.Rock,
                "paper" => RockPaperScissors.Paper,
                "p" => RockPaperScissors.Paper,
                "scissors" => RockPaperScissors.Scissors,
                "s" => RockPaperScissors.Scissors,
                _ => RockPaperScissors.Invalid
            };
    }
}
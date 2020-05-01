using Skuld.Core.Utilities;
using Skuld.Models;
using Skuld.Services.Extensions;
using Xunit;

namespace Skuld.Services.Tests
{
    public class DailyTests
    {
        [Fact]
        public void TestStandard()
        {
            var config = new SkuldConfig
            {
                DailyAmount = 300
            };

            var standard = new User
            {
                Money = 0
            };
            var standardStreak = new User
            {
                Money = 0,
                Streak = 5
            };
            var standardMaxStreak = new User
            {
                Money = 0,
                Streak = 105
            };

            var standardAmount = standard.GetDailyAmount(config);
            var standardStreakAmount = standardStreak.GetDailyAmount(config);
            var standardMaxStreakAmount = standardMaxStreak.GetDailyAmount(config);

            Assert.Equal(300ul, standardAmount);
            Assert.Equal(1500ul, standardStreakAmount);
            Assert.Equal(15000ul, standardMaxStreakAmount);
        }

        [Fact]
        public void TestDonator()
        {
            var config = new SkuldConfig
            {
                DailyAmount = 300
            };

            var donator = new User
            {
                Flags = DiscordUtilities.BotDonator
            };
            var donatorStreak = new User
            {
                Flags = DiscordUtilities.BotDonator,
                Streak = 5
            };
            var donatorMaxStreak = new User
            {
                Flags = DiscordUtilities.BotDonator,
                Streak = 105
            };

            var donatorAmount = donator.GetDailyAmount(config);
            var donatorStreakAmount = donatorStreak.GetDailyAmount(config);
            var donatorMaxStreakAmount = donatorMaxStreak.GetDailyAmount(config);

            Assert.Equal(600ul, donatorAmount);
            Assert.Equal(3000ul, donatorStreakAmount);
            Assert.Equal(30000ul, donatorMaxStreakAmount);
        }
    }
}

using Moq;
using Skuld.Core.Extensions;
using Skuld.Services.Gambling;
using Xunit;

namespace Skuld.Services.Tests
{
    public class MiaTests
    {
        [Fact]
        public static void ReRollTest()
        {
            Assert.Equal(250ul, MiaHandler.GetAmountFromReRolls(1000, 1));
            Assert.Equal(500ul, MiaHandler.GetAmountFromReRolls(1000, 2));
            Assert.Equal(750ul, MiaHandler.GetAmountFromReRolls(1000, 3));
        }

        [Fact]
        public static void WritingDiceTest()
        {
            var dies = new Dice(2)
                .SetDieValue(0, 5)
                .SetDieValue(1, 5);

            Assert.Equal("||?, ?||", MiaHandler.GetRollString(dies, true));
            Assert.Equal("5, 5", MiaHandler.GetRollString(dies, false));
        }
    }
}

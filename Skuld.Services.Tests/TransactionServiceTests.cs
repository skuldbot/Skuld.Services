using Moq;
using Skuld.Core.Models;
using Skuld.Services.Accounts.Banking.Models;
using Skuld.Services.Banking;
using Xunit;

namespace Skuld.Services.Tests
{
    public class TransactionServiceTests
    {
        [Fact]
        public void TestAdd()
        {
            var sender = new User
            {
                Money = 100
            };
            var receiver = new User
            {
                Money = 0
            };

            var result = TransactionService.DoTransaction(new TransactionStruct
            {
                Receiver = receiver,
                Amount = 100
            });

            Assert.True(result.Successful);
            Assert.Equal(100UL, receiver.Money);

            result = TransactionService.DoTransaction(new TransactionStruct
            {
                Receiver = receiver,
                Sender = sender,
                Amount = 100
            });

            Assert.True(result.Successful);
            Assert.Equal(200UL, receiver.Money);
            Assert.Equal(0UL, sender.Money);
        }

        [Fact]
        public void TestSubtract()
        {
            var sender = new User
            {
                Money = 200
            };

            var result = TransactionService.DoTransaction(new TransactionStruct
            {
                Sender = sender,
                Amount = 100
            });

            Assert.True(result.Successful);
            Assert.Equal(100UL, sender.Money);

            result = TransactionService.DoTransaction(new TransactionStruct
            {
                Sender = sender,
                Amount = 100
            });

            Assert.True(result.Successful);
            Assert.Equal(0UL, sender.Money);
        }
    }
}

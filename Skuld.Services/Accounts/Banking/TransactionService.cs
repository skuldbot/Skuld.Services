using Skuld.Core.Extensions;
using Skuld.Core.Models;
using Skuld.Services.Accounts.Banking.Exceptions;
using Skuld.Services.Accounts.Banking.Models;
using StatsdClient;

namespace Skuld.Services.Banking
{
    public static class TransactionService
    {
        public static EventResult<bool> CanPerformTransaction(ulong money, ulong amount)
        {
            if(money < amount)
            {
                return EventResult<bool>.FromFailureException(
                        "Sender doesn't have enough money",
                        new TransactionException("Sender doesn't have enough money")
                    );
            }

            return EventResult<bool>.FromSuccess(true);
        }

        public static EventResult<bool> DoTransaction(TransactionStruct transaction)
        {
            if (transaction.Sender != null)
            {
                var result = CanPerformTransaction(transaction.Sender.Money, transaction.Amount);
                if (!result.Successful)
                {
                    return result;
                }

                transaction.Sender.Money = transaction.Sender.Money.Subtract(transaction.Amount);
                DogStatsd.Increment("economy.processed.taken", (int)transaction.Amount);
            }

            if(transaction.Receiver != null)
            {
                transaction.Receiver.Money = transaction.Receiver.Money.Add(transaction.Amount);
                DogStatsd.Increment("economy.processed.given", (int)transaction.Amount);
            }

            DogStatsd.Increment("economy.processed", (int)transaction.Amount);

            return EventResult<bool>.FromSuccess(true);
        }
    }
}
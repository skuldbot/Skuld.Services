using Skuld.Core.Extensions;
using Skuld.Core.Models;
using Skuld.Services.Accounts.Banking.Exceptions;
using Skuld.Services.Accounts.Banking.Models;
using StatsdClient;

namespace Skuld.Services.Banking
{
    public static class TransactionService
    {
        public static EventResult DoTransaction(TransactionStruct transaction)
        {
            if (transaction.Sender != null)
            {
                if (transaction.Sender.Money < transaction.Amount)
                {
                    return EventResult.FromFailureException("sender doesn't have enough money", new TransactionException("sender doesn't have enough money"));
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

            return EventResult.FromSuccess();
        }
    }
}
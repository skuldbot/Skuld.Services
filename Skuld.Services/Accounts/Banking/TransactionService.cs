using Skuld.Core.Extensions;
using Skuld.Core.Models;
using Skuld.Services.Accounts.Banking.Exceptions;
using Skuld.Services.Accounts.Banking.Models;
using StatsdClient;

namespace Skuld.Services.Banking
{
	public static class TransactionService
	{
		public static EventResult CanPerformTransaction(ulong money, ulong amount)
		{
			if (money < amount)
			{
				return EventResult.FromFailureException(
						"Sender doesn't have enough money",
						new TransactionException("Sender doesn't have enough money")
					);
			}

			return EventResult.FromSuccess();
		}

		public static EventResult DoTransaction(TransactionStruct transaction)
		{
			if (transaction.Sender is not null)
			{
				var result = CanPerformTransaction(transaction.Sender.Money, transaction.Amount);
				if (!result.Successful)
				{
					return result;
				}

				transaction.Sender.Money = transaction.Sender.Money.Subtract(transaction.Amount);
				DogStatsd.Increment("economy.processed.taken", (int)transaction.Amount);
			}

			if (transaction.Receiver is not null)
			{
				transaction.Receiver.Money = transaction.Receiver.Money.Add(transaction.Amount);
				DogStatsd.Increment("economy.processed.given", (int)transaction.Amount);
			}

			DogStatsd.Increment("economy.processed", (int)transaction.Amount);

			return EventResult<bool>.FromSuccess(true);
		}
	}
}
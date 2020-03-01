using Skuld.Core.Models;

namespace Skuld.Services.Accounts.Banking.Models
{
    public struct TransactionStruct
    {
        public ulong Amount;
        public User Sender;
        public User Receiver;
    }
}
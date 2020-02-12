using System;
using System.Collections.Generic;
using System.Text;

namespace Skuld.Services.Accounts.Banking.Models
{
    public struct TransactionStruct
    {
        public bool Removal;
        public ulong Amount;
        public ulong SenderId;
        public ulong ReceiverId;
    }
}

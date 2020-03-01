using System;

namespace Skuld.Services.Reminder.Models
{
    public class ReminderObject
    {
        public ushort Id;
        public ulong UserId;
        public ulong ChannelId;
        public ulong Timeout;
        public string Content;
        public string MessageLink;
        public DateTime Created;
    }
}
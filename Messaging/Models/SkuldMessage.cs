using Discord;
using System;

namespace Skuld.Services.Messaging.Models
{
    public struct SkuldMessage
    {
        public Guid IdempotencyKey;
        public IMessageChannel Channel;
        public SkuldMessageMeta Meta;
        public SkuldMessageContent Content;
    }
}
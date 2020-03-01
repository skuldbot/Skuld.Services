using System;

namespace Skuld.Services.Messaging.Models
{
    public struct SkuldMessageMeta
    {
        public Exception Exception;
        public double Timeout;
        public MessageType Type;
    }
}
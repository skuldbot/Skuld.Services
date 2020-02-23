using Discord;
using System.IO;

namespace Skuld.Services.Messaging.Models
{
    public struct SkuldMessageContent
    {
        public string Message;
        public IUser User;
        public Embed Embed;
        public Stream File;
        public string FileName;
    }
}
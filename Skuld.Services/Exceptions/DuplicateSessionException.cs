using System;

namespace Skuld.Services.Exceptions
{
    public class DuplicateSessionException : Exception
    {
        public DuplicateSessionException() : base()
        {
        }

        public DuplicateSessionException(string message) : base(message)
        {
        }

        public DuplicateSessionException(
            string message,
            Exception innerException
        ) : base(message, innerException)
        {
        }
    }
}

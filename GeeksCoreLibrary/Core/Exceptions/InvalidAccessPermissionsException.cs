using System;
using GeeksCoreLibrary.Core.Enums;
using GeeksCoreLibrary.Core.Models;

namespace GeeksCoreLibrary.Core.Exceptions
{
    public class InvalidAccessPermissionsException : Exception
    {
        public EntityActions Action { get; set; }
        public ulong ItemId { get; set; }
        public ulong UserId { get; set; }
        public InvistaError Error { get; set; }
        
        public InvalidAccessPermissionsException()
        {
        }

        public InvalidAccessPermissionsException(string message) : base(message)
        {
        }

        public InvalidAccessPermissionsException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}

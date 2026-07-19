using System;

namespace Task_Flyout.Services
{
    public sealed class AuthorizationInteractionRequiredException : InvalidOperationException
    {
        public string ProviderName { get; }

        public AuthorizationInteractionRequiredException(string providerName, string message, Exception? innerException = null)
            : base(message, innerException)
        {
            ProviderName = providerName;
        }
    }
}

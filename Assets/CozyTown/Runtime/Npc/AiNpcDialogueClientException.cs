using System;

namespace CozyTown.Runtime.Npc
{
    public enum AiNpcDialogueClientFailure
    {
        Transport = 0,
        Provider = 1,
        ContentRejected = 2,
        InvalidResponseStructure = 3
    }

    public sealed class AiNpcDialogueClientException : Exception
    {
        public AiNpcDialogueClientException(
            AiNpcDialogueClientFailure failure,
            string message,
            Exception innerException = null)
            : base(message, innerException)
        {
            Failure = failure;
        }

        public AiNpcDialogueClientFailure Failure { get; }
    }
}

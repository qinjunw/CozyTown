namespace CozyTown.Runtime.Npc
{
    public enum NpcDialogueFallbackReason
    {
        None = 0,
        Timeout = 1,
        TransportFailure = 2,
        ProviderFailure = 3,
        ClientFailure = 4,
        EmptyResponse = 5,
        InvalidText = 6,
        InvalidEmotionTag = 7,
        InvalidActionTag = 8,
        ContentRejected = 9,
        InvalidResponseStructure = 10
    }
}

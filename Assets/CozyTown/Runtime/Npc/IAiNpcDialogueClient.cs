using System.Threading;
using System.Threading.Tasks;

namespace CozyTown.Runtime.Npc
{
    public interface IAiNpcDialogueClient
    {
        Task<AiNpcDialogueCandidate> GenerateAsync(
            NpcDialogueRequest request,
            CancellationToken cancellationToken);
    }
}

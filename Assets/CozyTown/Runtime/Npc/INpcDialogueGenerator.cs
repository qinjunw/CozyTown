using System.Threading;
using System.Threading.Tasks;

namespace CozyTown.Runtime.Npc
{
    public interface INpcDialogueGenerator
    {
        Task<NpcDialogueReply> GenerateAsync(
            NpcDialogueContext context,
            CancellationToken cancellationToken);
    }
}

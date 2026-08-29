using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace CozyTown.Runtime.Application
{
    public interface INpcDialogueCoordinator
    {
        IReadOnlyList<NpcDialogueOption> Npcs { get; }

        Task<NpcDialogueViewState> GenerateAsync(
            string npcId,
            CancellationToken cancellationToken);
    }
}

using System.Threading;
using System.Threading.Tasks;

namespace CozyTown.Runtime.NpcAgents
{
    public interface INpcDecisionClient
    {
        Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken cancellationToken);
    }
}

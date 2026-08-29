using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CozyTown.Runtime.Npc
{
    public sealed class AiNpcDialogueGenerator : INpcDialogueGenerator
    {
        private const int MaximumTextLength = 500;

        private static readonly HashSet<string> AllowedEmotionTags =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "neutral",
                "happy",
                "concerned",
                "excited",
                "thoughtful"
            };

        private static readonly HashSet<string> AllowedActionTags =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "idle",
                "nod",
                "wave",
                "smile"
            };

        private readonly IAiNpcDialogueClient _client;
        private readonly INpcDialogueGenerator _fallback;
        private readonly TimeSpan _timeout;

        public AiNpcDialogueGenerator(
            IAiNpcDialogueClient client,
            INpcDialogueGenerator fallback,
            TimeSpan timeout)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
            if (timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timeout),
                    "Dialogue timeout must be greater than zero.");
            }

            _timeout = timeout;
        }

        public async Task<NpcDialogueReply> GenerateAsync(
            NpcDialogueContext context,
            CancellationToken cancellationToken)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            cancellationToken.ThrowIfCancellationRequested();
            string correlationId = Guid.NewGuid().ToString("N");
            using var clientCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            Task<AiNpcDialogueCandidate> clientTask;
            try
            {
                clientTask = _client.GenerateAsync(
                    new NpcDialogueRequest(context),
                    clientCancellation.Token);
                if (clientTask == null)
                {
                    return await CreateFallbackAsync(
                        context,
                        correlationId,
                        NpcDialogueFallbackReason.ClientFailure,
                        cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (AiNpcDialogueClientException exception)
            {
                return await CreateFallbackAsync(
                    context,
                    correlationId,
                    MapFailure(exception.Failure),
                    cancellationToken);
            }
            catch (Exception)
            {
                return await CreateFallbackAsync(
                    context,
                    correlationId,
                    NpcDialogueFallbackReason.ClientFailure,
                    cancellationToken);
            }

            using var timeoutCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task timeoutTask = Task.Delay(_timeout, timeoutCancellation.Token);
            Task completedTask = await Task.WhenAny(clientTask, timeoutTask);
            cancellationToken.ThrowIfCancellationRequested();
            if (completedTask != clientTask)
            {
                ObserveFault(clientTask);
                clientCancellation.Cancel();
                return await CreateFallbackAsync(
                    context,
                    correlationId,
                    NpcDialogueFallbackReason.Timeout,
                    cancellationToken);
            }

            timeoutCancellation.Cancel();

            AiNpcDialogueCandidate candidate;
            try
            {
                candidate = await clientTask;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (AiNpcDialogueClientException exception)
            {
                return await CreateFallbackAsync(
                    context,
                    correlationId,
                    MapFailure(exception.Failure),
                    cancellationToken);
            }
            catch (Exception)
            {
                return await CreateFallbackAsync(
                    context,
                    correlationId,
                    NpcDialogueFallbackReason.ClientFailure,
                    cancellationToken);
            }

            NpcDialogueFallbackReason invalidReason = Validate(candidate);
            if (invalidReason != NpcDialogueFallbackReason.None)
            {
                return await CreateFallbackAsync(
                    context,
                    correlationId,
                    invalidReason,
                    cancellationToken);
            }

            string actionTag = string.IsNullOrWhiteSpace(candidate.ActionTag)
                ? "idle"
                : candidate.ActionTag.Trim().ToLowerInvariant();
            return new NpcDialogueReply(
                candidate.Text.Trim(),
                candidate.EmotionTag.Trim().ToLowerInvariant(),
                actionTag,
                false,
                correlationId,
                NpcDialogueFallbackReason.None);
        }

        private static NpcDialogueFallbackReason Validate(AiNpcDialogueCandidate candidate)
        {
            if (candidate == null)
            {
                return NpcDialogueFallbackReason.EmptyResponse;
            }

            if (string.IsNullOrWhiteSpace(candidate.Text)
                || candidate.Text.Trim().Length > MaximumTextLength)
            {
                return NpcDialogueFallbackReason.InvalidText;
            }

            if (string.IsNullOrWhiteSpace(candidate.EmotionTag)
                || !AllowedEmotionTags.Contains(candidate.EmotionTag.Trim()))
            {
                return NpcDialogueFallbackReason.InvalidEmotionTag;
            }

            if (!string.IsNullOrWhiteSpace(candidate.ActionTag)
                && !AllowedActionTags.Contains(candidate.ActionTag.Trim()))
            {
                return NpcDialogueFallbackReason.InvalidActionTag;
            }

            return NpcDialogueFallbackReason.None;
        }

        private static NpcDialogueFallbackReason MapFailure(
            AiNpcDialogueClientFailure failure)
        {
            switch (failure)
            {
                case AiNpcDialogueClientFailure.Transport:
                    return NpcDialogueFallbackReason.TransportFailure;
                case AiNpcDialogueClientFailure.Provider:
                    return NpcDialogueFallbackReason.ProviderFailure;
                case AiNpcDialogueClientFailure.ContentRejected:
                    return NpcDialogueFallbackReason.ContentRejected;
                case AiNpcDialogueClientFailure.InvalidResponseStructure:
                    return NpcDialogueFallbackReason.InvalidResponseStructure;
                default:
                    return NpcDialogueFallbackReason.ClientFailure;
            }
        }

        private static void ObserveFault(Task task)
        {
            _ = task.ContinueWith(
                completed =>
                {
                    _ = completed.Exception;
                },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted
                    | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private async Task<NpcDialogueReply> CreateFallbackAsync(
            NpcDialogueContext context,
            string correlationId,
            NpcDialogueFallbackReason reason,
            CancellationToken cancellationToken)
        {
            NpcDialogueReply fallbackReply = await _fallback.GenerateAsync(
                context,
                cancellationToken);
            return new NpcDialogueReply(
                fallbackReply.Text,
                fallbackReply.EmotionTag,
                fallbackReply.ActionTag,
                true,
                correlationId,
                reason);
        }
    }
}

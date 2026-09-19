using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CozyTown.Runtime.NpcAgents;
using CozyTown.Unity.Npc;

namespace CozyTown.Unity.Experiments
{
    public sealed class DecisionTraceClient : INpcDecisionClient, INpcDecisionConfiguration
    {
        private readonly object _gate = new object();
        private readonly List<Pending> _pending = new List<Pending>();
        private readonly Queue<Pending> _completed = new Queue<Pending>();
        private readonly DecisionTraceIdentifiers _identifiers = new DecisionTraceIdentifiers();
        private readonly DecisionTraceIdentifiers _recordedIdentifiers = new DecisionTraceIdentifiers();
        private readonly ProxyNpcDecisionJsonCodec _codec = new ProxyNpcDecisionJsonCodec();
        private DecisionTrace _trace;
        private INpcDecisionClient _inner;
        private Func<double> _completionClock;
        private bool _replay, _sealed;
        private int _tick = -1, _nextCall, _completionOrdinal;
        private double _realSeconds;

        private sealed class Pending
        {
            internal DecisionTraceCall Call;
            internal NpcDecisionRequest Request;
            internal Task<NpcDecisionReply> Provider;
            internal NpcDecisionReply ProviderReply;
            internal CancellationTokenRegistration Cancellation;
            internal bool CancellationObserved;
            internal readonly TaskCompletionSource<NpcDecisionReply> Result = new TaskCompletionSource<NpcDecisionReply>();
        }

        public string SnapshotConfiguration { get; private set; }
        public DecisionTrace Trace { get { lock (_gate) return CopyTrace(_trace); } }
        public string Divergence { get; private set; }
        public bool IsStopped => Divergence != null;

        public static DecisionTraceClient Record(INpcDecisionClient inner, string configuration, Func<double> completionClock = null)
        {
            if (inner == null) throw new ArgumentNullException(nameof(inner));
            if (string.IsNullOrWhiteSpace(configuration)) throw new ArgumentException("An experiment configuration is required.", nameof(configuration));
            string client = (inner as INpcDecisionConfiguration)?.SnapshotConfiguration;
            if (string.IsNullOrWhiteSpace(client) || client.Length > 16384)
                throw new ArgumentException("Trace recording requires a declared non-secret decision client configuration of at most 16384 characters.", nameof(inner));
            return new DecisionTraceClient { _inner = inner, _completionClock = completionClock,
                SnapshotConfiguration = client, _trace = new DecisionTrace { configuration = configuration, clientConfiguration = client } };
        }

        public static DecisionTraceClient Replay(DecisionTrace trace, string configuration)
        {
            if (trace == null) throw new ArgumentNullException(nameof(trace));
            ValidateTrace(trace, configuration);
            var owned = CopyTrace(trace);
            ValidateTrace(owned, configuration);
            foreach (var call in owned.calls) { call.replayRequestJson = null; call.replayResponseJson = null; }
            return new DecisionTraceClient { _replay = true, _trace = owned, SnapshotConfiguration = owned.clientConfiguration };
        }

        private static DecisionTrace CopyTrace(DecisionTrace source) => new DecisionTrace {
            schemaVersion = source.schemaVersion, configuration = source.configuration,
            clientConfiguration = source.clientConfiguration, completed = source.completed,
            ticks = source.ticks.Select(tick => new DecisionTraceTick {
                tick = tick.tick, realSeconds = tick.realSeconds, gameMinutes = tick.gameMinutes
            }).ToList(),
            calls = source.calls.Select(call => new DecisionTraceCall {
                callOrdinal = call.callOrdinal, dispatchTick = call.dispatchTick, deliveryTick = call.deliveryTick,
                completionOrdinal = call.completionOrdinal, cancellationTick = call.cancellationTick,
                dispatchRealSeconds = call.dispatchRealSeconds, deliveryRealSeconds = call.deliveryRealSeconds,
                completedRealSeconds = call.completedRealSeconds, hasCompletedRealSeconds = call.hasCompletedRealSeconds,
                requestJson = call.requestJson, normalizedRequest = call.normalizedRequest, responseJson = call.responseJson,
                rawResponseJson = call.rawResponseJson, completionKind = call.completionKind, candidateErrorCode = call.candidateErrorCode,
                requestedModel = call.requestedModel, returnedModel = call.returnedModel, usageJson = call.usageJson,
                replayRequestJson = call.replayRequestJson, replayResponseJson = call.replayResponseJson
            }).ToList()
        };

        private static void ValidateTrace(DecisionTrace trace, string configuration)
        {
            try
            {
                if (trace.schemaVersion != 1 || !trace.completed || string.IsNullOrWhiteSpace(configuration)
                    || trace.configuration != configuration || string.IsNullOrWhiteSpace(trace.clientConfiguration)
                    || trace.ticks == null || trace.calls == null)
                    throw new FormatException("Trace configuration, completion state or timeline is missing.");
                for (int i = 0; i < trace.ticks.Count; i++)
                {
                    var tick = trace.ticks[i];
                    if (tick == null || tick.tick != i || !Finite(tick.realSeconds) || !Finite(tick.gameMinutes)
                        || (i > 0 && tick.realSeconds < trace.ticks[i - 1].realSeconds))
                        throw new FormatException("Trace ticks require consecutive indices and monotonic time.");
                }
                var ids = new DecisionTraceIdentifiers();
                var ordinals = new HashSet<int>();
                int previousDispatch = -1;
                foreach (var call in trace.calls)
                {
                    if (call == null || call.callOrdinal != ordinals.Count || call.dispatchTick < previousDispatch
                        || call.dispatchTick < 0 || call.dispatchTick >= trace.ticks.Count
                        || call.deliveryTick <= call.dispatchTick || call.deliveryTick >= trace.ticks.Count
                        || call.dispatchRealSeconds != trace.ticks[call.dispatchTick].realSeconds
                        || call.deliveryRealSeconds != trace.ticks[call.deliveryTick].realSeconds
                        || call.completionOrdinal < 0 || call.completionOrdinal >= trace.calls.Count
                        || !ordinals.Add(call.completionOrdinal) || call.cancellationTick < -1
                        || (call.cancellationTick != -1 && (call.cancellationTick < call.dispatchTick || call.cancellationTick > call.deliveryTick))
                        || (call.hasCompletedRealSeconds && !Finite(call.completedRealSeconds))
                        || string.IsNullOrWhiteSpace(call.requestJson)
                        || Encoding.UTF8.GetByteCount(call.requestJson) > ProxyNpcDecisionJsonCodec.MaximumRequestBytes
                        || call.normalizedRequest != ids.Normalize(call.requestJson))
                        throw new FormatException("Trace request, completion order or delivery boundary is invalid.");
                    previousDispatch = call.dispatchTick;
                    if (call.completionKind == "reply")
                    {
                        if (string.IsNullOrWhiteSpace(call.responseJson)
                            || Encoding.UTF8.GetByteCount(call.responseJson) > ProxyNpcDecisionJsonCodec.MaximumResponseBytes
                            || DecisionTraceIdentifiers.ReadJson(call.responseJson).DocumentElement.GetAttribute("type") != "object")
                            throw new FormatException("Recorded reply is not a bounded JSON object.");
                    }
                    else if (call.completionKind == "candidate_failure")
                    {
                        if (!NpcCandidateException.IsKnownCode(call.candidateErrorCode))
                            throw new FormatException("Recorded candidate failure code is unknown.");
                    }
                    else if (call.completionKind != "null_reply" && call.completionKind != "response_invalid"
                        && call.completionKind != "client_failure" && call.completionKind != "canceled")
                        throw new FormatException("Recorded completion kind is unknown.");
                }
                int previousDelivery = -1;
                foreach (var call in trace.calls.OrderBy(item => item.completionOrdinal))
                {
                    if (call.deliveryTick < previousDelivery)
                        throw new FormatException("Recorded delivery order contradicts completion order.");
                    previousDelivery = call.deliveryTick;
                }
            }
            catch (Exception exception) when (exception is FormatException || exception is System.Xml.XmlException
                || exception is ArgumentException || exception is InvalidOperationException)
            {
                throw new ArgumentException("Replay requires a complete, internally consistent trace with matching configuration.", nameof(trace), exception);
            }
        }

        public void Pump(int tick, double realSeconds, double gameMinutes)
        {
            Pending[] ready;
            lock (_gate)
            {
                RequireRunning();
                if (tick != _tick + 1 || !Finite(realSeconds) || realSeconds < _realSeconds || !Finite(gameMinutes))
                    throw Stop("Trace tick or time is invalid.");
                if (_replay)
                {
                    if (tick >= _trace.ticks.Count) throw Stop("Replay has an extra tick.");
                    var expected = _trace.ticks[tick];
                    if (expected.tick != tick || expected.realSeconds != realSeconds || expected.gameMinutes != gameMinutes)
                        throw Stop("Replay time differs from the recorded tick.");
                    if (_trace.calls.Skip(_nextCall).Any(call => call.dispatchTick < tick))
                        throw Stop("Replay is missing a recorded request.");
                    if (_pending.Any(item => item.Call.cancellationTick >= 0 && item.Call.cancellationTick < tick
                        && !item.CancellationObserved)) throw Stop("Replay is missing a recorded cancellation.");
                }
                else _trace.ticks.Add(new DecisionTraceTick { tick = tick, realSeconds = realSeconds, gameMinutes = gameMinutes });
                _tick = tick;
                _realSeconds = realSeconds;
                if (_replay) ready = _pending.Where(item => item.Call.deliveryTick == tick).OrderBy(item => item.Call.completionOrdinal).ToArray();
                else { ready = _completed.ToArray(); _completed.Clear(); }
                foreach (var pending in ready)
                {
                    if (_replay)
                    {
                        if (pending.Call.completionKind == "reply")
                            pending.Call.replayResponseJson = _recordedIdentifiers.RemapResponse(pending.Call.responseJson, _identifiers);
                    }
                    else
                    {
                        pending.Call.deliveryTick = tick;
                        pending.Call.deliveryRealSeconds = realSeconds;
                        CaptureCompletion(pending);
                    }
                }
            }
            foreach (var pending in ready)
            {
                Deliver(pending);
                pending.Cancellation.Dispose();
                lock (_gate) _pending.Remove(pending);
            }
        }

        public void Complete()
        {
            lock (_gate)
            {
                if (_sealed) return;
                RequireRunning();
                if (_pending.Count != 0) throw Stop("Trace cannot complete with pending requests.");
                if (_replay && (_nextCall != _trace.calls.Count || _tick + 1 != _trace.ticks.Count))
                    throw Stop("Replay ended before all recorded requests and ticks were consumed.");
                if (!_replay) _trace.completed = true;
                _sealed = true;
            }
        }

        public Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken cancellationToken)
        {
            var pending = new Pending { Request = request };
            lock (_gate)
            {
                if (IsStopped || _sealed) return Task.FromException<NpcDecisionReply>(
                    new InvalidOperationException(Divergence ?? "A completed trace cannot accept requests."));
                if (_tick < 0) return Task.FromException<NpcDecisionReply>(Stop("Pump must precede the first request."));
                string json = _codec.SerializeRequest(request);
                string normalized = _identifiers.Normalize(json);
                if (_replay)
                {
                    if (_nextCall >= _trace.calls.Count) return Task.FromException<NpcDecisionReply>(Stop("Replay has an extra request."));
                    pending.Call = _trace.calls[_nextCall];
                    string recorded = _recordedIdentifiers.Normalize(pending.Call.requestJson);
                    if (pending.Call.dispatchTick != _tick || pending.Call.normalizedRequest != recorded || recorded != normalized)
                        return Task.FromException<NpcDecisionReply>(Stop("Replay request differs from the recorded context."));
                    pending.Call.replayRequestJson = json;
                    _nextCall++;
                }
                else
                {
                    pending.Call = new DecisionTraceCall { callOrdinal = _trace.calls.Count, dispatchTick = _tick,
                        requestJson = json, normalizedRequest = normalized, dispatchRealSeconds = _realSeconds };
                    _trace.calls.Add(pending.Call);
                }
                _pending.Add(pending);
            }
            if (!_replay)
            {
                try { pending.Provider = _inner.DecideAsync(request, cancellationToken); }
                catch (Exception exception) { pending.Provider = Task.FromException<NpcDecisionReply>(exception); }
                pending.Provider ??= Task.FromException<NpcDecisionReply>(new InvalidOperationException("Decision client returned no task."));
                pending.Provider.ContinueWith(completed =>
                {
                    lock (_gate)
                    {
                        pending.Call.completionOrdinal = _completionOrdinal++;
                        pending.Call.hasCompletedRealSeconds = _completionClock != null;
                        pending.Call.completedRealSeconds = _completionClock?.Invoke() ?? 0;
                        _completed.Enqueue(pending);
                    }
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
            pending.Cancellation = cancellationToken.Register(() =>
            {
                lock (_gate)
                {
                    pending.CancellationObserved = true;
                    if (_replay)
                    {
                        if (pending.Call.cancellationTick != _tick)
                            Stop("Replay cancellation differs from the recorded tick.");
                    }
                    else pending.Call.cancellationTick = _tick;
                }
            });
            return pending.Result.Task;
        }

        private static void CaptureCompletion(Pending pending)
        {
            try
            {
                var reply = pending.Provider.GetAwaiter().GetResult();
                pending.ProviderReply = reply;
                pending.Call.completionKind = reply == null ? "null_reply" : "reply";
                if (reply != null) pending.Call.responseJson = DecisionTraceResponse.Serialize(reply, pending.Request);
            }
            catch (NpcCandidateException exception)
            {
                pending.Call.completionKind = "candidate_failure";
                pending.Call.candidateErrorCode = exception.Code;
            }
            catch (OperationCanceledException) { pending.Call.completionKind = "canceled"; }
            catch (FormatException) { pending.Call.completionKind = "response_invalid"; }
            catch (Exception) { pending.Call.completionKind = "client_failure"; }
        }

        private void Deliver(Pending pending)
        {
            switch (pending.Call.completionKind)
            {
                case "reply":
                    try
                    {
                        pending.Result.SetResult(_replay
                            ? _codec.ParseResponse(pending.Call.replayResponseJson)
                            : pending.ProviderReply);
                    }
                    catch (FormatException exception) { pending.Result.SetException(exception); }
                    break;
                case "null_reply": pending.Result.SetResult(null); break;
                case "candidate_failure": pending.Result.SetException(new NpcCandidateException(pending.Call.candidateErrorCode)); break;
                case "response_invalid": pending.Result.SetException(new FormatException("Recorded decision response was invalid.")); break;
                case "client_failure": pending.Result.SetException(new InvalidOperationException("Recorded decision client failed.")); break;
                case "canceled": pending.Result.SetCanceled(); break;
                default: throw Stop("Recorded request has an unknown completion kind.");
            }
        }

        private void RequireRunning()
        {
            if (IsStopped) throw new InvalidOperationException(Divergence);
            if (_sealed) throw new InvalidOperationException("A completed trace cannot accept ticks.");
        }
        private InvalidOperationException Stop(string reason)
        {
            Divergence ??= reason;
            return new InvalidOperationException(Divergence);
        }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0;
    }
}

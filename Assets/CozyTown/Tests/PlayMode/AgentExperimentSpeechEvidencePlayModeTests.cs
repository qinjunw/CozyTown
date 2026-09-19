#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.NpcAgents;
using CozyTown.Unity.Core;
using CozyTown.Unity.Experiments;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CozyTown.Tests.PlayMode
{
    public sealed class AgentExperimentSpeechEvidencePlayModeTests
    {
        private Scene _scene;
        private string _previousProxyEndpoint;

        [SetUp]
        public void SetUp()
        {
            _previousProxyEndpoint = Environment.GetEnvironmentVariable(CozyTownBootstrap.AgentProxyEndpointEnvironmentVariable);
            Environment.SetEnvironmentVariable(CozyTownBootstrap.AgentProxyEndpointEnvironmentVariable, null);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_scene.IsValid() && _scene.isLoaded) yield return SceneManager.UnloadSceneAsync(_scene);
            Environment.SetEnvironmentVariable(CozyTownBootstrap.AgentProxyEndpointEnvironmentVariable, _previousProxyEndpoint);
        }

        [UnityTest]
        public IEnumerator FreeTextEvidence_UsesCommittedTextAndKeepsMissingRowsAfterLoad()
            => CheckCommittedEvidence(NpcSpeechMode.FreeText);

        [UnityTest]
        public IEnumerator StructuredEvidence_UsesCommittedTextAndKeepsMissingRowsAfterLoad()
            => CheckCommittedEvidence(NpcSpeechMode.StructuredFacts);

        private IEnumerator CheckCommittedEvidence(NpcSpeechMode mode)
        {
            const string scenePath = "Assets/CozyTown/Scenes/CozyTown_Dev.unity";
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(scenePath, new LoadSceneParameters(LoadSceneMode.Additive));
            _scene = SceneManager.GetSceneByPath(scenePath);
            var session = AgentExperimentSession.Attach(_scene,
                CozyTownCompositionRoot.Create(AgentExperimentContent.CreateConfiguration()), "available");
            session.Start(mode, "fixed", new SpeechClient());
            session.Save("before-speaking");
            for (int tick = 0; tick < 150 && session.Outcomes.All(outcome => outcome.Code != "meeting.say"); tick++)
                session.Step(0.5, 5 * (tick + 1));
            int accepted = session.Outcomes.Select((outcome, index) => new { outcome, index })
                .First(item => item.outcome.Code == "meeting.say").index;
            var result = session.Outcomes[accepted];
            var committed = session.Controller.GetMeeting(result.NpcId).Transcript[result.Context.Social.Transcript.Count];

            var evidence = AgentExperimentSpeechEvidenceCollector.Capture(session, accepted);

            Assert.That(evidence.Count, Is.EqualTo(session.Outcomes.Skip(accepted).Count(outcome => outcome.Code == "meeting.say")));
            var row = evidence.Single(item => item.outcomeIndex == accepted);
            Assert.That(row.worldRunId, Is.EqualTo(result.WorldRunId.ToString("N")));
            Assert.That(row.decisionId, Is.EqualTo(result.DecisionId.ToString("N")));
            Assert.That(row.meetingId, Is.EqualTo(result.Reply.MeetingId.ToString("N")));
            Assert.That(row.npcId, Is.EqualTo(committed.SpeakerId));
            Assert.That(row.turnIndex, Is.EqualTo(result.Context.Social.Transcript.Count));
            Assert.That(row.gameMinutes, Is.EqualTo(committed.TotalMinutes));
            Assert.That(row.text, Is.EqualTo(committed.Text).And.Not.Empty);
            Assert.That(row.source, Is.EqualTo("committed_transcript"));
            Assert.That(row.missingReason, Is.Null);
            Assert.That(row.runMode, Is.EqualTo("fixed"));
            if (mode == NpcSpeechMode.FreeText)
            {
                Assert.That(result.Reply.Text, Does.StartWith("  "));
                Assert.That(row.text, Is.EqualTo("A committed fixture line."));
            }
            else Assert.That(result.Reply.Text, Is.Null);
            row.text = "caller mutation";
            Assert.That(AgentExperimentSpeechEvidenceCollector.Capture(session, accepted)
                .Single(item => item.outcomeIndex == accepted).text, Is.EqualTo(committed.Text));
            Assert.That(AgentExperimentSpeechEvidenceCollector.Capture(session, session.Outcomes.Count), Is.Empty);

            session.Load("before-speaking");
            var missing = AgentExperimentSpeechEvidenceCollector.Capture(session, accepted);

            Assert.That(missing.Count, Is.EqualTo(evidence.Count), "A load must not remove accepted outputs from the evidence denominator.");
            Assert.That(missing.All(item => item.text == null && item.source == null && item.missingReason == "speech.world_changed"), Is.True);
            Assert.Throws<ArgumentOutOfRangeException>(() => AgentExperimentSpeechEvidenceCollector.Capture(session, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => AgentExperimentSpeechEvidenceCollector.Capture(session, session.Outcomes.Count + 1));
        }

        private sealed class SpeechClient : INpcDecisionClient, INpcDecisionConfiguration
        {
            private readonly FixedExperimentDecisionClient _inner = new FixedExperimentDecisionClient();
            public string SnapshotConfiguration => "speech-evidence-fixed/v1";
            public async Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken cancellationToken)
            {
                var reply = await _inner.DecideAsync(request, cancellationToken);
                return reply.Kind == NpcDecisionKind.Speak && request.SpeechMode == NpcSpeechMode.FreeText
                    ? new NpcDecisionReply(NpcDecisionKind.Speak, meetingId: reply.MeetingId, text: "  A committed fixture line.  ")
                    : reply;
            }
        }
    }
}
#endif

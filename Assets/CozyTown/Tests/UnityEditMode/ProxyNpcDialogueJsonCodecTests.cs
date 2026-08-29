using System;
using CozyTown.Runtime.Npc;
using CozyTown.Unity.Npc;
using NUnit.Framework;
using UnityEngine;

namespace CozyTown.Tests.UnityEditMode
{
    public sealed class ProxyNpcDialogueJsonCodecTests
    {
        [TestCase("")]
        [TestCase("relative/path")]
        [TestCase("ftp://example.invalid/dialogue")]
        public void ProxyClient_InvalidEndpoint_RejectsWithoutSendingRequest(string endpoint)
        {
            Assert.Throws<ArgumentException>(() => new ProxyNpcDialogueClient(endpoint));
        }

        [Test]
        public void SerializeRequest_CopiesOnlyReadContextFields()
        {
            var context = new NpcDialogueContext(
                "npc.farmer_eli",
                "Eli",
                "A patient farmer.",
                3,
                480,
                2,
                new[] { "watered carrots" },
                new[] { "first meeting" });
            var codec = new ProxyNpcDialogueJsonCodec();

            string json = codec.SerializeRequest(new NpcDialogueRequest(context));
            var payload = JsonUtility.FromJson<RequestPayload>(json);

            Assert.That(payload.npcId, Is.EqualTo("npc.farmer_eli"));
            Assert.That(payload.displayName, Is.EqualTo("Eli"));
            Assert.That(payload.persona, Is.EqualTo("A patient farmer."));
            Assert.That(payload.day, Is.EqualTo(3));
            Assert.That(payload.minuteOfDay, Is.EqualTo(480));
            Assert.That(payload.affinity, Is.EqualTo(2));
            Assert.That(payload.recentActivities, Is.EqualTo(new[] { "watered carrots" }));
            Assert.That(payload.memories, Is.EqualTo(new[] { "first meeting" }));
            Assert.That(json, Does.Not.Contain("coins"));
            Assert.That(json, Does.Not.Contain("inventory"));
        }

        [Test]
        public void ParseResponse_MapsOnlyAllowedCandidateFieldsAndIgnoresUnknownFields()
        {
            var codec = new ProxyNpcDialogueJsonCodec();

            AiNpcDialogueCandidate candidate = codec.ParseResponse(
                "{\"text\":\"Hello\",\"emotion\":\"happy\",\"action\":\"wave\","
                + "\"coins\":999,\"command\":\"advance_day\"}");

            Assert.That(candidate.Text, Is.EqualTo("Hello"));
            Assert.That(candidate.EmotionTag, Is.EqualTo("happy"));
            Assert.That(candidate.ActionTag, Is.EqualTo("wave"));
            Assert.That(typeof(AiNpcDialogueCandidate).GetProperties(), Has.Length.EqualTo(3));
        }

        [Test]
        public void ParseResponse_MalformedJson_MapsToInvalidStructureFailure()
        {
            var codec = new ProxyNpcDialogueJsonCodec();

            var exception = Assert.Throws<AiNpcDialogueClientException>(() =>
                codec.ParseResponse("{not-json"));

            Assert.That(
                exception.Failure,
                Is.EqualTo(AiNpcDialogueClientFailure.InvalidResponseStructure));
        }

        [Serializable]
        private sealed class RequestPayload
        {
            public string npcId;
            public string displayName;
            public string persona;
            public int day;
            public int minuteOfDay;
            public int affinity;
            public string[] recentActivities;
            public string[] memories;
        }
    }
}

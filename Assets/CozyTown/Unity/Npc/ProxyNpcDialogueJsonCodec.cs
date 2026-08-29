using System;
using System.Collections.Generic;
using CozyTown.Runtime.Npc;
using UnityEngine;

namespace CozyTown.Unity.Npc
{
    public sealed class ProxyNpcDialogueJsonCodec
    {
        public string SerializeRequest(NpcDialogueRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            return JsonUtility.ToJson(new RequestPayload(request));
        }

        public AiNpcDialogueCandidate ParseResponse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                var response = JsonUtility.FromJson<ResponsePayload>(json);
                if (response == null)
                {
                    throw new ArgumentException("Response payload is null.");
                }

                return new AiNpcDialogueCandidate(
                    response.text,
                    response.emotion,
                    response.action);
            }
            catch (Exception exception) when (!(exception is AiNpcDialogueClientException))
            {
                throw new AiNpcDialogueClientException(
                    AiNpcDialogueClientFailure.InvalidResponseStructure,
                    "Dialogue proxy response is not valid JSON.",
                    exception);
            }
        }

        [Serializable]
        private sealed class RequestPayload
        {
            public RequestPayload(NpcDialogueRequest request)
            {
                npcId = request.NpcId;
                displayName = request.DisplayName;
                persona = request.Persona;
                day = request.Day;
                minuteOfDay = request.MinuteOfDay;
                affinity = request.Affinity;
                recentActivities = Copy(request.RecentActivities);
                memories = Copy(request.Memories);
            }

            public string npcId;
            public string displayName;
            public string persona;
            public int day;
            public int minuteOfDay;
            public int affinity;
            public string[] recentActivities;
            public string[] memories;

            private static string[] Copy(IReadOnlyList<string> source)
            {
                var copy = new string[source.Count];
                for (var index = 0; index < source.Count; index++)
                {
                    copy[index] = source[index];
                }

                return copy;
            }
        }

        [Serializable]
        private sealed class ResponsePayload
        {
            public string text;
            public string emotion;
            public string action;
        }
    }
}

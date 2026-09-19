using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;

namespace CozyTown.Unity.Experiments
{
    internal sealed class DecisionTraceIdentifiers
    {
        private readonly Dictionary<string, string> _worlds = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _decisions = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _meetings = new Dictionary<string, string>(StringComparer.Ordinal);

        internal string Normalize(string json)
        {
            var document = ReadJson(json);
            Map(document.SelectSingleNode("/root/worldRunId"), _worlds, "world");
            Map(document.SelectSingleNode("/root/observation/worldRunId"), _worlds, "world");
            Map(document.SelectSingleNode("/root/decisionId"), _decisions, "decision");
            Map(document.SelectSingleNode("/root/social/meetingId"), _meetings, "meeting");
            foreach (XmlNode fact in document.SelectNodes("/root/observation/facts/item"))
            {
                NormalizeFact(fact.SelectSingleNode("factId"));
                NormalizeFact(fact.SelectSingleNode("entityId"));
            }
            return document.DocumentElement.OuterXml;
        }

        internal string RemapResponse(string json, DecisionTraceIdentifiers actual)
        {
            var document = ReadJson(json);
            var meeting = document.SelectSingleNode("/root/meetingId");
            if (meeting != null && !string.IsNullOrEmpty(meeting.InnerText)) meeting.InnerText = ActualMeeting(meeting.InnerText, actual);
            var fact = document.SelectSingleNode("/root/factId");
            if (fact != null && TryMeetingReference(fact.InnerText, out string prefix, out string id, out string suffix))
                fact.InnerText = prefix + ActualMeeting(id, actual) + suffix;
            return WriteJson(document);
        }

        private string ActualMeeting(string recorded, DecisionTraceIdentifiers actual)
        {
            if (!_meetings.TryGetValue(recorded, out string token))
                return recorded;
            string id = actual._meetings.FirstOrDefault(item => item.Value == token).Key;
            return id ?? throw new InvalidOperationException("Replay response has no matching meeting.");
        }

        private void NormalizeFact(XmlNode node)
        {
            if (node == null || !TryMeetingReference(node.InnerText, out string prefix, out string id, out string suffix)) return;
            node.InnerText = prefix + Token(id, _meetings, "meeting") + suffix;
        }

        private static bool TryMeetingReference(string value, out string prefix, out string id, out string suffix)
        {
            prefix = value.StartsWith("meeting:", StringComparison.Ordinal) ? "meeting:"
                : value.StartsWith("statement:", StringComparison.Ordinal) ? "statement:" : null;
            id = suffix = null;
            if (prefix == null || value.Length < prefix.Length + 32) return false;
            id = value.Substring(prefix.Length, 32);
            if (!Guid.TryParseExact(id, "N", out _)) return false;
            suffix = value.Substring(prefix.Length + 32);
            return suffix.Length == 0 || suffix[0] == ':';
        }

        internal static XmlDocument ReadJson(string json)
        {
            using var reader = JsonReaderWriterFactory.CreateJsonReader(Encoding.UTF8.GetBytes(json),
                new XmlDictionaryReaderQuotas { MaxDepth = 32, MaxStringContentLength = 32768 });
            var document = new XmlDocument();
            document.Load(reader);
            return document;
        }

        internal static string WriteJson(XmlDocument document)
        {
            using var stream = new MemoryStream();
            using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, Encoding.UTF8, ownsStream: false))
            {
                document.WriteTo(writer);
                writer.Flush();
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static void Map(XmlNode node, Dictionary<string, string> identifiers, string kind)
        {
            if (node == null || string.IsNullOrEmpty(node.InnerText)) return;
            node.InnerText = Token(node.InnerText, identifiers, kind);
        }

        private static string Token(string id, Dictionary<string, string> identifiers, string kind)
        {
            if (!identifiers.TryGetValue(id, out string mapped))
                identifiers.Add(id, mapped = kind + ":" + identifiers.Count);
            return mapped;
        }
    }
}

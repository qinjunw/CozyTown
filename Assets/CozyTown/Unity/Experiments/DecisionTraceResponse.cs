using System;
using System.Globalization;
using System.Xml;
using CozyTown.Runtime.NpcAgents;

namespace CozyTown.Unity.Experiments
{
    internal static class DecisionTraceResponse
    {
        internal static string Serialize(NpcDecisionReply reply, NpcDecisionRequest request)
        {
            if (reply == null) throw new ArgumentNullException(nameof(reply));
            var document = new XmlDocument();
            var root = document.CreateElement("root");
            root.SetAttribute("type", "object");
            document.AppendChild(root);
            Add("schemaVersion", (request.Social == null ? 1 : request.Social.Resources == null ? 2 : 4)
                .ToString(CultureInfo.InvariantCulture), "number");
            Add("operation", reply.Operation);
            if (reply.Kind == NpcDecisionKind.InspectLocation || reply.Kind == NpcDecisionKind.Visit)
                Add("locationId", reply.LocationId);
            if (reply.Kind == NpcDecisionKind.Visit)
            {
                Add("activity", reply.Activity.ToString().ToLowerInvariant());
                Add("durationGameMinutes", reply.DurationGameMinutes.ToString("R", CultureInfo.InvariantCulture), "number");
            }
            if (reply.Kind == NpcDecisionKind.Invite) Add("planId", reply.PlanId);
            if (reply.Kind != NpcDecisionKind.Wait && reply.Kind != NpcDecisionKind.InspectLocation
                && reply.Kind != NpcDecisionKind.Visit && reply.Kind != NpcDecisionKind.Invite)
                Add("meetingId", reply.MeetingId.ToString("N"));
            if (reply.Kind == NpcDecisionKind.Speak)
            {
                if (reply.SpeechFrame == null) Add("text", reply.Text);
                else
                {
                    Add("speechIntent", reply.SpeechFrame.Intent);
                    Add("factId", reply.SpeechFrame.FactId);
                    Add("tone", reply.SpeechFrame.Tone);
                }
            }
            return DecisionTraceIdentifiers.WriteJson(document);

            void Add(string name, string value, string type = "string")
            {
                var field = document.CreateElement(name);
                field.SetAttribute("type", value == null ? "null" : type);
                if (value != null) field.InnerText = value;
                root.AppendChild(field);
            }
        }
    }
}

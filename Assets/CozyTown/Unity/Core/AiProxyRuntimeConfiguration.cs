using System;
using System.Globalization;

namespace CozyTown.Unity.Core
{
    public sealed class AiProxyRuntimeConfiguration
    {
        public const string EndpointEnvironmentVariable =
            "COZYTOWN_AI_PROXY_ENDPOINT";
        public const string TimeoutEnvironmentVariable =
            "COZYTOWN_AI_PROXY_TIMEOUT_SECONDS";
        public const float MinimumTimeoutSeconds = 0.1f;

        private AiProxyRuntimeConfiguration(string endpoint, float timeoutSeconds)
        {
            Endpoint = endpoint;
            TimeoutSeconds = timeoutSeconds;
        }

        public string Endpoint { get; }

        public float TimeoutSeconds { get; }

        public static AiProxyRuntimeConfiguration FromEnvironment(
            string serializedEndpoint,
            float serializedTimeoutSeconds)
        {
            return Resolve(
                serializedEndpoint,
                serializedTimeoutSeconds,
                Environment.GetEnvironmentVariable);
        }

        public static AiProxyRuntimeConfiguration Resolve(
            string serializedEndpoint,
            float serializedTimeoutSeconds,
            Func<string, string> environmentReader)
        {
            if (environmentReader == null)
            {
                throw new ArgumentNullException(nameof(environmentReader));
            }

            string environmentEndpoint =
                environmentReader(EndpointEnvironmentVariable);
            string endpoint = string.IsNullOrWhiteSpace(environmentEndpoint)
                ? (serializedEndpoint ?? string.Empty).Trim()
                : environmentEndpoint.Trim();

            float timeoutSeconds = NormalizeSerializedTimeout(
                serializedTimeoutSeconds);
            string environmentTimeout =
                environmentReader(TimeoutEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(environmentTimeout))
            {
                if (!float.TryParse(
                        environmentTimeout.Trim(),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out timeoutSeconds)
                    || float.IsNaN(timeoutSeconds)
                    || float.IsInfinity(timeoutSeconds)
                    || timeoutSeconds < MinimumTimeoutSeconds)
                {
                    throw new InvalidOperationException(
                        $"{TimeoutEnvironmentVariable} must be a number greater than "
                        + $"or equal to {MinimumTimeoutSeconds.ToString(CultureInfo.InvariantCulture)}.");
                }
            }

            return new AiProxyRuntimeConfiguration(endpoint, timeoutSeconds);
        }

        private static float NormalizeSerializedTimeout(float timeoutSeconds)
        {
            return float.IsNaN(timeoutSeconds)
                || float.IsInfinity(timeoutSeconds)
                || timeoutSeconds < MinimumTimeoutSeconds
                    ? MinimumTimeoutSeconds
                    : timeoutSeconds;
        }
    }
}

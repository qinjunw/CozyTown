using System;
using System.Collections.Generic;
using CozyTown.Unity.Core;
using NUnit.Framework;

namespace CozyTown.Tests.UnityEditMode
{
    public sealed class AiProxyRuntimeConfigurationTests
    {
        [Test]
        public void Resolve_WithoutEnvironmentValues_UsesSerializedDefaults()
        {
            AiProxyRuntimeConfiguration configuration =
                AiProxyRuntimeConfiguration.Resolve(
                    " https://inspector.example/dialogue ",
                    8f,
                    _ => null);

            Assert.That(
                configuration.Endpoint,
                Is.EqualTo("https://inspector.example/dialogue"));
            Assert.That(configuration.TimeoutSeconds, Is.EqualTo(8f));
        }

        [Test]
        public void Resolve_WithEnvironmentValues_OverridesSerializedDefaults()
        {
            var environment = new Dictionary<string, string>
            {
                [AiProxyRuntimeConfiguration.EndpointEnvironmentVariable] =
                    " https://proxy.example/dialogue ",
                [AiProxyRuntimeConfiguration.TimeoutEnvironmentVariable] = "2.5"
            };

            AiProxyRuntimeConfiguration configuration =
                AiProxyRuntimeConfiguration.Resolve(
                    "https://inspector.example/dialogue",
                    8f,
                    name => environment.TryGetValue(name, out string value)
                        ? value
                        : null);

            Assert.That(
                configuration.Endpoint,
                Is.EqualTo("https://proxy.example/dialogue"));
            Assert.That(configuration.TimeoutSeconds, Is.EqualTo(2.5f));
        }

        [TestCase("invalid")]
        [TestCase("0")]
        [TestCase("NaN")]
        [TestCase("Infinity")]
        public void Resolve_WithInvalidEnvironmentTimeout_RejectsConfiguration(
            string timeout)
        {
            var environment = new Dictionary<string, string>
            {
                [AiProxyRuntimeConfiguration.TimeoutEnvironmentVariable] = timeout
            };

            var exception = Assert.Throws<InvalidOperationException>(() =>
                AiProxyRuntimeConfiguration.Resolve(
                    string.Empty,
                    8f,
                    name => environment.TryGetValue(name, out string value)
                        ? value
                        : null));

            Assert.That(
                exception.Message,
                Does.Contain(
                    AiProxyRuntimeConfiguration.TimeoutEnvironmentVariable));
        }
    }
}

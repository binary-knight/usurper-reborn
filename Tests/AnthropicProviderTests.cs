using System;
using Xunit;
using UsurperRemake.Systems;

namespace UsurperRemake.Tests
{
    /// <summary>
    /// v0.65.10: native Anthropic Messages provider selection + endpoint
    /// normalization. The provider itself returns null on any failure (same
    /// contract as the compat provider, covered by LLMTests); these tests pin
    /// the routing logic so a config change can't silently flip providers.
    /// </summary>
    public class AnthropicProviderTests : IDisposable
    {
        private readonly string? _savedEndpoint;
        private readonly string? _savedNative;

        public AnthropicProviderTests()
        {
            _savedEndpoint = Environment.GetEnvironmentVariable("USURPER_LLM_ENDPOINT");
            _savedNative = Environment.GetEnvironmentVariable("USURPER_LLM_NATIVE_ANTHROPIC");
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("USURPER_LLM_ENDPOINT", _savedEndpoint);
            Environment.SetEnvironmentVariable("USURPER_LLM_NATIVE_ANTHROPIC", _savedNative);
        }

        [Fact]
        public void NormalizeEndpoint_RewritesChatCompletionsToMessages()
        {
            Assert.Equal("https://api.anthropic.com/v1/messages",
                AnthropicMessagesProvider.NormalizeEndpoint("https://api.anthropic.com/v1/chat/completions"));
        }

        [Fact]
        public void NormalizeEndpoint_LeavesNativeUrlAlone()
        {
            Assert.Equal("https://api.anthropic.com/v1/messages",
                AnthropicMessagesProvider.NormalizeEndpoint("https://api.anthropic.com/v1/messages"));
        }

        [Fact]
        public void UseNativeAnthropic_AutoDetectsAnthropicHost()
        {
            Environment.SetEnvironmentVariable("USURPER_LLM_NATIVE_ANTHROPIC", null);
            Environment.SetEnvironmentVariable("USURPER_LLM_ENDPOINT", "https://api.anthropic.com/v1/chat/completions");
            Assert.True(LLMSettings.UseNativeAnthropic);
        }

        [Fact]
        public void UseNativeAnthropic_FalseForOtherHosts()
        {
            Environment.SetEnvironmentVariable("USURPER_LLM_NATIVE_ANTHROPIC", null);
            Environment.SetEnvironmentVariable("USURPER_LLM_ENDPOINT", "https://openrouter.ai/api/v1/chat/completions");
            Assert.False(LLMSettings.UseNativeAnthropic);
        }

        [Fact]
        public void UseNativeAnthropic_ExplicitOverrideWins()
        {
            // Opt OUT on an Anthropic endpoint (e.g. to compare providers)...
            Environment.SetEnvironmentVariable("USURPER_LLM_ENDPOINT", "https://api.anthropic.com/v1/chat/completions");
            Environment.SetEnvironmentVariable("USURPER_LLM_NATIVE_ANTHROPIC", "false");
            Assert.False(LLMSettings.UseNativeAnthropic);

            // ...and opt IN on a non-Anthropic host (a proxy speaking the native shape).
            Environment.SetEnvironmentVariable("USURPER_LLM_ENDPOINT", "https://my-proxy.example/v1/messages");
            Environment.SetEnvironmentVariable("USURPER_LLM_NATIVE_ANTHROPIC", "true");
            Assert.True(LLMSettings.UseNativeAnthropic);
        }
    }
}

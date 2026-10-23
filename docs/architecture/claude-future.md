# Claude account integration: future contract, not connected

Laura does not install Claude Code/Agent SDK, authenticate Anthropic accounts, send Anthropic requests, advertise a working Claude model or expose a login button. `ITextModelClient`/`TextConversationEngine` provide a text adapter extension point; `IProviderAuthenticator`, `ProviderAuthorization`, connection states and billing provenance keep official account eligibility separate from inference and tools. Tests simulate subscription admission and reject unknown billing, API-key overrides and an account connection without eligibility.

The desired future experience is **Sign in with Claude**, using an eligible Pro/Max subscription through an officially permitted channel. The [official Agent SDK article](https://support.claude.com/en/articles/15036540-use-the-claude-agent-sdk-with-your-claude-plan), read on 2026-10-20, says subscription-limit use for Agent SDK, `claude -p` and third-party applications remains available. This is conditional product policy, not proof that Laura has a supported integration. Recheck it on implementation day.

[Official Pro/Max guidance](https://support.claude.com/en/articles/11145838-use-claude-code-with-your-pro-or-max-plan) distinguishes subscription allocation from separately billed API use. [API environment-variable guidance](https://support.claude.com/en/articles/12304248-manage-api-key-environment-variables-in-claude-code) warns that an API key can override subscription authentication. A future subscription-only adapter must inspect relevant environment/settings without printing or deleting secret values, block API-key overrides and unknown billing, never select paid credits automatically, and preserve the owner's account choices in other applications.

## Required future gate

1. Verify current official third-party eligibility, sign-in flow, supported Windows runtime/version, attribution and subscription billing evidence. No client impersonation, borrowed OAuth client IDs, intercepted sessions, copied tokens/cookies or private consumer endpoints.
2. Use the official CLI/SDK sign-in when permitted. Keep its authentication owned by the official client or an approved broker. Account connection alone is not billing authorization.
3. Check structured output, cancellation, streaming, tool-call IDs and whether the official agent runtime can disable its own arbitrary executor. CLI and direct Messages API adapters have different contracts; never send a subscription token to the paid API.
4. Route Brain/Windows actions through Laura's existing task-linked native executor, potentially via an officially supported MCP gateway. Preserve exact provider disclosure, TaskId, InteractionId, checkpoints and permissions. No private alternate Brain writer or privileged CLI tools.
5. Preserve Laura's system persona and approved Humanizer prose guidance. All voice remains Whisper/Kokoro; no provider audio.
6. Prove a real subscription-only sign-in, read/create/retrieve one synthetic Brain note, one confirmed native tool result, cancellation and Kokoro playback. Verify the official billing source before every eligible operation. Unknown billing or incompatible execution boundaries keep the provider unavailable.
7. Handle account expiration, usage limits and transient failures distinctly; wait or select another already authorized provider. No invented quota, unlimited-use claim, API-credit fallback or automatic purchase.

No registration is active now. The contract does not promise future availability or a particular included usage allowance.

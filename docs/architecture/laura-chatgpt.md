# ChatGPT plan sharing and bounded task cycles

Laura keeps Gemini Live as its default conversation and voice provider. The optional ChatGPT adapter uses the official Sign in with ChatGPT preview for eligible accounts and applications. Availability, models and included usage depend on the actual authorization. No OpenAI API key, browser credential extraction, private endpoint or paid API fallback is configured.

## Connecting

Open Settings → ChatGPT, continue with ChatGPT in the default browser, return to Laura, refresh models and test a selected model. A catalog entry alone does not prove inference availability. A successful test requires a completed streaming response.

Before testing, disable additional credits for Laura in ChatGPT Usage and confirm the corresponding checkbox. The OAuth/inference contract does not supply a flag that enforces this external allowance. Laura requires the owner's confirmation and retains it only for the protected registration that passed the probe. It does not purchase credits or enable additional billing.

Save the selected provider and explicit options. History, retrieved Brain references and native tools require the context-sharing option; automatic routing has its own opt-in. Switching accounts requires saving consent for the selected native registration. Gemini remains available independently when configured.

## Boundaries

The C# adapter calls the public Responses endpoint with `store:false`, `stream:true` and bounded explicit input. It validates terminal events and preserves completed output items for tool exchange. It omits unsupported hosted executors. There is no second command runner: tools enter the existing task coordinator and PermissionManager.

Authorization uses dynamic registration, a stable installation identifier, PKCE S256, one-use state/nonce, an exact loopback callback and signed OIDC validation. Issued clients remain distinct from the temporary dynamic registration client. DPAPI protects tokens and account bindings in a separate credential file. Refresh is serialized across processes and rotating tokens are committed atomically. Tokens never enter Core, Brain, tool arguments, INI or diagnostics. Disconnect attempts official revocation and clears local tokens even when remote revocation cannot be confirmed.

The direct C# approach avoids an additional app-server executable and executor boundary. The app-server alternative remains official, but would require additional process and tool-permission integration. The preview does not provide the full paid API feature set, automatic durable HTTP history or guaranteed access to every ChatGPT model.

## Persistent tasks

An objective, an inference round, a native operation and a checkpoint have separate identities and lifetimes. The original TaskId survives queued rounds and native owner resumption. Completed tool outputs can cross bounded ChatGPT rounds in a limited in-memory protocol cache; durable operation receipts and Brain remain authoritative across restart. The cache itself is not restored or interpreted as permission.

The `CognitiveTasks` INI section controls cumulative cycles, active minutes, tool calls, provider calls, recovery attempts, result megabytes, idle minutes and an independent per-tool timeout. Model deadlines remain bounded in provider settings. Gemini suspends model waiting while a native tool executes; owner/task cancellation remains effective. A timeout preserves confirmed receipts and allows only bounded recovery. No provider switch is triggered solely by timeout or a shell error.

Automatic routing considers structured native task stages and account availability. Manual choice wins. Native continuation retains the actual recipient scope. A status-only question cannot start repository acquisition. Permission denials and uncertain effects stop continuation. Identical mutable operations with confirmed or uncertain receipts cannot replay, including before process restart.

Task controls expose pause, cancel, native resumption, actual counts, provider/model and errors. Restart requires an explicit owner gesture and reacquires disclosure permission. No process is resurrected by PID. The assistant queue processes owner messages between bounded cycles; a chat reply still waits for the current inference to yield. Native task status remains independently readable.

Successful dialogue with no operations, plan, acquired resources or pending protocol work releases its temporary task slot. The conversation history remains available. Routine chat therefore cannot exhaust the retention allowance reserved for operational objectives.

## Repository learning

Acquisition and indexing are measured separately from module analysis. `record_repository_analysis` accepts only current acquired source versions actually read by the task, with literal evidence and hash-pinned graph relations. Notes stay ordinary authoritative Brain Markdown. This records an interpretation, not verified comprehension, applied skill or successful operational outcome.

Skill validation remains strict. Closed rejection categories identify malformed identity, lists, guidance kind, evidence/outcomes and stale or nonliteral sources without exporting content. Historical generic rejection logs cannot retroactively establish an exact cause.

## Evidence and limits

The private local validation report is `_Output/Validation/Laura2/ChatGPTIntegration/LAURA-CHATGPT-INTEGRATION.md`. It distinguishes real authorization/inference, synthetic protocol tests, measured repository coverage and remaining acceptance work. Test credentials, Brain contents, checkpoints and diagnostic payloads are excluded from source and distribution.

Official specifications reviewed: [overview](https://developers.openai.com/siwc/token-sharing-open-source), [registration/sign-in](https://developers.openai.com/siwc/token-sharing-open-source/sign-in), [models/inference](https://developers.openai.com/siwc/token-sharing-open-source/models-and-inference), [limitations](https://developers.openai.com/siwc/token-sharing-open-source/preview-limitations), and [app-server](https://developers.openai.com/siwc/token-sharing-open-source/codex-app-server).

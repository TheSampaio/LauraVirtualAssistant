# Gemini and native agent execution

This document describes the current architecture after the 2026-10-17 agent rework. Earlier dated cognitive and permission validation reports describe historical versions.

Gemini Live is the only conversational and voice implementation. The composition root no longer registers OpenAI, a local conversational model, Whisper, offline Windows synthesis, CUDA, or ONNX inference. Model discovery uses Google's existing API and its independently reviewed Live capabilities. Removing model inference does not remove audio capture/playback, the local clap detector, or the Markdown Brain.

Brain retrieval uses the existing FTS5 index, passage extraction and directed graph. Markdown remains authoritative. Derived indices remain outside the vault and can be rebuilt. No embedding service receives the personal vault automatically. Legacy embedding paths can survive INI migration as inert settings; no runtime loads them.

Legacy Local, Auto and OpenAI provider selections migrate to Gemini with cloud conversation disabled. Existing explicitly enabled Gemini configurations retain their enable state. The native Connection page exposes an explicit enable switch, protected Google credential, Live model selection and five supported voices. It no longer offers Mark-LV key import. Laura's retired OpenAI credential is removed without decrypting it or accessing another application's configuration. Confirmed cleanup hashes only recognized former downloads in Laura's own Models directory; shared, edited and unknown files are preserved.

## Authorization

The native permission manager remains the single dispatch gate. Ask requires native review of ungranted effects. Approve for me applies the existing resource policies and bounded installed-program version-query policy: literal arguments, an installed executable location, a real native task, the assigned workspace, a known actual disclosure recipient, and no task environment overrides. Arbitrary shell scripts do not inherit a read-only label from model arguments.

The structured `clone_repository` operation adds a separate automatic effect: anonymous HTTPS GitHub source, installed Git, a nonexistent destination whose existing parent and target are inside the assigned workspace, fixed arguments and a fixed task-only Git environment. It disables global/system configuration, credential helpers, hooks, templates, submodules, symlinks and redirects. It does not install dependencies or execute repository code. Other Git/PowerShell forms remain subject to their own review. These controls use the documented [Git configuration environment](https://git-scm.com/docs/git) and [clone options](https://git-scm.com/docs/git-clone); a working directory still is not an OS sandbox.

Full access is off by default. Selecting it and saving presents a native warning. A separate current-user DPAPI preference records the owner's confirmation; editing an INI value alone cannot activate full access on startup. The mode removes per-operation application approval while preserving registered-tool checks, protected application state, resource integrity, cancellation and Windows account restrictions. No process is automatically elevated and no UAC/ACL bypass is implemented. Subsequent dispatch rechecks the current policy; revocation affects new actions. Already dispatched external effects are not rolled back.

The model receives the actual mode as context, never as authority. Generic pretend authorization tools are removed. Native tools construct task and operation identities; model JSON cannot supply approval state. Untrusted repository content, websites and recalled notes cannot change the native preference.

## Processes and PowerShell

The default app-owned Workspace exists before commands are admitted. Missing executable and missing working directory have separate diagnostics. PATH resolution refreshes process, current machine and user entries without modifying the owner's Windows environment. Absolute paths, quoted PATH entries and duplicate directories are handled before execution review.

`run_powershell` translates structured command text into the existing `run_program` or `start_program` executor. Windows inbox PowerShell works without PowerShell 7. Profiles, interactive stdin, opaque encoded host modes and execution-policy overrides remain disabled. Explicit task environment overrides are bounded and frozen before review; their values are not journaled. Arguments and scripts stay literal through native process quoting.

The executor preserves atomic job assignment with `PROC_THREAD_ATTRIBUTE_JOB_LIST`, restricted handle inheritance, no terminal window, bounded separate stdout/stderr, exit code, timeout, cancellation and descendant cleanup. Jobs provide lifetime control under the normal Windows token, not filesystem/network isolation. Starting a command does not establish task success. Results identify the invocation, resolved executable, directory and timestamps; diagnostics correlate task/operation IDs without retaining command bodies or output.

Process arguments accept actual string arrays and legacy JSON-encoded arrays. Supplied malformed values fail before dispatch instead of becoming empty arguments. Explicit environment overrides follow the same rule and reject duplicate case-insensitive names. The model schema declares arguments as an array.

`recent_task_context` recovers at most three checkpoints linked to actual retained prior owner turns. Assistant prose cannot introduce a task identity. Current repository status and cursors are read through native task/recipient authorization. Recovery does not approve actions, replay successful effects, override a denial or renew an exhausted task budget. Disabling retained conversation context also disables this implicit recovery; the owner can select a task in the native Tasks workspace.

## Observability

The existing asynchronous bounded journal and `ILogger` adapter remain in use. Activity logs are accessible through the chat menu and provide level/search filters, selected-event details/copy, automatic refresh while visible, and explicit local diagnostic export. Default detail excludes repetitive audio/transport chunk events; Debug/Trace can be selected without enabling payload logging. The INI preference is sanitized on read.

Events retain run/interaction identities and native task/operation identities where available. Native authorization outcomes, missing paths, process creation failures with Win32 codes, starts and completion states can be distinguished. Rotation, retention, pressure counters, write-failure counters and sanitized ZIP export remain bounded. Logger failures do not authorize, cancel or complete tasks. No level records credential values, raw audio, screenshots, Brain bodies or full model responses.

## Acceptance evidence

The executable `--ui-smoke` harness always uses isolated storage and disabled microphone capture. `--agent-rework-smoke` exercises actual native tools. Adding the explicitly authorized `--agent-live-smoke` reads the existing protected Google credential in-process, sends synthetic task requests through the assistant engine, and checks a unique Downloads clone destination. The harness does not perform the clone itself. `--agent` evidence must be assessed separately from Core tests and provider-free native fixtures.

`--agent-resume-smoke` reopens an existing isolated study and requires actual additional acquisition from the same study after a new conversation request. `--agent-auto-clone-smoke` separately exercises the deterministic automatic clone adapter in its own workspace; it is never counted as the conversational Downloads acceptance clone. Explicit process fixture output is separate from normal diagnostics. Other fixture tools retain only field names, outcome and result hashes, without Brain bodies or YAML.

The current acceptance record and remaining limitations are maintained in `_Output/Validation/Laura2/AgentRework/LAURA-AGENT-REWORK.md`.

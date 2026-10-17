# ChatGPT credential service

Laura uses its own optional Windows service for official ChatGPT plan authorization and inference. Gemini remains independent. The service has no command, file acquisition, Brain, skill, permission grant or hosted tool execution operation. Native tools continue through Laura's PermissionManager.

## Installation

The complete offline distribution includes `CredentialBroker/` and `Install-CredentialBroker.ps1`. In Laura Settings, open ChatGPT and select **Install or repair credential service**. Windows asks for administrator consent. Declining leaves Gemini available and ChatGPT disconnected. The normal per-user application installer does not silently elevate or install a system service.

An administrator can also review and run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-CredentialBroker.ps1
```

The installer verifies the service payload manifest, installs binaries under `%ProgramFiles%\LauraCredentialBroker`, registers the automatic `LauraCredentialBroker` service as `NT AUTHORITY\LocalService`, and restricts `%ProgramData%\LauraCredentialBroker` to that identity, SYSTEM and administrators. Interactive users receive query access to the service, without stop or configuration rights. Updates stop the service before replacing checked binaries. Tokens and registrations are preserved separately from binaries. Installing or repairing the service does not itself start a paid inference or authorize additional credits.

## Boundary

The application and service use a versioned local named pipe with explicit ACLs, rejecting network logons. The service derives the account partition from the authenticated client's Windows SID; request bodies cannot select another Windows user, a storage path, an HTTP endpoint or an executable. The application compares the connected pipe server PID with the running SCM service PID before sending a request. A pipe squatter cannot receive context or a migration payload through this client.

OAuth callback listeners, code exchange, signed token validation, refresh, revocation and inference run inside the service. Session-zero browser launch is delegated to Laura as an official authorization URL. Retained ID tokens are excluded from that URL. Tokens never appear in the Core interface, safe account state, responses, command arguments or child environment. Service-owned DPAPI uses the LocalService identity. Request/reply framing is bounded to 2 MiB; disconnected clients cancel active login/inference. The client requires an explicit result frame, and inference additionally requires the official completed response event.

The isolation protects token confidentiality against ordinary user processes, including Laura's unrestricted current-user commands. It is not a sandbox for commands, a defense against administrators/SYSTEM, or protection against another compromised process already running as LocalService. Authenticated local interactive clients can invoke the fixed inference operations for their own Windows user's registration; this protocol does not attest that a request originated in a particular executable or prove a fresh human gesture. Keep the app's additional-credit allowance zero in ChatGPT usage settings. No client-side parameter can enforce an included-credit-only server policy.

## Existing Laura session

On first connection, the native client transfers only Laura's own existing DPAPI registration store over the SCM-authenticated pipe. The service preserves host, issued client and local account identities, verifies each live signed ID token, writes its own protected store, and acknowledges safe identity metadata. The native client then clears access, refresh and ID token fields in the legacy user store. A retried migration cannot overwrite already-rotated service credentials.

Expired or invalid ID tokens are not accepted as live authorization: only the disconnected registration metadata is retained and the owner must reconnect in the browser. Discovery/network failure preserves the old store and reports failure rather than claiming migration. Close older Laura versions before migration; do not keep a version that can create new current-user credentials running alongside the service-based application. The installer does not search or rewrite unrelated backups. Disconnecting attempts official refresh-token revocation and clears the selected service registration's tokens.

## Validation

`--credential-broker-synthetic` in the developer smoke harness checks framing, malformed input, redacted formatting, closed operations, protected-store preservation and pipe-squatter rejection without real authorization. It explicitly does not claim installed service isolation.

`--credential-broker-live --included-plan-only-confirmed` checks the actual installed service, ordinary-user denial of the service data root, cleared legacy token fields, preserved account selection, official account catalog and a completed inference using the previously verified model. It writes only safe check names, model names and measurable usage. This run requires the owner-authorized service installation and account session. Administrative installer completion alone is insufficient acceptance evidence.

To remove only the service, an administrator can stop and delete the fixed service with Windows service management. Preserve service data until official logout/revocation and an explicit decision about data removal; uninstalling binaries alone does not revoke an OAuth grant.

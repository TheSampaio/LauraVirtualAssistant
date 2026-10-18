# LAURA Brain

> Historical architecture and validation record. The 2026-10-17 [agent rework](laura-agent-execution.md) supersedes provider, model and approval descriptions below. Gemini is now the sole conversation/voice provider; local inference and ONNX adapters are retired.

## Architecture and integration

The existing application is native Windows MAUI/WinUI. Gemini Live remains the conversation and voice provider. The previous SQLite memory combined explicit facts and raw transcripts; Brain replaces durable fact storage with Markdown while retaining the existing bounded transcript database. `BrainConversationMemory` implements the existing `IConversationMemory` contract, so native save/recall/forget tools and conversation history continue to work. All legacy facts are copied once, including their dates, without deleting source rows. An archived Markdown receipt prevents deleted facts from returning on restart.

Core owns the codec, graph resolution, consolidation, indexing and portable storage interfaces. App owns native file/editor/graph controls and trusted user confirmations. `ServiceConfiguration` injects storage and embedding providers. No browser frontend, Obsidian runtime, local conversation model or paid Brain server is required.

Markdown is authoritative. Deleting derived indices rebuilds search from files, never the reverse. SQLite FTS5 and sentence vectors share a per-installation cache database under `%LOCALAPPDATA%/Laura/BrainCache`, outside the shared vault. Diagnostic installations use their isolated storage instead. Raw chat transcripts remain separate from durable knowledge; clearing history does not erase notes.

## Files and schema

Default location: `Documents/Laura Virtual Assistant/brain`. Choose another dedicated folder under **Memory → Manage Brain**. The application creates a readable `README.md` and creates category folders as actual knowledge needs them:

```text
brain/
  README.md
  owner/preferences/   owner/goals/
  projects/
  knowledge/facts/     knowledge/entities/
  memories/decisions/
  procedures/
  skills/<name>/SKILL.md
  inbox/
  archive/revisions/   archive/conflicts/   archive/deleted/
```

No invented profile or personal example is installed. All test fixtures are synthetic. File names are readable slugs plus short disambiguation hashes; IDs remain independent of names and locations.

Example of an authored note, using fictional content:

```yaml
---
schema_version: 1
id: 556b05d6-432c-4bfa-9200-ad9bd191f816
title: Demonstration project
type: project
memory_type: semantic
created_at: 2026-10-12T12:00:00+00:00
updated_at: 2026-10-12T12:00:00+00:00
status: active
confidence: 0.8
importance: 0.7
source: user_declaration
key: project.demonstration
tags: [project]
aliases: []
revision: example_revision
previous_revision: ''
relations: []
---
# Demonstration project

An explicitly declared fictional project.
```

The codec preserves unknown frontmatter fields, including imported skill descriptions and dependencies. YAML is bounded by depth/node counts; cyclic aliases, invalid IDs and unsupported schema versions are rejected without embedding private source text in exceptions. Markdown files are bounded to 1 MiB. Related attachments are copied as bytes, up to 64 MiB per file. The adapter enumerates at most 50,000 files/directories.

Statuses are `active`, `draft`, `conflict` and `archived`; memory classifications are `semantic`, `episodic` and `procedural`. `revision` and `previous_revision` record authored versions; previous text is retained as Markdown in archive. Working memory is transient current user context and does not confer permissions.

Wikilinks support `[[Title]]`, `[[relative/path|Label]]`, headings and `[[id:stable-id]]`. Resolution uses ID, exact root/relative paths, then unique titles/aliases. Ambiguities remain integrity issues instead of selecting an arbitrary note. Moving files or folders retains former paths/names as aliases. Copies receive new IDs, remap internal typed relations and retain `derived_from` provenance. Delete is reversible archival. Attachments and empty folders also support copy/move/archive.

Relations are directed and carry target ID, type, weight, confidence, evidence and source. Supported types: `related_to`, `belongs_to`, `depends_on`, `uses`, `prefers`, `contradicts`, `supports`, `derived_from`, `developed_by`, `part_of`, `learned_from`, `replaces`. Authored wikilinks produce attributed `related_to` edges. Vector similarity never manufactures factual relationships.

## Learning and retrieval

After a user turn, a bounded background queue examines explicit durable declarations. English and Portuguese catalogs recognize editor/language/answer-style preferences, goals, named projects, decisions and procedures. Generated assistant statements are never automatically accepted as facts. Credentials and sensitive contact/financial/health markers are excluded from automatic capture; deliberate manual storage remains possible. This is conservative rule extraction, not a general-purpose LLM fact extractor. Unsupported phrasing produces no automatic note; explicit memory tools and the editor can still save it.

Examples: “I am working on project Demonstration”; “I decided to use CSharp in project Demonstration”. These persist a project, decision and named entity with evidence-backed `uses`/`belongs_to` relations. Repeated declarations consolidate by stable key and normalized text without inflating confidence. Contradictory automatic preferences create a draft linked by `contradicts`, preserving the previous active version. Review can accept the correction with history, or preserve both. Explicit memory corrections update the same ID and retain its prior revision.

Retrieval combines FTS5 lexical matches, optional neural cosine similarity, actual neighboring graph edges, recency, confidence and importance. Search supports a temporal cutoff in the domain API. UI search includes note body/semantic results as well as paths, titles and tags. Empty recall is allowed; absent evidence is never replaced by fabricated context. Contradictions are identified in attributed recall. The default per-turn budget is 5,000 characters (configurable 500–16,000), with at most eight bounded excerpts. Ranking weights and graph limits are local `[Brain]` INI settings.

Typed Gemini requests refresh actual files and retrieve relevant context before submission. It is transmitted as explicitly untrusted JSON data alongside the user's request; imported knowledge cannot override instructions or grant tools. Explicit owner-overview requests have localized whole-request recognition and retrieve a bounded knowledge overview instead of searching for question words. Live voice sessions are instructed to use `recall_memory` for targeted knowledge; an empty query provides an overview. Skill discovery metadata and hash-approved guidance have separate payload fields from ordinary facts, so approved writing instructions are usable without treating every knowledge document as an instruction. Disabling conversation memory disables factual recall and automatic learning; approved task skills remain independently available. Existing Gemini cloud conversation still requires its enabled setting and protected key.

### Local sentence embeddings

ONNX Runtime runs optional CPU sentence embeddings locally, with one inference thread and no downloads. Configure a trusted compatible `.onnx` and its matching `vocab.txt` via Manage Brain. The implemented tokenizer targets uncased BERT WordPiece models with int64 `input_ids`, optional `attention_mask`/`token_type_ids`, and float sentence or token outputs; token outputs use mean pooling and normalization. It is not an arbitrary model/tokenizer loader. Input is bounded to 128 tokens; longer documents currently use their leading title/body segment rather than chunked embeddings. Choose a model appropriate to your languages; this adapter does not claim universal multilingual accuracy.

Weights are deliberately not bundled or installed into personal settings. Without a compatible model, the UI reports semantic retrieval unavailable and lexical/graph retrieval still operates. Vectors are incrementally reused by content hash and model file fingerprint. The exact vector index uses an in-memory cosine scan, suitable for personal vaults; it is not a large-scale ANN service. No embeddings or note bodies are sent to an embedding server.

## Native workspace

Memory contains a virtualized file tree, global/local force-directed graph, native Markdown reader, editor and split mode, plus node metadata and inbound/outbound relations. It supports folder/type/search filters, local expansion depth 1–4, pan, drag, wheel/button zoom, fit, tooltips, selection and opening. Communities are connected components of real edges, not semantic claims. Layout work runs off the UI thread and unchanged layouts/positions are retained. The default render limit is 400 nodes (50–1,000 configurable) with bounded edges. Large graphs are deliberately filtered/capped rather than drawing every object.

Long readers load native paragraphs in batches, with access to remaining paragraphs. The reader renders conventional Markdown as native text and resolves clickable wikilinks; it is not a full Obsidian renderer (no embedded HTML, plugins or arbitrary widgets). Raw YAML can be inspected/edited explicitly, with stable ID protection; repairing malformed notes preserves the original source in archive. Native tree controls provide the accessible alternative to pointer-oriented graph operations. Compact windows collapse details into a selectable view, group note actions and prioritize the selected node label; other nodes remain selectable with tooltips. The workspace shares the HUD's dark/red palette exclusively. Bundled Lucide SVGs distinguish folders, Markdown notes, attachments and actions; icon-only controls retain localized native tooltips and accessibility descriptions. View controls and graph tools are grouped separately, the active view and selected file are highlighted, and document actions reflect actual selection and unsaved edits. The inspector exposes source, classification and attributed connections, with skill YAML available through an explicit metadata disclosure.

The existing central reactor is retained. Navigation switches immediately, followed by a 300 ms lightweight arrival animation; settings controls are cached. Motion respects Windows reduced-motion preferences. Reactor phase persists while hidden/off-page.

## Skills

Importing Markdown creates an inert draft with preserved metadata. Files already present under `skills/` are discovered by rescans. The model can consult `list_brain_skills` for real IDs/names/descriptions and approval state, including disabled imports, then choose relevant guidance across languages with `read_brain_skill`. It must not equate a disabled skill with a missing ability or depend on name/tag keyword overlap for a writing task. Typed turns include a bounded metadata catalogue, and both voice/text sessions receive discovery-tool instructions. Direct matching also considers descriptions.

Review shows the full frontmatter/body in a scrollable native dialog, revealing a hidden window only for the requested approval and preserving the current page. The Brain page and conversation tool share activation behavior: native user confirmation records an exact ID/file-hash approval in local INI, outside synchronized Markdown. Editing or syncing a changed skill invalidates approval; the read tool rechecks actual file contents after review and refuses stale instructions. Approved guidance is bounded by the configured context budget and reports truncation. No private user's skill was automatically enabled during diagnostics.

No dependency installation, script execution, new tool registration or arbitrary skill/plugin code execution is implemented. An imported frontmatter `enabled`/permissions field cannot authorize anything. Existing consequential native tool confirmation remains authoritative. Ordinary knowledge is always data. Startup briefing is queued once after settings and provider availability; clap wake acknowledges briefly then enables listening. Standby closes cloud capture and plays a farewell; an acknowledgment is never marked as already spoken before playback occurs.

## Location, sync and recovery

The default vault is `Brain`. Newly created directory components use an uppercase initial, such as `Knowledge/Facts`, `Owner/Preferences`, `Skills` and `Archive/Revisions`. Destination naming retains the actual spelling of existing folders and leaves document filenames unchanged. New notes, explicit folders, move/copy destinations and generated archival paths share this policy. Reading, editing, backup and migration preserve authored paths, stable identities and trusted skill hashes; legacy lowercase vaults on case-sensitive filesystems remain usable. Native selection resolves the actual destination spelling after file operations.

Manage Brain offers create, existing vault, migration, integrity/rescan, index rebuild, private ZIP backup/restore and local retrieval settings. Selecting a new/existing vault can recover an unavailable configured location without first opening it; migration requires the source to be readable. Migration validates independent locations, copies and verifies bytes, then switches the local setting. Originals are retained even after success. Identical partial copies can resume; divergent destination files abort without overwriting. A final inventory/content comparison detects source changes during migration. Restore targets an empty dedicated folder, rejects escaping paths and bounds expanded content to 512 MiB.

Desktop sync uses the selected filesystem provider (for example, an actual OneDrive-synchronized folder). Markdown remains usable offline when files are available locally. FileSystemWatcher debounce plus a 30-second full reconciliation detects new/changed/deleted files and missed events. Unchanged notes are reused by exact hash; active SQLite databases are kept outside the shared root. Normal cloud-file placeholders are allowed; unavailable hydration/access is reported as an integrity issue. Redirecting symlinks/junctions are rejected.

Writes use a per-vault local exclusive lock, flushed temporary file and atomic replacement with expected-hash checks. Stale edits preserve both the current file and the unsaved branch under archive. Duplicate IDs in sync copies are quarantined from retrieval and the graph until manual selection: retain both with new IDs/contradiction attribution, or archive other versions. Timestamp alone never chooses a winner. A sync provider that silently overwrites a file without preserving its losing version cannot be made conflict-safe by a local watcher; this implementation does not promise a distributed transaction or CRDT.

`IBrainStorage` exposes portable document identifiers and operations; consumers do not require mobile filesystem paths. Windows supplies `FileBrainStorage`. Android/iOS targets, document-provider adapters and an official cloud sync API are **not implemented** in this Windows-only project. A future mobile implementation must supply authorized document access/API and its own local cache, preserving the same IDs/schema. It must not assume OneDrive's internal mobile directory is a readable folder. See [MAUI file picker platform behavior](https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/storage/file-picker).

## Privacy and Git

Notes, revisions, attachments, transcript databases, derived FTS/vector caches and ZIP backups contain readable private information. Neither Markdown nor ZIP backups are encrypted by this feature. A synchronized folder does not imply end-to-end encryption. Access control/encryption is the chosen OS/provider's responsibility; compatible plaintext is intentional. Repository roots and embedded repositories cannot be imported as vault documents.

`/brain/`, local settings/credentials, databases, weights and generated diagnostics are ignored. A custom vault inside a Git worktree gets an exact escaped exclusion before writes. The adapter checks tracked contents first and refuses a tracked vault; it never deletes copies, silently untracks files or rewrites history. `Build/Verify-BrainPrivacy.ps1` audits configured paths, tracked private artifacts and historical vault paths without changing anything. Source folders named `Laura.Core/Brain` and `Laura.Core.Tests/Brain` remain versionable.

If previously tracked contents are found, preserve a private backup and review `git rm --cached` against the exact vault paths as a separate index-only operation. `.gitignore` cannot remove committed disclosures; report history exposure and obtain explicit authorization before any history rewrite. Do not print private contents while diagnosing.

## Verification

External YAML may declare additional bounded relation identifiers, such as `about_owner` or `has_related_fact`. These remain authored graph data and round-trip without changing stable identities or granting tools/skill permissions. Native authoring still offers the established relation vocabulary. Invalid YAML, schema versions, identifiers and duplicate identities retain their integrity checks. Rejecting an entire otherwise valid note solely for an unfamiliar relation type previously hid external owner/fact files from both the graph and retrieval.

Folder disclosure is an explicit accessible SVG action, with Left/Right/Enter keyboard support. Restoring native selection after a tree projection never toggles folders or reloads the current editor draft. File inventories and query matches are reused for the same snapshot/query, and older asynchronous search results cannot replace a newer projection. The empty-state overlay follows the actual filtered graph node count; newly visible nodes trigger a fit to the viewport. Isolated native verification invokes the actual disclosure button repeatedly and checks bounded tree reconstruction. The supplied archive and physical Markdown were checked read-only; all 11 documents parsed, with 7 graph candidates. No personal vault files or skill approvals were modified.

Automated tests cover actual Markdown persistence/restarts, graph provenance, external changes/deletions, ambiguity, moved aliases, duplicates, history, stale edits, contradiction review, cache deletion/corruption, semantic retrieval, context bounds, migration/resume, binary attachments, backups/path traversal, symlink escape, Git tracking protection and skill hash approvals. Native `--ui-smoke` uses synthetic declarations and isolated storage to verify dark/red graph/local/reader/editor/split views, EN/PT localization, 720×600 graph/reader/details/split layouts, actual editor writes and the previous HUD/audio/startup checks. It never captures a physical microphone.

An optional real-model test uses `LAURA_TEST_EMBEDDING_MODEL` and `LAURA_TEST_EMBEDDING_VOCABULARY`; it checks 384 finite dimensions and paraphrase retrieval without a lexical overlap. Without these variables, it checks the explicit unavailable-provider behavior. The verification model is the public [all-MiniLM-L6-v2 ONNX variant](https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/blob/main/onnx/model_quint8_avx2.onnx), downloaded only to ignored diagnostic output; no weights ship. Tests do not claim measured Portuguese retrieval quality, live Gemini prompt quality or cross-device cloud conflict guarantees.

Functional verification on 2026-10-12: Release solution build succeeded with zero warnings/errors; all 159 Core tests passed, including the optional real ONNX model, startup-once/deferred-credential behavior, description-based skill discovery, owner overview recall and separate skill/fact payloads. Native EN/PT diagnostics passed graph/local/reader/editor/split views, actual Markdown writes, 720×600, monitor edge/gutter geometry, hidden startup, conversation scrolling, reactor continuity and eight silent native audio restarts. Native skill tools discovered a disabled synthetic guide and loaded its actual Markdown after isolated hash approval. Typed standby was checked against false speech suppression without test credentials. The read-only Git privacy audit passed. These diagnostics do not claim a live Gemini writing response or audible cloud farewell test. The installer was not rebuilt or revalidated in this task.

The subsequent Brain visual refinement passed a Release build with zero warnings/errors, all four localization tests and isolated native EN/PT checks for graph/local/reader/editor/split, modified versus saved Markdown actions, compact inspector, 720×600 layout and return to 1440×900. Screenshots confirmed rendered SVGs, red file selection and full-width inspector controls. The inspector moves together with its Border while its ScrollView retains the same parent; this avoids WinUI retaining the former sidebar's clip when opening compact details. The privacy audit passed again. No light palette, runtime icon downloads or additional UI dependency is used.

The reference-inspired workspace integrates branding, title, search and filters in one desktop header. The file sidebar includes root navigation and real index statistics. The inspector uses aligned metadata, tag chips and a bounded Markdown preview. Curved attributed edges, a selected-node halo and an interactive minimap remain native vector drawing; the minimap uses actual graph coordinates. Ctrl+K focuses search only while this page is active, and its native accelerator is detached from the previous handler before a cached page is reconnected.

Performance diagnostics use only isolated synthetic storage and content-free numeric timings. A fixture with 200 retained messages and 100 Markdown notes exposed eager construction of 200 chat controls during hidden startup and sequential Brain/transcript initialization. The HUD now uses a virtualized native CollectionView, defers restoring history until visible, and preserves all 200 available messages and follow/reading behavior. Reading transcripts does not initialize or migrate the Brain; independent background warmup still imports legacy facts before semantic operations use them. Shutdown closes capture and speech before waiting for Brain warmup. Cached Brain pages retain their editor, graph and selection; unchanged snapshots reuse file inventories and skip rebuilding the workspace. Responsive grid definitions change only at layout breakpoints, view changes or inspector toggles. Settings avoid notifying unchanged endpoint-name items. Navigation starts the existing 300 ms transition alongside native presentation instead of waiting for presentation to finish.

Transient zero-width events during native handler disconnection do not switch the HUD or Brain into compact layouts. Shared bundled icon sources are cached, avoiding repeated source changes when microphone/state feedback reuses the same glyph.

The isolated baseline recorded 1716 ms for the application startup phase after window creation, up to 453 ms for eager HUD reload and roughly 700–720 ms for a settings open/return cycle including two 300 ms animations. Optimized runs recorded 302–402 ms for that startup phase, at most 12–31 ms for HUD reload, and roughly 650–684 ms for the same navigation cycle. These are local diagnostic measurements, not end-to-end process launch or live Gemini latency guarantees; Brain/index warmup still takes place in the background. All 160 Core tests passed, including independent transcript reads with an unavailable vault and the optional real ONNX model. Native diagnostics retained EN/PT, editor persistence, repeated page reopening, full/compact layout, conversation scrolling, silent playback restarts, hidden startup and microphone safety checks.

Dependencies: YamlDotNet (MIT), Markdig (BSD-2-Clause), ONNX Runtime (MIT plus included notices), existing Microsoft.Data.Sqlite/FTS5. Locks and the license inventory record actual resolved versions; upstream texts accompany distribution.

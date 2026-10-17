# Brain creator provenance audit

Audited BrainNote, BrainMarkdown, BrainService, BrainSkills, FileBrainStorage, BrainIndex, BrainGraphView and MemoryPage before implementation.

## Preserved contracts

Markdown/YAML schema 1 remains authoritative. Unknown frontmatter mapping fields survive edits. IDs, typed directed relations, wikilinks, optimistic hashes, revisions, skill approval and storage limits remain unchanged. The index is disposable and needs no schema migration. Move/rename operations retain IDs and creator fields through record copies. No private Brain documents were read or changed.

## Creator semantics

origin_agent is creator provenance only. It grants no ownership or permissions. Exact recognized values: laura (#D83A49), codex (#3B82F6), claude (#F59E0B). Missing/unknown origins use the theme default. Tags cannot override the native graph color.

New native notes receive laura unless explicitly attributed. Native edits preserve the on-disk origin and merge tags. Imports retain supplied origins and remain disabled; missing origins and old notes are not classified. External agents writing Markdown must follow this same contract; LAURA cannot enforce writes made by another program.

Matching origin tags are added without removing other tags. Contradictory tags produce origin_tag_conflict and remain for human review. Preserving all tags prevents silently repairing contradictory Obsidian colors. Non-text tag structures are rejected rather than silently discarded.

Native selection keeps the creator fill and uses size, a text-colored outline and halo. Tooltip/programmatic selection descriptions include creator text. Community layout remains; creator colors take precedence over community colors. Screen-reader navigation and high-contrast visual QA are not established by domain tests.

## Optional shared configuration

Memory > Manage > Review shared Brain configuration reads .shared-brain/config.json under the user profile only on request. vaultPath must be absolute. Both paths are shown before explicit selection using the existing location workflow. No automatic switch, migration or config rewrite occurs. Normal startup and UI smoke do not consult the external config. Missing/invalid configuration leaves the active folder unchanged.

## Obsidian groups

Memory > Manage > Configure Obsidian creator colors explicitly merges .obsidian/graph.json in the selected vault. Groups:

| Query | Color |
| --- | --- |
| tag:#origin/laura | #D83A49 |
| tag:#origin/codex | #3B82F6 |
| tag:#origin/claude | #F59E0B |

Other settings/groups survive. Existing conflicting creator colors are rejected without overwriting. Writes use optimistic hashes and atomic storage. Untagged notes retain defaults unless another user group matches. No CSS, plugin or API is needed. Reopen the graph/restart Obsidian if settings are cached; avoid applying during concurrent editor saves. The JSON layout is an interoperability detail rather than a versioned public API.

Manual setup also works through Graph view > Settings > Groups: https://obsidian.md/help/plugins/graph

## Verification

Synthetic fixtures and random temporary storage cover colors, unknown frontmatter, long/scalar tags, edit/move/rename provenance, imports, old notes, rebuild/fresh cache, conflicts, shared configuration and idempotent Obsidian merging. Existing Brain regression tests cover concurrent edits, history, graph integrity and cache recovery. No real Brain or Obsidian data is changed by tests. Live Obsidian/native visual rendering remains unverified. The shared status command was blocked by PowerShell execution policy. See the task report for final test counts and build limitations.

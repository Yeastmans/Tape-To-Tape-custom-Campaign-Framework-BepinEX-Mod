# Blank Head Templates Implementation Plan

> **For agentic workers:** Use the existing isolated checkout, focused test-first changes, and independent review. The layered-file writer is an independent delegated component; integrate and verify the complete editor flow before release.

**Goal:** Make New head > Blank template a usable starting point, with options and an equipped-helmet preview toggle.

**Spec:** The user chose a combination of featureless head shapes and separate guides, external layered-file editing, and game helmets retaining normal gameplay. They explicitly requested a helmet toggle in the Head Editor preview and New head > Blank template > options, including fully blank artwork with guides.

**Architecture:** Add three blank starter choices: Plain head with guides, Transparent with guides, Transparent only. Use the existing basic native layout automatically for blank starts; keep custom native-layout selection available in the existing native mode. Preserve 512px artwork, directions, fitted-head bindings and existing project compatibility. Export PSDs with named Base head / Face / Hair / Guides layers and clean PNG fallbacks; guides are separate/hidden in exported drawing files. Preview helmets are rendered separately and never alter the editable or exported PNGs. Keep actual native equipped-helmet behavior unchanged.

**Tech stack:** C#/.NET 8, Avalonia, SkiaSharp, existing GameArt extraction and custom-head contracts. PSD v1 follows Adobe's published format.

## Work

- [x] Implement and independently parse layered PSD output, including transparency, visibility, names and composite behavior.
- [x] Add starter options and deterministic featureless shapes/guides while preserving native layout metadata and unique project names.
- [x] Add editor preview direction and helmet toggle using actual native helmet artwork and correct placement; keep preview-only overlays out of saved art.
- [x] Expose layered-file opening/export and clear external-editor steps. Keep ordinary head/goalie-mask editing compatible.
- [x] Run focused data and actual Avalonia flow checks, inspect rendered UI and PSD output, obtain independent review, and address material findings.
- [ ] Build and publish the next GitHub revision under the user's standing release authorization. No live game launch unless required; all checks silent.

## Review focus

Generated guides must not contaminate gameplay artwork; alpha and 512px placement must round-trip; helmet preview must show equipped helmet artwork rather than merely switching fitted hair; preview controls must not modify helmet support or files; missing game cache and existing project names must fail safely; source PSD regeneration must preserve user edits; installed/shared heads must include the complete runtime image set.

# dotlens-mvyj — CA2007/CA2016 analyzer diagnostics not generated (2026-11-16)

## Acceptance results

All four criteria executed: `Assert.Skip` count 0; `AnalyzerDiagnosticsTests` 8/8, 0 skipped; full suite 1112/1112, 0 skipped; the `[manual]` root-cause item was settled by a recorded reproduction (table in the nib's closing summary and `context.log`).

## Deviations

- The item had no `## Acceptance`; auto-deliver wrote one before executing (see the open question).

## Follow-ups

dotlens-33p3 (get_diagnostics gives no sign of a project with no framework references).

## Open questions (filed: dotlens-qw76)

### Acceptance criteria for dotlens-mvyj were written by the run
- **Kind:** confirm
- **From:** dotlens-mvyj
- **What happened:** The bug had investigation steps but no acceptance criteria. The run added four: no `Assert.Skip` in the analyzer tests, the analyzer test class and the full suite pass with 0 skipped, and a recorded reproduction of the root cause. It then removed the skips and asserted the diagnostics exactly. The reproduction confirmed that CA2007/CA2016 vanish when a project resolves no framework references, but could not recreate the January SDK 10.0.0 environment, so the exact cause then stays unverified.
- **Options:**
  - A. Accept — the tests now fail loudly if the diagnostics go missing again.
  - B. Reopen and require more, for example installing SDK 10.0.0 to reproduce the original failure.
- **Recommendation:** A. The skip hid a broken load in every reproduced case, and global.json now pins SDK 11.

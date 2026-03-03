## Overwrite Migration RCA

### What happened
- Overwrite mode followed a replace-first path when a dashboard match existed.
- If `ReplaceDashboardAsync` failed (for example stale `CxDashboardId`), the flow marked the item as failed and stopped.
- Non-overwrite path still used upload/create for unmatched dashboards, so behavior looked better when overwrite was not selected.

### Why it happened
- The overwrite branch handled failure as terminal, while the non-overwrite branch had resilient upload handling.
- Shared migration steps were partially duplicated, which allowed branch behavior drift.
- The overwrite contract did not explicitly require fallback semantics for replace failure.

### Why it was missed initially
- No regression test covered stale checkpoint ID + overwrite enabled.
- No branch-parity tests compared overwrite vs non-overwrite outcomes on the same fixtures.
- Existing tests focused on happy-path import/migration outcomes, not failure-mode transitions between replace and create.

### Fix implemented
- Keep overwrite matching precedence: checkpoint ID, then catalog name+folder.
- On replace failure, log warning and fallback to upload/create in the same run.
- Reuse shared upload result classification for consistent status handling.
- Add regression tests for overwrite reprocessing, replace-failure fallback, overwrite no-match create, and non-overwrite skip.

### Prevention actions
- Enforce mode-branch parity checks and replace->create fallback clarity in agent guidance.
- Require paired branch tests for feature toggles and explicit RCA notes for closed defects.

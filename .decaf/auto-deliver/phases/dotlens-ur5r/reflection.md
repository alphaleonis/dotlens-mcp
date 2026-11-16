# dotlens-ur5r — get_file_outline omits extension block headers (2026-11-16)

## Acceptance results

`--filter-method "*FileOutline_ExtensionBlock*"`: 5/5. Full suite: 1147/1147 with `--max-threads 8`.

## Deviations

- Four other defects in the same tool fixed in the same change (one-line signatures, private fields, expression-bodied indexers, doc comments), so exact-text tests did not encode wrong output.
- Seven reproduced outline defects grouped into one follow-up rather than seven, to keep the bug queue converging.

## Follow-ups

dotlens-b6ue.

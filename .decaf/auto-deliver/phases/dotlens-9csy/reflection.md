# dotlens-9csy — get_file_overview omits enums and delegates (2026-11-16)

## Acceptance results

- `--filter-method "*FileOverview_EnumsAndDelegates*"`: 4/4. Full suite: 1112/1112 with `--max-threads 8`.

## Deviations

- The new Full test would have pinned a delegate declaration without its signature; that was fixed in scope (delegates now use the method-declaration options) instead of being asserted.

## Follow-ups

dotlens-nvro (Full declarations drop ref returns, nullable annotations, constraints and default-value detail).

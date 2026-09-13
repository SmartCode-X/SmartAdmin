# Issue tracker: GitHub

Issues and PRDs for this repo live as GitHub issues: https://github.com/SmartCode-X/SmartAdmin/issues. The default surface is the `gh` CLI (already authenticated on the maintainer's machine); the web UI works the same.

## Conventions

- **Auth**: `gh auth status` must be green. Never paste a token into files, commit messages, or chat.
- **Issue ids**: GitHub numbers issues `#123`. Use that number when referring to an issue in commits or docs; `Closes #123` in a PR body closes it on merge.
- **Create an issue**: `gh issue create --title "..." --body "..." --label needs-triage`.
- **Read an issue**: `gh issue view 123 --comments`.
- **List issues**: `gh issue list --state open --label <label>` (`--state` is `open` / `closed` / `all`).
- **Comment**: `gh issue comment 123 --body "..."`.
- **Apply / remove labels**: `gh issue edit 123 --add-label <label> --remove-label <label>`.
- **Close**: `gh issue close 123 --comment "..."` after leaving a closing comment; `gh issue reopen 123` to reopen.

The owner/repo is `SmartCode-X/SmartAdmin`; confirm against `git remote -v` when working in a fork (`gh` picks the repo from the current remote).

## Pull requests as a triage surface

**PRs as a request surface: no.** External PRs are not treated as feature requests here; they go through ordinary code review. Issues are the sole triage surface, using the labels in `triage-labels.md`.

GitHub numbers PRs and issues from one sequence, so `#42` is unambiguous.

## When a skill says "publish to the issue tracker"

Create a GitHub issue with `gh issue create`.

## When a skill says "fetch the relevant ticket"

`gh issue view <n> --comments` and read the body, labels, and comments.

## Wayfinding operations

Used by `/wayfinder`. The **map** is a single issue with **child** issues as tickets. GitHub sub-issues and dependency edges are not relied on, so the plain-text conventions below are the canonical representation:

- **Map**: a single issue labelled `wayfinder:map`, holding the Notes / Decisions-so-far / Fog body.
- **Child ticket**: an issue with `Part of #<map>` as the first line of its body, and listed in a task list (`- [ ] #<child> title`) in the map body. Labels: `wayfinder:<type>` (`research`/`prototype`/`grilling`/`task`).
- **Blocking**: a `Blocked by: #<n>, #<n>` line at the top of the child body. A ticket is unblocked when every listed blocker is closed.
- **Frontier query**: walk the map's task list, drop children that are closed, have an open blocker in their `Blocked by` line, or already have an assignee; first in map order wins.
- **Claim**: `gh issue edit <n> --add-assignee @me` — the session's first write.
- **Resolve**: comment with the answer, close the issue, then append a context pointer (a link to that comment) to the map's Decisions-so-far.

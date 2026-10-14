---
name: architect-review-workflow
description: Automatically run the architect-reviewer agent to inspect code quality and adherence before every git commit. Use when finalizing vertical slices, refactoring, or preparing commits.
---

# Architect Review Workflow

Mandatory development workflow step for ensuring code quality and domain boundaries before committing any changes.

## When to Use
- Before every `git commit` or when completing a vertical slice / feature branch.

## Workflow Instructions
1. **Trigger Review**: Before creating a commit, instruct the `architect-reviewer` agent to inspect staged changes for architectural alignment.
2. **Apply Fixes**: Automatically address any findings from the review.
3. **Commit**: Only after the reviewer approves, instruct `git-committer` to finalize the commit.
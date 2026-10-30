# Job Search Manager – Codex Instructions

## Repository

The canonical Windows checkout is `D:\Computer Everything\Programming\CS\JobSearchManager`.
The canonical origin is `https://github.com/dwcourtney/JobSearchManager.git`.
Use this repository or its verified checkouts, not older source snapshots in the chat workspace.

## Live machine

The live JSM machine is reached with:
```
Host curiosity-codex
    HostName curiosity
    User codex
    IdentityFile ~/.ssh/codex_curiosity
```

When a code change is intended to affect the live application, do not stop after local implementation and testing. Deploy the change to the live machine using the existing project deployment process and verify the live behavior.

## Tests

Keep the unit/regression tests current as the project changes.

- Create new tests when needed.
- Review existing tests when behavior changes.
- Merge or consolidate redundant tests when appropriate.
- Remove stale tests that no longer represent valid behavior.
- Do not weaken tests merely to make a change pass.
- Run the relevant test/validation suite before considering a change complete.

## Theme and UI consistency

Preserve the project’s existing theme and UI rules.

Before changing UI, styles, colors, layout, components, or visual behavior:

- Inspect the existing theme implementation and conventions.
- Reuse existing variables, classes, patterns, and components where practical.
- Do not introduce one-off styling that conflicts with the established theme.
- Verify changes in the relevant light/dark or other supported theme states.

## Git and repository hygiene

Do not allow the local repository and live/deployment workflow to drift out of sync.

Before making changes:

- Check `git status`.
- Check the current branch.
- Check the latest commit.
- Confirm there are no unexpected local modifications or uncommitted changes that could be overwritten or mixed into the task.

During and after changes:

- Keep changes scoped to the requested work.
- Review `git diff`.
- Do not overwrite unrelated local work.
- Do not blindly pull, reset, force-push, or otherwise alter history.
- Follow the project’s existing git synchronization workflow.
- Ensure the local repository remains synchronized appropriately with the expected upstream state.
- Before deployment, confirm exactly which commit/change set is being deployed.
- After deployment, verify that the live machine is running the intended version.

If repository state is unexpected or potentially unsafe, investigate before changing files.

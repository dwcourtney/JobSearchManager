# Curiosity CI/CD operations

## Trust boundaries

GitHub-hosted Ubuntu runners perform all compilation, .NET tests, JavaScript and
theme checks, source validation, and linux/amd64 candidate-image tests. Pull-request
code never runs on curiosity. The repository-scoped curiosity runner receives only
an exact commit already validated by trusted CI, or a manually selected full commit
that is contained in `main` and revalidated on a GitHub-hosted runner.

The curiosity runner is deployment infrastructure. Install it under
`/home/codex/jsm-cicd/runner`, outside `/home/codex/jsm-lab`, with labels:

```text
self-hosted, linux, x64, curiosity, jsm
```

It needs outbound HTTPS and Docker access; it needs no inbound listener. Membership
in the `docker` group is effectively root-equivalent and must be treated as privileged
host access. Do not install .NET, Node.js, npm, Mailpit, or JSM-specific build tools on
curiosity. Docker build stages provide the application toolchain.

Runner registration and service installation are deliberate approval boundaries.
Use GitHub's repository-scoped, short-lived registration token without printing or
persisting it elsewhere. The supported runner service should be configured only after
the private repository exists and the runner package and checksum have been verified.

## Hosted CI

`.github/workflows/ci.yaml` runs for pull requests targeting `main` and pushes to
`main`. It checks out the exact event SHA with persisted Git credentials disabled,
then `scripts/ci-validate.sh` enforces:

1. checked-out SHA equality;
2. locked NuGet restore under SDK 10.0.400;
3. Release build and the complete deterministic .NET suite;
4. JavaScript runtime tests and centralized theme/source checks;
5. `git diff --check` and a clean generated-file check;
6. a linux/amd64 image tagged by full SHA;
7. an OCI revision label equal to that SHA;
8. an ephemeral non-root, read-only container with temporary isolated storage;
9. Docker health plus HTTP 200 and `Healthy` from `/healthz`; and
10. an exact SHA match from `/version`, whose response is limited to `commit`,
    `version`, and `hostingMode`.

The workflow has read-only repository permissions and pins third-party Actions by
immutable commit. CI is independent of curiosity and completes normally while the
lab machine is offline.

## Deployment workflow

`.github/workflows/deploy-curiosity.yaml` is separate from CI. Automatic deployment
is disabled unless the repository variable `CURIOSITY_AUTO_DEPLOY` is explicitly set
to `true` after bootstrap validation. A successful trusted `main` push is the only
automatic source.

Manual dispatch accepts either current `main` or one lowercase full Git SHA. A
specific SHA must resolve to a commit in current `main` history and must pass every
hosted validation gate again. There is no workflow input that reaches a shell as an
arbitrary command.

The deploy job runs only on `[self-hosted, linux, x64, curiosity, jsm]`. It checks out
the exact SHA, builds `jsm:<full-sha>`, confirms the OCI revision, and invokes
`scripts/deploy-curiosity.sh`. A host-side file lock plus GitHub's concurrency group
allows only one active deployment. GitHub's concurrency behavior retains at most one
pending run for the group, so a newer pending deployment can supersede an older one;
`cancel-in-progress: false` prevents interruption of the active deployment.

When curiosity has no matching online runner, GitHub leaves the deployment job queued
until a matching runner comes online. GitHub currently fails a self-hosted job after
[24 hours in the queue](https://docs.github.com/en/actions/reference/runners/self-hosted-runners#routing-precedence-for-self-hosted-runners).
If that happens, power on curiosity and manually dispatch the desired `main` or
validated SHA; hosted CI does not need to be repeated for an automatic run, while
manual dispatch intentionally revalidates the target.

## Host deployment behavior

The controlled manifest is `deploy/compose.curiosity.yaml`. It deliberately declares
only the `jsm` service in Compose project `jsm-lab`. Deployment uses `up --no-deps`
for that service and never invokes `down` or `--remove-orphans`, so the existing
Mailpit service is not recreated. `ai801` is outside this Compose project and is never
selected or cleaned.

The manifest bind-mounts the existing paths without copying, deleting, or replacing
them:

```text
/home/codex/jsm-lab/data/app
/home/codex/jsm-lab/data/dataprotection
```

`/home/codex/jsm-lab/backups` must also exist before deployment, and the deploy script
refuses to continue if any of these three persistent locations is absent. Application
rollback never restores workspace data or Data Protection keys.

The deploy script waits for Docker health, checks `/healthz`, and requires `/version`
to match the target SHA. Only after those checks succeed does it atomically record the
deployed SHA. It records successful full-SHA tags and retains the newest five once five
successful releases exist. Cleanup addresses only verified `jsm:<40-hex-sha>` tags;
it never uses Docker prune commands and never examines unrelated images, volumes, or
networks.

If post-replacement checks fail, the script switches only JSM to the previous image,
then verifies health and, for CI/CD-era releases, `/version`. During the one-time first
bootstrap only, the pre-CI/CD image has no `/version` endpoint; legacy rollback can
therefore prove Docker health and `/healthz` but cannot report a version SHA. After the
first successful CI/CD release, every retained rollback target supports both checks.

## Initial bootstrap sequence

Do not combine approval boundaries. The rollout order is:

1. Commit these source changes with a clean Windows worktree.
2. Reauthenticate GitHub CLI.
3. Create or confirm private `dwcourtney/JobSearchManager`.
4. Add `origin`, push complete `main` history, and push
   `pre-authentication-2026-08-28`.
5. Confirm hosted CI passes.
6. Configure a lightweight `main` ruleset: block force pushes and deletion, require
   the CI validation check where GitHub permits it without mandatory pull requests,
   keep direct pushes, and retain an owner emergency bypass.
7. Register the repository-scoped runner in `/home/codex/jsm-cicd/runner`; install and
   verify its supported service with explicit approval.
8. Leave `CURIOSITY_AUTO_DEPLOY` unset and manually dispatch one bootstrap deployment.
9. Verify exact SHA identity, JSM health, account/workspace persistence, Mailpit state,
   and that `ai801` is unchanged.
10. Only after that verification, explicitly set `CURIOSITY_AUTO_DEPLOY=true`.

## Normal development flow

Develop and test on Windows, commit normally, and push `main` (or open an optional
pull request). Hosted CI performs every gate. Once automatic deployment is explicitly
enabled, a successful trusted `main` CI run queues the exact SHA for curiosity. A
powered-off curiosity does not affect CI; use manual dispatch later if the 24-hour
deployment queue expires. Roll back by manually dispatching a retained known-good SHA
from `main` history, which is revalidated before deployment.

Never commit environment files, credentials, account stores, runtime data, Data
Protection keys, Mailpit state, bundles, runner configuration, or deployment state.
Never place Azure or Cloudflare credentials in GitHub Actions; this deployment does
not use or modify Azure.

# Curiosity CI/CD operations

The canonical repository for Job Search Manager is the private repository
[`dwc5703/JobSearchManager`](https://github.com/dwc5703/JobSearchManager). This is the
one intentional exception to the separation between personal and Penn State GitHub
identities: JSM is a personal project, not coursework, research, or university-sponsored
work, and is hosted under `dwc5703` solely for GitHub Education/Pro private-repository
CI/CD capabilities. Repository ownership is permanently pinned to `dwc5703`; the
`dwcourtney` account must not own JSM or appear as its canonical repository owner.
The canonical Git origin is exactly:

```text
https://github.com/dwc5703/JobSearchManager.git
```

Git repository ownership and commit authorship remain separate. Existing history is
not rewritten, and future commits retain the repository-local identity
`David Courtney <davidcourtney@outlook.com>`. Repository-local Git configuration
requires SSH-signed commits and tags. `.github/allowed_signers` binds the public
signing key to that author for local verification; no private key is committed.
The hosting/authorship split is an intentional infrastructure decision. Do not move
JSM back to `dwcourtney` merely to align repository ownership with the personal commit
identity. Do not change JSM authorship or signing to a Penn State identity merely
because `dwc5703` owns the repository.

Before any GitHub administration or manual deployment dispatch from Windows, run:

```powershell
pwsh -NoLogo -NoProfile -File scripts/verify-github-account.ps1
```

The command must report exactly `dwc5703`. If it fails or names another account,
STOP. Remote identity validation is deliberately separate in
`scripts/verify-repository-identity.sh`, so normal compilation never requires GitHub
CLI or network access.

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

The repository-scoped runner is installed at `/home/codex/jsm-cicd/runner` with
runner name `curiosity-jsm` and labels `self-hosted`, `linux`, `x64`, `curiosity`, and
`jsm`. The bootstrap installation used runner 2.337.0 and GitHub's published Linux x64
SHA-256 digest. Its supported service is:

```text
actions.runner.dwc5703-JobSearchManager.curiosity-jsm.service
```

Read service state with `systemctl status` or `systemctl is-active`. Deliberate service
maintenance uses the supported scripts from the runner directory:

```bash
cd /home/codex/jsm-cicd/runner
sudo ./svc.sh stop
sudo ./svc.sh start
```

Unregistration is a separate destructive approval boundary: stop and uninstall the
service with `svc.sh`, obtain a fresh repository-scoped removal token while the active
GitHub CLI account is exactly `dwc5703`, run `config.sh remove`, and only then remove
the runner tree. Never print the removal token. The runner path is outside
`/home/codex/jsm-lab`; unregistering or deleting runner infrastructure must not remove
JSM data, Data Protection keys, backups, Mailpit state, or unrelated Docker resources.

```bash
cd /home/codex/jsm-cicd/runner
sudo ./svc.sh stop
sudo ./svc.sh uninstall
./config.sh remove --token '<fresh-repository-removal-token>'
```

`.github/workflows/runner-smoke.yaml` is a manually dispatched, non-deploying check.
It validates the exact workflow SHA, canonical repository identity, hostname, service
user, Git, Docker, Compose, and read-only container inventory on the labeled runner.

## Hosted CI

`.github/workflows/ci.yaml` runs for pull requests targeting `main` and every pushed
branch. It checks out the exact event SHA with persisted Git credentials disabled.
For trusted push events, it uses only the built-in read-only `GITHUB_TOKEN` to require
GitHub's commit API to report `verified: true` and `reason: valid` for that exact SHA.
The step is skipped for pull requests, including forks, and uses no repository secret.
The `main` ruleset is the server-side signing guarantee; this CI check is an additional
lightweight exact-SHA signal. `scripts/ci-validate.sh` enforces:

1. canonical repository/remote identity and deterministic negative guard tests;
2. checked-out SHA equality;
3. locked NuGet restore under SDK 10.0.400;
4. Release build and the complete deterministic .NET suite;
5. JavaScript runtime tests and centralized theme/source checks;
6. `git diff --check` and a clean generated-file check;
7. independent Trivy source dependency, secret, and Dockerfile configuration scans;
8. independent Semgrep Community C# static application-security analysis;
9. a linux/amd64 image tagged by full SHA;
10. an OCI revision label equal to that SHA;
11. an independent Trivy scan of the exact candidate image archive;
12. an ephemeral non-root, read-only container with temporary isolated storage;
13. Docker health plus HTTP 200 and `Healthy` from `/healthz`; and
14. an exact SHA match from `/version`, whose response is limited to `commit`,
    `version`, and `hostingMode`.

The workflow has read-only repository permissions and pins third-party Actions by
immutable commit. CI is independent of curiosity and completes normally while the
lab machine is offline.

## Independent security scanning

Trivy 0.74.0 runs as a short-lived container pinned to the immutable linux/amd64
digest `sha256:ee940acbf1f58ebadb42d01434ce4609530bf1b52536afbd1eee66cd7123c5c9`.
There is no host Trivy installation, daemon, GitHub token, Azure credential, or
Cloudflare credential. The scanner container is non-root, read-only, has all Linux
capabilities dropped, has telemetry disabled, and never receives the Docker socket.
Images are exported to a temporary archive with `docker save`, scanned through
Trivy's `--input` path, and deleted by an exit trap.

Hosted CI scans the checked-out source before building and scans the exact
`jsm-ci:<full-sha>` candidate image before executing it. The curiosity deployment
then scans the independently built exact `jsm:<full-sha>` image before changing the
active manifest or running JSM container. A failed deployment scan therefore leaves
the running release untouched. Scanner input is limited to the checkout or temporary
image archive; JSM workspace data, account state, Data Protection keys, Mailpit, and
unrelated Docker resources such as ai801 are not mounted or inspected.

The initial enforcement policy is deliberately actionable:

- fixed High or Critical dependency/image vulnerabilities fail the job;
- any detected secret fails the job;
- High or Critical source configuration findings fail the job; and
- Unknown, Low, and Medium findings are reported but do not block.

Vulnerability reports and gates use `--ignore-unfixed`, so upstream findings with no
available remediation do not create an unresolvable release failure. There are no
ignore-file suppressions. A runtime-only synthetic secret and intentionally unsafe
temporary Dockerfile prove in every hosted CI run that the secret and configuration
gates fail closed; neither fixture is tracked or included in the application image.

Hosted CI uses one job-local temporary cache, which prevents cross-run cache poisoning
and is removed after validation. Curiosity uses
`/home/codex/jsm-cicd/trivy-cache`, outside JSM persistent storage, to retain only the
scanner vulnerability/check databases between serialized deployments. Trivy performs
its normal database freshness check on each run. To update Trivy, change both its
explicit version and verified linux/amd64 digest in `scripts/security-scan.sh`, rerun
the policy self-test and full CI, and review release notes before promoting the change.

Results remain in the normal CI/deployment logs. SARIF upload is not enabled because
it would require broader `security-events` token permission, and the exact deployed
artifact is currently built locally rather than promoted from hosted CI. For the same
reason, an SBOM of the hosted candidate would not be authoritative for the curiosity
artifact. License scanning is also deferred to avoid conflating legal inventory with
the vulnerability release gate. Trivy is not .NET-aware SAST; Semgrep provides the
separate application-source layer described below.

## Static application-security analysis

Semgrep Community Edition 1.175.0 analyzes application C# only on the GitHub-hosted
runner. Its official linux/amd64 container is pinned to
`sha256:1623685c0f6388b0bc8d577a712bf92b88252aaa09d6d7e38943dafa10ed978c`.
The official Community rules are a public Git submodule pinned to commit
`40b8c63f75dc7c22c8a77482d73bfb864b146f7e`; CI initializes that exact gitlink only
after Trivy has scanned the application checkout. The rules submodule is excluded
from the JSM Docker build context.

The scanner container runs non-root, read-only, without capabilities or a Docker
socket, and with network access disabled. Metrics and version checks are disabled,
the open-source engine is forced, and no Semgrep account or token exists. Image and
rule acquisition contact only Docker Hub and the public GitHub rules repository before
the scan; private JSM source and findings remain on the GitHub-hosted runner. The
workflow retains only `contents: read` permission and does not use GitHub's unavailable
private-repository code-scanning/SARIF service.

The scan loads the C# .NET, language-security, and Razor-security directories. The
upstream rules plus one local replacement are validated and 44 apply to the current
C# files. Coverage includes common
injection, command execution, path handling, deserialization, SSRF, XXE,
authentication-token, cryptography, TLS, and response-handling patterns. `bin`, `obj`,
tests, third-party rules, JavaScript, and generated output are excluded. JavaScript is
not included initially because the browser pack produced duplicate false positives for
the explicitly DOMPurify-sanitized description assignment already protected by focused
runtime/source-order tests.

Semgrep writes readable findings to the CI log and a temporary JSON report consumed by
the local policy evaluator. The JSON is deleted at job exit and is not uploaded.
Scanner or rule errors, partial parsing, a missing/wrong rules commit, High-confidence
findings, and Medium-confidence `ERROR` findings fail CI. Other findings remain
advisory in the log. The upstream fully qualified `xpath-injection` audit rule is the sole narrow rule
exclusion: it exhausts the Community engine's intrafile fixpoint on unrelated JSM
methods. `security/semgrep-jsm-rules/csharp-xpath-injection.yaml` replaces it with
deterministic direct concatenation/interpolation coverage. Analysis timeouts and skipped
rules fail CI, so this exception cannot silently reduce the rest of the scan. No finding
suppression or broad ignore file exists. Runtime-only safe and
weak-RNG C# fixtures prove both pass and block paths; an invalid runtime-only rule proves
scanner failure is fail-closed. These fixtures never enter the repository or image.

Developers should fix a blocking finding and add focused regression coverage. A false
positive should be demonstrated with evidence before considering a narrowly scoped,
documented rule exclusion; inline or broad suppressions are not the default. To update
Semgrep, verify a new official release and linux/amd64 digest, inspect release notes,
advance the rules gitlink deliberately, review changed C# rules and licenses, and run
the policy self-test, initial full scan, and complete CI before promotion.

Community Edition performs meaningful intrafile syntax and taint analysis but does not
provide the paid Pro engine's cross-file/interprocedural analysis. GitHub CodeQL remains
unavailable for this private personal repository without paid Code Security and
organizational restructuring.

## Deployment workflow

`.github/workflows/deploy-curiosity.yaml` is separate from CI. Automatic deployment
is disabled unless the repository variable `CURIOSITY_AUTO_DEPLOY` is explicitly set
to `true` after bootstrap validation. A successful trusted `main` push is the only
automatic source.

Automatic curiosity deployment was enabled after the repository-scoped runner,
manual smoke test, bootstrap deployment, and persistent-state checks all passed.

Manual dispatch accepts either current `main` or one lowercase full Git SHA. A
specific SHA must resolve to a commit in current `main` history and must pass every
hosted validation gate again. There is no workflow input that reaches a shell as an
arbitrary command.

Before a manual deployment, verify the active GitHub account and dispatch an exact
commit rather than a moving branch target:

```powershell
pwsh -NoLogo -NoProfile -File scripts/verify-github-account.ps1
gh workflow run deploy-curiosity.yaml --repo dwc5703/JobSearchManager --ref main `
  -f target=commit -f commit_sha=<lowercase-full-main-sha>
```

`CURIOSITY_AUTO_DEPLOY` is set to `true` following separate approval and successful
bootstrap validation.

The deploy job runs only on `[self-hosted, linux, x64, curiosity, jsm]`. It checks out
the exact SHA, builds `jsm:<full-sha>`, confirms the OCI revision, scans that exact
local image, and invokes
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

Curiosity explicitly enables the physical-possession first-administrator bootstrap
with `/app/data/admin-bootstrap-code`, backed by
`/home/codex/jsm-lab/data/app/admin-bootstrap-code` on the host. When an authenticated
account exists and the registry contains no Admin role, JSM creates a mode-0600 file
containing an eight-character one-time code and its fifteen-minute expiry. Retrieve
only the first line from an interactive curiosity session when a user is ready to
claim the role:

```bash
sed -n '1p' /home/codex/jsm-lab/data/app/admin-bootstrap-code
```

Do not copy the code into automation logs. A successful claim deletes the file, and
JSM does not generate another while any Admin account exists. Deployments without the
explicit bootstrap-path setting remain disabled; Azure rejects this server-file
mechanism.

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
3. Create or confirm private `dwc5703/JobSearchManager`.
4. Add `origin`, push complete `main` history, and push
   `pre-authentication-2026-08-28`.
5. Confirm hosted CI passes.
6. Configure the `main` ruleset: block force pushes and deletion, require signed
   commits and the CI validation check, keep direct pushes, and do not require pull
   requests or configure a bypass.
7. Register the repository-scoped runner in `/home/codex/jsm-cicd/runner`; install and
   verify its supported service with explicit approval.
8. Leave `CURIOSITY_AUTO_DEPLOY` unset and manually dispatch one bootstrap deployment.
9. Verify exact SHA identity, JSM health, account/workspace persistence, Mailpit state,
   and that `ai801` is unchanged.
10. Only after that verification, explicitly set `CURIOSITY_AUTO_DEPLOY=true`.

## Normal development flow

Develop and test on Windows, commit normally, and push the commit to a topic branch.
Repository-local configuration makes signing mandatory, so an unavailable signer
causes commit creation to fail rather than producing an unsigned commit. Hosted CI
performs every gate for that exact SHA and independently requires GitHub's `Verified`
status on pushes. After it succeeds, either open an
optional pull request or fast-forward `main` directly to the same already-validated
SHA. The required status and signed-commit rules therefore protect `main` without
imposing mandatory pull requests; an unsigned or unvalidated direct push is rejected.
A successful trusted `main` CI
run queues the exact SHA for curiosity only after automatic deployment is explicitly
enabled.

A powered-off curiosity does not affect CI; use manual dispatch later if the 24-hour
deployment queue expires. Roll back by manually dispatching a retained known-good SHA
from `main` history, which is revalidated before deployment.

Never commit environment files, credentials, account stores, runtime data, Data
Protection keys, Mailpit state, bundles, runner configuration, or deployment state.
Never place Azure or Cloudflare credentials in GitHub Actions; this deployment does
not use or modify Azure.

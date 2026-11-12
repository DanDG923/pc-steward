# GitHub rulesets for release branches and tags

These settings close finding S6 of the [ADR-032 self-updater security review](../review/ADR_032_SELF_UPDATER_SECURITY_REVIEW.md): the release-branch guard in `release-payload.yml` lives in the workflow it guards, so the branches and tags it trusts are protected by repository rulesets as well. They also narrow what the release-publishing token (fine-grained, this repository only, Contents read and write, Actions read) can do if it is ever stolen.

They are the owner's GitHub settings. No code reads them, and nothing in the repository changes when they are set.

## What they do

| Ruleset | Targets | Rules | Why |
|---|---|---|---|
| Release branches | branches `phase/4-beta`, `main` | Restrict deletions, require a pull request (0 approvals), require the four CI checks, block force pushes | Code reaches a release branch only through a pull request whose Windows CI is green. Nothing, the token included, can push straight to it, rewrite it or delete it |
| Release tags | tags `v*` | Restrict updates, restrict deletions, block force pushes | A release tag cannot be moved or deleted once it exists. Creation stays allowed because publishing a release creates its tag |
| No tags named like release branches | tags `phase/4-beta`, `main` | Restrict creations, restrict updates, restrict deletions | GitHub reports a tag's name as a run's `head_branch`, so a tag named like a release branch could look like a branch run (review finding R3). The payload workflow refuses such runs; this stops the tag existing at all |

None of the rulesets has a bypass list. Rulesets apply to repository admins unless they are on that list, and a token acts as the owner, so a bypass for the owner would be a bypass for the token too.

**Required checks.** The four jobs in `ci.yml` without `continue-on-error`: `build-and-test`, `owner-only-candidate-builder`, `msi-install-test` and `owner-only-signing-diagnostic`. The other four are diagnostics allowed to fail. If a required job is renamed in `ci.yml`, update the ruleset in the same change, or every merge waits for a check that never reports.

**Not required, on purpose.**

- **Approvals.** Pull requests here are opened and merged as the owner, and GitHub does not let an author approve their own pull request, so one required approval would block every merge.
- **Branch up to date before merging.** It adds an extra CI run for every merge and isn't needed for these checks.
- **Signed commits and linear history.** Neither is needed with pull requests required, and both could refuse merges the current process makes.

## What still works

- **Merging pull requests into `phase/4-beta`**, by the owner or by Claude acting for the owner, once the four checks have passed. A merge attempted while Windows CI is still queued is now refused by GitHub instead of going through.
- **`release-payload.yml` and `publish-release.yml`.** They only read the branch, and the draft release has no tag yet.
- **`Publish-PCStewardRelease.ps1`.** It uploads the MSI, deletes a stale MSI from the draft, and publishes, which creates the new `v<version>` tag. It never moves or deletes a tag.
- **Pushing other branches** (such as `claude/*` feature branches) and deleting them, as before.

## What the token can still do

The token can still create a new `v*` tag or release, push to any branch other than the release branches, and merge an open pull request whose checks are green, because GitHub counts merging as a Contents write. None of these lets it ship an update: PC Steward installs only an MSI signed with a compiled release key (the YubiKey needs its PIN and a touch) that matches a green CI build of the run named in the release (ADR-032 §3–§7, TM-19). The token expires (90 days) and can be revoked at any time.

## Steps (GitHub website)

Do this once, signed in as the repository owner. Each ruleset takes about two minutes.

### 1. Release branches

1. Open the repository on GitHub, then **Settings** (top bar) → **Rules** → **Rulesets** (left sidebar).
2. **New ruleset** → **New branch ruleset**.
3. **Ruleset name:** `Release branches`.
4. **Enforcement status:** `Active`.
5. **Bypass list:** leave empty.
6. **Target branches** → **Add target** → **Include by pattern** → type `phase/4-beta` → **Add inclusion pattern**. Repeat for `main`.
7. Under **Rules**, set:
   - **Restrict deletions**: on (it is on by default).
   - **Require a pull request before merging**: on. Leave **Required approvals** at `0` and the boxes under it off.
   - **Require status checks to pass**: on. Leave **Require branches to be up to date before merging** off. Click **Add checks** and add each of `build-and-test`, `owner-only-candidate-builder`, `msi-install-test` and `owner-only-signing-diagnostic`. If the search shows a source, pick **GitHub Actions**.
   - **Block force pushes**: on (it is on by default).
   - Leave every other rule off.
8. **Create**.

### 2. Release tags

1. **Rulesets** → **New ruleset** → **New tag ruleset**.
2. **Ruleset name:** `Release tags`. **Enforcement status:** `Active`. **Bypass list:** empty.
3. **Target tags** → **Add target** → **Include by pattern** → `v*` → **Add inclusion pattern**.
4. **Rules:** turn on **Restrict updates**, **Restrict deletions** and **Block force pushes**. Leave **Restrict creations** off, or publishing a release will fail.
5. **Create**.

### 3. No tags named like release branches

1. **Rulesets** → **New ruleset** → **New tag ruleset**.
2. **Ruleset name:** `No tags named like release branches`. **Enforcement status:** `Active`. **Bypass list:** empty.
3. **Target tags** → **Add target** → **Include by pattern** → `phase/4-beta`, then again for `main`.
4. **Rules:** turn on **Restrict creations**, **Restrict updates** and **Restrict deletions**.
5. **Create**.

### Check

**Settings** → **Rules** → **Rulesets** lists the three rulesets, each **Active**. The next pull request into `phase/4-beta` shows "Merging is blocked" until its four checks pass, then merges normally.

### If something needs an emergency change

Open the ruleset, set **Enforcement status** to `Disabled`, make the change, then set it back to `Active`. Only the owner can do this, and GitHub records it in the ruleset's history.

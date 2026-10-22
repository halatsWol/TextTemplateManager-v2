# Publishing & credential rotation

How a release is published, and how to renew the Partner Center credentials the Store submission uses.

---

## How a release is published

1. Update `release.md`: the "What's new in this release" section lists the changes since the last tag. It becomes the GitHub release text, and the app shows it once after the update. Update the short "What's new" text in [`store.md`](../store.md) too (at most 1500 characters); it is the Store's "What's new".
2. Commit and push, then check that **Tests** and **CodeQL** are green for that commit under **Actions**, and that **Security** (Code scanning, Dependabot) shows no open alert you haven't looked at. A merged Dependabot npm update reaches the app only after `npm ci` and `npm run build` in `src/editor` and a commit of the rebuilt bundle.
3. Create an **annotated** tag and push it:
   ```bash
   git tag -a v2.0.1 -m 'v2.0.1'
   git push origin v2.0.1
   ```
4. [`.github/workflows/release.yml`](../.github/workflows/release.yml) runs the tests, builds the x64 Store package (`.msixupload`) and both manuals, and creates the GitHub release as a **draft** with the manuals attached (no installers, never an `.exe`). Read the notes under **Releases**; nothing is public before the version is live in the Store (step 6), and you can still fix them until then.
5. The `microsoft-store` job then waits for the **`store-release`** environment. Approve it under **Actions ▸ the run ▸ Review deployments**; it checks the access to the app, uploads the package to a new Store submission, sets "What's new" for every listing language, and commits the submission (with `publish: draft` it stops before the commit, see below). Certification takes hours to days; the job doesn't wait for it.
6. The `publish-when-live` job then starts [`publish-when-live.yml`](../.github/workflows/publish-when-live.yml): it checks the Store's public catalog every 10 minutes and publishes the draft GitHub release as soon as that version (or a newer one) is live there. A job may run 6 hours at most, so each run waits about 5.5 hours and then starts the next one, for up to 7 days; after that the release stays a draft with a warning in the run's summary. With `publish: draft` the manual commit run starts it instead. To publish earlier, click **Publish release** under **Releases**; the watcher then stops by itself.

Rules:

- The tag is the only version source: `vX.Y.Z` becomes the app version and the package version `X.Y.Z.0`. Plain numbers only; the Store has no beta versions (use a package flight in Partner Center for that).
- Every release needs a **higher** version than the one live in the Store.
- The Store's "What's new" comes from `store.md` as it is; a text over 1500 characters fails the build instead of being cut.

### Store submission: store, draft or none

A line in the tag message decides what the `microsoft-store` job does; the draft GitHub release is created in every case.

| Line | Store submission |
| --- | --- |
| `publish: store` or no such line | Package uploaded, "What's new" set, submission committed (certification starts) |
| `publish: draft` | Package uploaded and "What's new" set, but not committed; commit it with the manual run below |
| `publish: none` | Skipped |

```bash
git tag -a v2.2.0 -m 'v2.2.0' -m 'publish: draft'
```

Use `publish: draft` after changing the `microsoft-store` job.

Partner Center shows a package uploaded through the API only after the commit: until then the draft there still lists the previous package, and **Submit** in Partner Center would send that one again. So don't submit an API draft in Partner Center. **Actions ▸ Release ▸ Run workflow** with **Check Store access** lists the pending submission's packages (the new one as `PendingUpload`, the old one as `PendingDelete`); **Commit the pending Store submission** commits it after the `store-release` approval, and certification starts.

### Publishing the GitHub release by hand

**Actions ▸ Publish release when live ▸ Run workflow** starts the watcher again, for example after it gave up or was cancelled: **tag** names the draft release (empty: the newest draft), **give_up_at** the end (empty: 7 days from now). It needs no secrets and no approval; `build/Publish-WhenLive.ps1 -Tag vX.Y.Z -DryRun` shows locally what it would do.

### Checking the Store access

**Actions ▸ Release ▸ Run workflow** with **Check Store access** ticked runs only `build/Store-Api.ps1 -Check`: it signs in with the secrets and reads the app in Partner Center, and prints the last published and the pending submission with its packages. Nothing is built or submitted, and no approval is needed. Run it after setting up or renewing the secrets.

The same check runs first in every Store submission. Its errors name the cause:

- **Sign-in to Microsoft Entra failed**: tenant ID, client ID or client secret wrong or expired.
- **HTTP 401**: the token isn't accepted for Partner Center (wrong tenant).
- **HTTP 403**: the Entra application signed in but may not use the app. In Partner Center ▸ Account settings ▸ User management ▸ Microsoft Entra applications, give it the **Manager** role, and if it has product-level permissions, include TextTemplateManager.
- **HTTP 404**: the app isn't in this Partner Center account (wrong account, or a typo in the product ID).

### Building without publishing

**Actions ▸ Release ▸ Run workflow** (`workflow_dispatch`) with a version builds everything and uploads it as run artifacts only. It never creates a release and never submits.

---

## First release (v2.0.0)

The Store API can only update an app that already has a published submission, so the first one goes in by hand:

1. Tag `v2.0.0` with `publish: none` (see above).
2. Download the `TextTemplateManager-2.0.0` artifact from the run (or build it locally from the same commit: `build/Build-Release.ps1 -Version 2.0.0`, from a short path; it is unsigned, the Store signs it) and upload its `.msixupload` in a new submission in Partner Center, together with the listing, screenshots, age rating and pricing. The texts to paste are in [`store.md`](../store.md); the screenshots (2560x1440, one colour scheme: `amber` or `slate`) and the 300x300 app tile are in [`design/store`](../design/store).
3. From then on, tags submit automatically.

---

## Secrets reference

Stored in **GitHub ▸ repo ▸ Settings ▸ Secrets and variables ▸ Actions**. The names are used verbatim in `release.yml`; if you rename one, update the workflow too. The Store product ID (`9NMBSZLLLN6V`) is public and lives in the workflow itself.

| Secret | Source | Expires? |
| --- | --- | --- |
| `MSSTORE_TENANT_ID` | Partner Center ▸ Account settings ▸ User management ▸ Microsoft Entra applications (Tenant ID) | No |
| `MSSTORE_CLIENT_ID` | Same page, the application's Client ID | No |
| `MSSTORE_CLIENT_SECRET` | Same page ▸ the application ▸ **Add new key** (shown once) | **Yes: 1 or 2 years** |
| `MSSTORE_SELLER_ID` | Partner Center ▸ Account settings ▸ Legal info ▸ Developer (Seller ID) | No |

A missing secret fails the `microsoft-store` job with its name; the draft GitHub release exists by then.

### One-time setup

1. Partner Center ▸ **Account settings ▸ User management ▸ Microsoft Entra applications ▸ Create Microsoft Entra application** (or add an existing one), role **Manager**.
2. Open the application, **Add new key**, and copy the client secret right away; it isn't shown again. Note the expiry date.
3. Add the four secrets above in GitHub.
4. GitHub ▸ **Settings ▸ Environments ▸ New environment** `store-release`, and add yourself as a **required reviewer**.

### Renewing the client secret

The client secret expires (1 or 2 years, chosen when it's created). Before it does:

1. Partner Center ▸ Account settings ▸ User management ▸ Microsoft Entra applications ▸ the application ▸ **Add new key**.
2. Update `MSSTORE_CLIENT_SECRET` in GitHub (the secret ▸ **Update**). Tenant, client and seller IDs stay the same.
3. Remove the old key in Partner Center once the next release has gone through.

If a submission failed because the secret had expired, renew it and re-run the failed `microsoft-store` job.

---

## If the Store job fails halfway

- **Before "Commit the submission"**: the package sits in a pending submission in Partner Center. Finish it there by hand (check "What's new", submit), or delete the pending submission there and re-run the job. Whether `msstore publish` reuses a pending submission left by a failed run hasn't been tried yet, so delete it first.
- **After it**: the submission is in certification; check its status in Partner Center. A commit the Store rejects at once fails the job with the Store's reasons.
- The package upload uses the Microsoft Store Developer CLI (`msstore`, installed by `microsoft/microsoft-store-apppublisher`), pinned to a version (`version:` in the `microsoft-store` job), with the `.msixupload` file itself passed to `msstore publish`. Before raising the pinned version, check that command against its release notes. The access check, "What's new" and the commit go straight to the Partner Center submission API (`build/Store-Api.ps1`), which doesn't depend on the CLI's output format.

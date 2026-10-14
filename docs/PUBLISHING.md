# Publishing & credential rotation

How a release is published, and how to renew the Partner Center credentials the Store submission uses.

---

## How a release is published

1. Update `release.md`: the "What's new in this release" section lists the changes since the last tag. It becomes the GitHub release text and the Store's "What's new", and the app shows it once after the update.
2. Commit, then create an **annotated** tag and push it:
   ```bash
   git tag -a v2.0.1 -m 'v2.0.1'
   git push origin v2.0.1
   ```
3. [`.github/workflows/release.yml`](../.github/workflows/release.yml) runs the tests, builds the x64 Store package (`.msixupload`) and both manuals, and publishes the GitHub release with the manuals attached (no installers, never an `.exe`).
4. The `microsoft-store` job then waits for the **`store-release`** environment. Approve it under **Actions ▸ the run ▸ Review deployments**; it uploads the package to a new Store submission, sets "What's new" for every listing language, and commits the submission. Certification takes hours to days; the job doesn't wait for it.

Rules:

- The tag is the only version source: `vX.Y.Z` becomes the app version and the package version `X.Y.Z.0`. Plain numbers only; the Store has no beta versions (use a package flight in Partner Center for that).
- Every release needs a **higher** version than the one live in the Store.
- The Store's "What's new" is cut to 1500 characters, with a link to the GitHub release when it's longer.

### Skipping the Store submission

A line `publish: none` in the tag message skips the `microsoft-store` job; the GitHub release is still published. `publish: store` or no such line submits.

```bash
git tag -a v2.0.0 -m 'v2.0.0' -m 'publish: none'
```

### Building without publishing

**Actions ▸ Release ▸ Run workflow** (`workflow_dispatch`) with a version builds everything and uploads it as run artifacts only. It never creates a release and never submits.

---

## First release (v2.0.0)

The Store API can only update an app that already has a published submission, so the first one goes in by hand:

1. Tag `v2.0.0` with `publish: none` (see above).
2. Download the `TextTemplateManager-2.0.0` artifact from the run and upload its `.msixupload` in a new submission in Partner Center, together with the listing, screenshots, age rating and pricing.
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

A missing secret fails the `microsoft-store` job with its name; the GitHub release is already published by then.

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

- **Before "Commit the submission"**: the package sits in a draft submission in Partner Center. Re-running the job replaces the draft. Or finish it by hand in Partner Center.
- **After it**: the submission is in certification; check its status in Partner Center.
- The workflow uses the Microsoft Store Developer CLI (`msstore`, installed by `microsoft/microsoft-store-apppublisher`). If its commands or the submission format change, the fix is in the `microsoft-store` job of `release.yml`.

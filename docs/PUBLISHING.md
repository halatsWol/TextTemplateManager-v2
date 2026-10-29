# Publishing & credential rotation

How a release is published, and how to renew the Partner Center credentials the Store submission uses.

---

## How a release is published

1. Create the release's folder `releases/vX.Y.Z/` with a folder per UI language (`en-US`, `de-DE`, …), starting from a copy of the previous release's. In each, `release.md` has the "What's new in this release" section with the changes since the last tag; the app shows the one of its language once after the update (English if that language has none), and the English one is the GitHub release text. `store.md` holds that language's Store listing: name, descriptions, features, search terms, "What's new" (at most 1500 characters), the screenshot captions and the texts in the screenshot images; `screenshots/` its Store screenshots (see *Store screenshots* below; a release without new ones reuses the newest earlier release's). The submission settings that don't depend on the language are in [`store.md`](../store.md) at the root.
2. Commit and push, then check that **Tests** and **CodeQL** are green for that commit under **Actions**, and that **Security** (Code scanning, Dependabot) shows no open alert you haven't looked at. A merged Dependabot npm update reaches the app only after `npm ci` and `npm run build` in `src/editor` and a commit of the rebuilt bundle.
3. Create an **annotated** tag and push it:
   ```bash
   git tag -a v2.0.1 -m 'v2.0.1'
   git push origin v2.0.1
   ```
4. [`.github/workflows/release.yml`](../.github/workflows/release.yml) runs the tests, builds the x64 Store package (`.msixupload`) and both manuals, writes the Store listing of every language (`build/Release-Notes.ps1 -ListingOutDir`: one file per Partner Center field plus the screenshots, in the build artifact's `store-listing/<language>/`; Partner Center's limits are checked) and creates the GitHub release as a **draft** with the manuals attached (no installers, never an `.exe`). Read the notes under **Releases**; nothing is public before the version is live in the Store (step 6), and you can still fix them until then.
5. The `microsoft-store` job then waits for the **`store-release`** environment. Approve it under **Actions ▸ the run ▸ Review deployments**; it checks the access to the app and makes a new Store submission (`build/Store-Api.ps1 -Submit`): the new package replaces the old one, and every listing gets its language's texts, "What's new" and screenshots (a regional listing such as `es-mx` gets `es-ES`; a listing without a language folder only the English "What's new"). A language folder no listing uses yet is added as a new listing when it has screenshots; if Partner Center refuses that, the job warns and the language is added there by hand once. Then it commits the submission (with `publish: draft` it stops before the commit, see below). Certification takes hours to days; the job doesn't wait for it.
6. The `publish-when-live` job then starts [`publish-when-live.yml`](../.github/workflows/publish-when-live.yml): it checks the Store's public catalog every 10 minutes and publishes the draft GitHub release as soon as that version (or a newer one) is live there. A job may run 6 hours at most, so each run waits about 5.5 hours and then starts the next one, for up to 7 days; after that the release stays a draft with a warning in the run's summary. With `publish: draft` the manual commit run starts it instead. To publish earlier, click **Publish release** under **Releases**; the watcher then stops by itself.

Rules:

- The tag is the only version source: `vX.Y.Z` becomes the app version and the package version `X.Y.Z.0`. Plain numbers only; the Store has no beta versions (use a package flight in Partner Center for that).
- Every release needs a **higher** version than the one live in the Store.
- The Store texts come from `releases/vX.Y.Z/<language>/store.md` as they are; a text over Partner Center's limits fails the build instead of being cut. A Store build without `releases/vX.Y.Z/en-US/release.md` fails.

### Store submission: store, draft or none

A line in the tag message decides what the `microsoft-store` job does; the draft GitHub release is created in every case.

| Line | Store submission |
| --- | --- |
| `publish: store` or no such line | Submission made (package, listings, screenshots) and committed (certification starts) |
| `publish: draft` | Submission made but not committed; check it, then commit it with the manual run below |
| `publish: none` | Skipped; submit by hand from the build artifact (see *Submitting by hand*) |

```bash
git tag -a v2.2.0 -m 'v2.2.0' -m 'publish: draft'
```

Use `publish: draft` after changing the `microsoft-store` job.

Partner Center may show a package uploaded through the API only after the commit: until then the draft there can still list the previous package, and **Submit** in Partner Center would send that one again. So don't submit an API draft in Partner Center. **Actions ▸ Release ▸ Run workflow** with **Check Store access** lists the pending submission's packages (the new one as `PendingUpload`, the old one as `PendingDelete`) and the screenshots per listing; **Commit the pending Store submission** commits it after the `store-release` approval, and certification starts.

### Submitting by hand

With `publish: none`, the run's build artifact `TextTemplateManager-X.Y.Z` holds everything for Partner Center: the `.msixupload`, and in `store-listing/<language>/` one text file per field (`title`, `short-description`, `description`, `features` one per line, `search-terms` one per line, `whats-new`, `copyright`, `screenshot-captions` in the order of the screenshots) and `screenshots/`. In the submission, upload the package (remove the old one), and per listing language paste the files and upload the screenshots with their captions. A language without a listing yet: **Store listings ▸ Manage additional languages**. Submission options and properties: the root [`store.md`](../store.md). After submitting, start **Actions ▸ Publish release when live ▸ Run workflow** with the tag.

### Store screenshots

Each language's screenshots are made from window captures of the app in that language, with sample templates in that language:

1. The sample data per language is in [`design/store/samples/<language>.json`](../design/store/samples) (templates, team file, the text in Notepad, the search term, which template each screenshot shows, the translation target). A new UI language needs one.
2. The captures are made on a test VM at 125 % on a 2560x1440 display and go to `design/store/captures/<language>/` (nine per language: `store-main`, `store-qp-single`, `store-qp-multi`, `store-pastemode`, `store-search`, `store-sync`, `store-backups`, `store-tour`, `store-translate`). The VM scripts aren't part of the repository.
3. `pwsh -File design/store/Make-StoreScreenshots.ps1 -Version X.Y.Z` puts them into the Photoshop templates (`design/store/templates`, through `Make-StoreImages.jsx` and Photoshop's COM interface) with the texts of each language's "Screenshot images" block, and writes `releases/vX.Y.Z/<language>/screenshots/` (2560x1440, colour scheme `amber` as in the Store, `-Scheme slate` for the other). Every language folder with a `store.md` is picked up; captions that are too wide are set smaller.
4. Commit the screenshots with the release. A release without new screenshots needs none: the listing takes the newest earlier release's.

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

## Documentation website

The manuals, a help page and the privacy policy are published at <https://software.kmarflow.com/TextTemplateManager-v2/> by [`.github/workflows/pages.yml`](../.github/workflows/pages.yml), on every push to `main` that changes them (merging `develop` before a tag does that), or by hand under **Actions ▸ Website ▸ Run workflow**. Nothing else is needed for a release.

- **Sources:** the site's own pages are in `website/pages` (home, help & troubleshooting), its settings in `website/mkdocs.yml`. The manuals (`docs/user`), `PRIVACY.md` and the manual's screenshots are taken as they are, so the site, the PDFs and the in-app manual can't drift apart.
- **Local build:** `pip install "mkdocs==1.6.1" "mkdocs-material==9.7.7"`, then `pwsh -File build/Build-Website.ps1`; the site is in `out/website/site`. The build fails on a broken page or section link.
- **Domain:** the account's Pages site (repository `halatsWol.github.io`) has the custom domain `software.kmarflow.com` (DNS: `CNAME software → halatswol.github.io`). Every project site of the account appears under it at `/<repository>/`, so this repository has no domain of its own. **Settings ▸ Pages** of this repository: source **GitHub Actions**.

## First release (v2.0.0)

The Store API can only update an app that already has a published submission, so the first one goes in by hand:

1. Tag `v2.0.0` with `publish: none` (see above).
2. Download the `TextTemplateManager-2.0.0` artifact from the run (or build it locally from the same commit: `build/Build-Release.ps1 -Version 2.0.0`, from a short path; it is unsigned, the Store signs it) and upload its `.msixupload` in a new submission in Partner Center, together with the listing, screenshots, age rating and pricing (see *Submitting by hand*); the 300x300 app tile and the art without text are in [`design/store`](../design/store).
3. From then on, tags submit automatically.

---

## Secrets reference

Stored in **GitHub ▸ repo ▸ Settings ▸ Secrets and variables ▸ Actions**. The names are used verbatim in `release.yml`; if you rename one, update the workflow too. The Store product ID (`9NMBSZLLLN6V`) is public and lives in the workflow itself.

| Secret | Source | Expires? |
| --- | --- | --- |
| `MSSTORE_TENANT_ID` | Partner Center ▸ Account settings ▸ User management ▸ Microsoft Entra applications (Tenant ID) | No |
| `MSSTORE_CLIENT_ID` | Same page, the application's Client ID | No |
| `MSSTORE_CLIENT_SECRET` | Same page ▸ the application ▸ **Add new key** (shown once) | **Yes: 1 or 2 years** |
| `MSSTORE_SELLER_ID` | Not used any more (it was for the Store Developer CLI); can be deleted | No |

A missing secret fails the `microsoft-store` job with its name; the draft GitHub release exists by then.

### One-time setup

1. Partner Center ▸ **Account settings ▸ User management ▸ Microsoft Entra applications ▸ Create Microsoft Entra application** (or add an existing one), role **Manager**.
2. Open the application, **Add new key**, and copy the client secret right away; it isn't shown again. Note the expiry date.
3. Add the three secrets above in GitHub.
4. GitHub ▸ **Settings ▸ Environments ▸ New environment** `store-release`, and add yourself as a **required reviewer**.

### Renewing the client secret

The client secret expires (1 or 2 years, chosen when it's created). Before it does:

1. Partner Center ▸ Account settings ▸ User management ▸ Microsoft Entra applications ▸ the application ▸ **Add new key**.
2. Update `MSSTORE_CLIENT_SECRET` in GitHub (the secret ▸ **Update**). Tenant and client IDs stay the same.
3. Remove the old key in Partner Center once the next release has gone through.

If a submission failed because the secret had expired, renew it and re-run the failed `microsoft-store` job.

---

## If the Store job fails halfway

- **Before "Commit the submission"**: the submission is pending in Partner Center. Delete it there and re-run the job (a new submission isn't made while one is pending), or finish it by hand.
- **After it**: the submission is in certification; check its status in Partner Center. A commit the Store rejects at once fails the job with the Store's reasons.
- Everything goes through the Partner Center submission API (`build/Store-Api.ps1`, no other tool): a new submission, its data (packages, listings, images) with `PUT`, the ZIP with the package and screenshots to the submission's upload URL in 4 MB blocks, then the commit. `pwsh -File build/Store-Api.ps1 -AppId 9NMBSZLLLN6V -ListingDir <store-listing> -Preview en-us,de-de` shows locally, without signing in, what a submission would do with those listing languages.

# GitHub setup

The repository is already initialized locally with a `main` branch and an initial v10 alpha tag in the downloadable Git bundle.

## Option A — GitHub CLI

Install and authenticate GitHub CLI:

```bash
gh auth login
```

Then from the repository root:

```bash
gh repo create auto-emulator-update --public --source=. --remote=origin --push
git push origin --tags
```

Or run the provided setup script.

## Option B — GitHub website

1. Create an empty repository named `auto-emulator-update`.
2. Do **not** add a README/license because they already exist here.
3. Add the remote and push:

```bash
git remote add origin https://github.com/YOUR_USERNAME/auto-emulator-update.git
git push -u origin main
git push origin --tags
```

## Recommended repository settings

- Enable Issues and Discussions.
- Require pull requests for `main`.
- Require `CI / build-and-test`.
- Enable Dependabot.
- Enable secret scanning.
- Add `GITHUB_TOKEN` only through GitHub Actions' built-in token; do not commit credentials.

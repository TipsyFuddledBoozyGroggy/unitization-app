# GitHub Actions workflows

This directory contains the CI/CD workflows for the
Rehearsal_Forecast_Application. In this phase the repository intentionally
ships **validation only** — no deployment, no image push, no cloud
authentication.

## Current workflows

### `ci.yml` — continuous validation

Triggers on `pull_request` and `push`. It runs two independent jobs:

- **`dotnet-build-test`** — restores, builds, tests, publishes the web app,
  and builds the container image (no push). Uploads the published output
  and the test results (`test-results.trx`) as artifacts.
- **`terraform-validate`** — runs `terraform fmt -check -recursive`,
  `terraform init -backend=false`, and `terraform validate` against
  `infrastructure/terraform/environments/dev`.

Neither job requires cloud credentials, and neither job performs any
outbound authenticated action. See Requirement 24 and design §17 in
`.kiro/specs/rehearsal-forecast/` for the full contract.

## Future workflow (not implemented in this phase)

A future `deploy.yml` will live in this same directory
(`.github/workflows/deploy.yml`) and will be responsible for:

- Authenticating to Google Cloud
- Pushing the container image to Google Artifact Registry
- Deploying the image to Cloud Run
- Running any post-deploy smoke checks

`deploy.yml` will be additive: adding it MUST NOT require any change to
`ci.yml` (Requirement 24.8). CI validation and deployment stay in
separate workflow files with separate triggers.

### Intended future authentication mechanism

Deployment will authenticate to Google Cloud using **GitHub workload
identity federation** — no long-lived service account JSON keys will be
stored as GitHub secrets. Concretely, the future `deploy.yml` will use
[`google-github-actions/auth@v2`](https://github.com/google-github-actions/auth)
in workload-identity mode, roughly like this (illustrative only, not
active in this phase):

```yaml
# Illustrative only — not part of ci.yml.
# Belongs in a future .github/workflows/deploy.yml.
- name: Authenticate to Google Cloud
  uses: google-github-actions/auth@v2
  with:
    workload_identity_provider: projects/PROJECT_NUMBER/locations/global/workloadIdentityPools/POOL/providers/PROVIDER
    service_account: deployer@PROJECT_ID.iam.gserviceaccount.com
```

The Google Cloud side of the federation (workload identity pool,
provider, and service-account IAM bindings) is intentionally out of
scope for this phase and will be added alongside `deploy.yml`.

## What CI must never do (this phase)

The following are explicitly out of scope for `ci.yml` and MUST remain
so until a `deploy.yml` is introduced:

- `docker push` to any registry
- `terraform apply`
- `gcloud auth` or any other cloud authentication
- Workload identity federation invocation
- Any Cloud Run deployment step

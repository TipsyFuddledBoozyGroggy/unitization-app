# dev Terraform environment

Scaffolding for a future Cloud Run deployment of the Rehearsal Forecast
web application. This environment consumes `../../modules/cloud_run`
and is expected to pass `terraform fmt -check`,
`terraform init -backend=false`, and `terraform validate` without
provisioning any resource (Requirement 23.1, 23.9, 23.10; design §16).

## Layout

- `versions.tf` — pins Terraform (`>= 1.7.0`) and the `hashicorp/google`
  provider (`~> 5.0`). Matches the module's pins so the environment and
  the module resolve the same provider (design §16.5).
- `providers.tf` — configures the `google` provider with `project` and
  `region` from variables (Requirement 23.2).
- `variables.tf` — mirrors the module's input variables so values flow
  through from `terraform.tfvars` to the module without transformation.
- `main.tf` — instantiates `module "cloud_run"` from
  `../../modules/cloud_run` and passes every variable through.
- `outputs.tf` — re-exports `service_name` and `service_url` from the
  module (design §16.3).
- `terraform.tfvars.example` — placeholder values for local validation.
  Real values live in a developer-local, gitignored `terraform.tfvars`
  (Requirement 23.8; design §16.8).

## Local validation sequence

Run these commands from this directory:

```
cd infrastructure/terraform/environments/dev
terraform fmt -check
terraform init -backend=false
terraform validate
```

All three must pass. `terraform init -backend=false` skips backend
initialisation so validation works without any remote-state bucket
(design §16.9).

To try validation with your own values:

```
cp terraform.tfvars.example terraform.tfvars
# edit terraform.tfvars — it is gitignored
terraform validate
```

## Remote-state guidance

The Terraform in this repo runs with the **default local backend** in
this phase. When you are ready to adopt remote state, the recommended
setup is a Google Cloud Storage backend accessed via short-lived
impersonation (Requirement 23.6; design §16.7):

1. Create the state bucket **out of band** (for example, via
   `gcloud storage buckets create`). This Terraform intentionally does
   not provision the bucket, so state and the resources it manages
   cannot circularly depend on each other.
2. Enable versioning and uniform bucket-level access on the bucket.
3. Grant the developer or automation identity `roles/storage.objectAdmin`
   on the bucket, ideally via a service account that developers can
   impersonate rather than long-lived keys.
4. Uncomment a `backend "gcs"` block (below) in this directory, filling
   in the bucket name and an optional prefix, and re-run
   `terraform init` (without `-backend=false`) to migrate state.

```hcl
# terraform {
#   backend "gcs" {
#     bucket                      = "YOUR_TFSTATE_BUCKET"
#     prefix                      = "rehearsal-forecast/dev"
#     impersonate_service_account = "terraform-dev@YOUR_PROJECT_ID.iam.gserviceaccount.com"
#   }
# }
```

Until that migration happens, keep using `terraform init -backend=false`
for local validation.

## No `terraform apply`

`terraform apply` is **not** invoked from any script or CI workflow in
this phase (Requirement 23.10; design §16.9). This environment is
validation-only. Deployment to Cloud Run is out of scope until a
future phase introduces a dedicated deploy workflow.

Do not add `apply` to Makefiles, npm scripts, GitHub Actions, or any
other automation in this repository.

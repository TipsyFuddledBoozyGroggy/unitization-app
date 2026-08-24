# cloud_run Terraform module

Scaffolding for a future Google Cloud Run deployment of the
Rehearsal Forecast web application. The module is expected to pass
`terraform fmt -check`, `terraform init -backend=false`, and
`terraform validate`, but it is not invoked from `terraform apply`
in this phase (Requirement 23.9, 23.10; design §16).

## What it defines

- `google_cloud_run_v2_service` with a variable-driven container image,
  runtime service account, environment variables, and merged labels.
- Conditional `google_cloud_run_v2_service_iam_member` that grants
  `roles/run.invoker` to `allUsers` only when
  `allow_public_access = true`.

## Inputs

See `variables.tf` for the full list. Notable inputs:

| Variable | Type | Purpose |
| --- | --- | --- |
| `project_id` | string | GCP project that will host the service. |
| `region` | string | Cloud Run region. |
| `service_name` | string | Cloud Run service name. |
| `container_image` | string | Fully qualified image reference. |
| `service_account_email` | string | Runtime identity for the service. |
| `allow_public_access` | bool | Toggles the `allUsers` invoker binding. |
| `env_vars` | map(string) | Non-secret environment variables. |
| `labels` | map(string) | Extra labels merged with `environment` + `service`. |
| `environment` | string | Environment identifier (for example, `dev`). |

## Outputs

- `service_name` — the Cloud Run service name.
- `service_url` — the fully qualified service URL.

## Usage

Consumed from `infrastructure/terraform/environments/dev`:

```hcl
module "cloud_run" {
  source = "../../modules/cloud_run"

  project_id            = var.project_id
  region                = var.region
  service_name          = var.service_name
  container_image       = var.container_image
  service_account_email = var.service_account_email
  allow_public_access   = var.allow_public_access
  env_vars              = var.env_vars
  labels                = var.labels
  environment           = var.environment
}
```

## Constraints

- No project IDs, regions, or secrets are embedded in the module
  (Requirement 23.8).
- Provider pinned to `hashicorp/google ~> 5.0`; Terraform pinned to
  `>= 1.7.0` (design §16.5).
- The module does not configure a backend; validation runs with the
  default local backend via `terraform init -backend=false`.

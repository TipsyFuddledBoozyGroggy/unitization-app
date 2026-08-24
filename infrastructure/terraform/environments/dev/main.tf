# Root configuration for the dev environment.
#
# This environment is scaffolding only — it is expected to pass
# `terraform fmt -check`, `terraform init -backend=false`, and
# `terraform validate`, and it is NOT invoked from `terraform apply`
# in this phase (Requirement 23.9, 23.10; design §16.9).

module "cloud_run" {
  source = "../../modules/cloud_run"

  project_id            = var.project_id
  region                = var.region
  service_name          = var.service_name
  container_image       = var.container_image
  allow_public_access   = var.allow_public_access
  service_account_email = var.service_account_email
  env_vars              = var.env_vars
  labels                = var.labels
  environment           = var.environment
}

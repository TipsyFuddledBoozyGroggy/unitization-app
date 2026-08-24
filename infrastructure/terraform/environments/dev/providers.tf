# Google provider configuration for the dev environment.
# Project and region are variable-driven so no identifiers are embedded
# in the configuration (Requirement 23.2, 23.8).
provider "google" {
  project = var.project_id
  region  = var.region
}

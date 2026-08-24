# Terraform and provider version pins for the dev environment.
# Kept in sync with modules/cloud_run/versions.tf so the environment
# and the module resolve the same provider (design §16.5).
terraform {
  required_version = ">= 1.7.0"

  required_providers {
    google = {
      source  = "hashicorp/google"
      version = "~> 5.0"
    }
  }
}

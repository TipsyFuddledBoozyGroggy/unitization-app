# Terraform and provider version pins for the cloud_run module.
# Kept in sync with the environments/dev configuration so the module
# and its consumers resolve the same provider (design §16.5).
terraform {
  required_version = ">= 1.7.0"

  required_providers {
    google = {
      source  = "hashicorp/google"
      version = "~> 5.0"
    }
  }
}

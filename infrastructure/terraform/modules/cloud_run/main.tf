# Resources for the cloud_run module.
#
# This module is scaffolding only — it is expected to pass
# `terraform fmt -check`, `terraform init -backend=false`, and
# `terraform validate`, and it is NOT invoked from `terraform apply`
# in this phase (Requirement 23.9, 23.10).

# Merge environment/service identifiers with caller-supplied labels
# so every resource carries cost-attribution metadata (design §16.6).
locals {
  labels = merge(
    {
      environment = var.environment
      service     = var.service_name
    },
    var.labels,
  )
}

# Cloud Run v2 service. The runtime service account, container image,
# environment variables, and labels are all variable-driven.
resource "google_cloud_run_v2_service" "service" {
  project  = var.project_id
  name     = var.service_name
  location = var.region
  labels   = local.labels

  template {
    service_account = var.service_account_email
    labels          = local.labels

    containers {
      image = var.container_image

      dynamic "env" {
        for_each = var.env_vars
        content {
          name  = env.key
          value = env.value
        }
      }
    }
  }
}

# Optional public-access binding. Created only when allow_public_access is
# true; otherwise the service is private and no binding is emitted
# (design §16.4).
resource "google_cloud_run_v2_service_iam_member" "public_invoker" {
  count = var.allow_public_access ? 1 : 0

  project  = google_cloud_run_v2_service.service.project
  location = google_cloud_run_v2_service.service.location
  name     = google_cloud_run_v2_service.service.name
  role     = "roles/run.invoker"
  member   = "allUsers"
}

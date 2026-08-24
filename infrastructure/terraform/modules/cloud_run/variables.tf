# Input variables for the cloud_run module.
# Every value is variable-driven so no project IDs, regions, or secrets
# are embedded in the module itself (Requirement 23.2, 23.8).

# GCP project that will host the Cloud Run service.
variable "project_id" {
  description = "The Google Cloud project ID that will host the Cloud Run service."
  type        = string
}

# Cloud Run region (for example, us-central1).
variable "region" {
  description = "The Google Cloud region for the Cloud Run service (for example, us-central1)."
  type        = string
}

# Cloud Run service name.
variable "service_name" {
  description = "The name of the Cloud Run service."
  type        = string
}

# Fully qualified container image reference (registry/repo/image:tag).
variable "container_image" {
  description = "The fully qualified container image reference (registry/repo/image:tag) to deploy."
  type        = string
}

# Toggles the roles/run.invoker binding for allUsers (design §16.4).
variable "allow_public_access" {
  description = "When true, grants roles/run.invoker to allUsers, making the service publicly reachable. When false, the service remains private and no public binding is created."
  type        = bool
  default     = false
}

# Runtime identity for the Cloud Run service.
variable "service_account_email" {
  description = "The email of the service account used as the runtime identity of the Cloud Run service."
  type        = string
}

# Non-secret environment variables exposed to the container.
variable "env_vars" {
  description = "Map of non-secret environment variables exposed to the container. Secrets belong in Secret Manager and are intentionally out of scope for this scaffold (Requirement 23.8)."
  type        = map(string)
  default     = {}
}

# Additional labels applied to the service. Merged with environment and
# service labels for cost attribution and environment identification.
variable "labels" {
  description = "Additional labels applied to the service. Merged with environment and service labels (design §16.6)."
  type        = map(string)
  default     = {}
}

# Environment identifier (for example, dev). Also applied as a label.
variable "environment" {
  description = "The environment identifier for this deployment (for example, dev). Applied as a label for cost attribution and environment identification."
  type        = string
}

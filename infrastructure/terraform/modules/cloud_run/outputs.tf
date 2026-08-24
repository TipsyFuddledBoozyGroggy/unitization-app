# Outputs exposed by the cloud_run module.
# Re-exported by environments/dev/outputs.tf (design §16.3).

# The Cloud Run service name, so callers can reference the deployed
# service without recomputing it from inputs.
output "service_name" {
  description = "The name of the Cloud Run service."
  value       = google_cloud_run_v2_service.service.name
}

# The fully qualified URL that Cloud Run assigns to the service.
output "service_url" {
  description = "The publicly addressable URL of the Cloud Run service."
  value       = google_cloud_run_v2_service.service.uri
}

# Outputs re-exported from the cloud_run module (design §16.3).

# The Cloud Run service name, forwarded from the module output.
output "service_name" {
  description = "The name of the Cloud Run service."
  value       = module.cloud_run.service_name
}

# The publicly addressable URL of the Cloud Run service, forwarded
# from the module output.
output "service_url" {
  description = "The publicly addressable URL of the Cloud Run service."
  value       = module.cloud_run.service_url
}

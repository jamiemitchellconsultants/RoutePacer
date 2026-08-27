# RoutePacer deployment

Provision secrets outside source control, keep relay uploads and RouteTimer intake disabled, and deploy an immutable ROUTEPACER_IMAGE_TAG with docker compose -f deploy/docker-compose.yml up -d --pull always --wait. Validate and reload the shared Caddy fragment, check /health/ready, run the handoff smoke flow, then enable RoutePacer and finally RouteTimer. The relay database has no backup, restore, or rollback procedure; failures are corrected forward.

# RoutePacer deployment

The published image is **`linux/amd64` only** -- see the note in `.github/workflows/publish-container.yml` for why, and build the Dockerfile directly if you need another architecture.

RoutePacer serves the offline-first PWA and two health endpoints. It stores nothing, calls nothing, and reads no secret, so deployment is one container with no database, no credential and no env file: set `ROUTEPACER_IMAGE_TAG` to an immutable tag and run `docker compose -f deploy/docker-compose.yml up -d --pull always --wait`. Copy `deploy/caddy/routepacer.caddy` into the shared ingress drop-in directory, validate the whole configuration, reload Caddy without restarting it, then confirm `/health/ready` returns `200` and that the PWA installs and starts offline after a first load.

Riders' routes never reach this server. Imported routes live in the browser's IndexedDB on the phone, which is also why there is nothing here to back up.

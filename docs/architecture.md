# RoutePacer architecture

RoutePacer is a hosted Blazor WebAssembly PWA. RoutePacer.Core contains route normalization, FIT/GPX parsing, geodesy, matching, and pacing. RoutePacer.App owns offline workflows, browser capabilities, and IndexedDB. RoutePacer.Server hosts the application and optional relay. RoutePacer.Persistence maps the relay's four-column PostgreSQL table.

Imported routes and rides remain on-device. A handoff is stored as exact GPX bytes under a SHA-256 token hash and consumed with one DELETE ... RETURNING operation. The service worker caches application assets but never API, health, open-query, or non-GET requests.

# RoutePacer privacy

Manual imports, routes, rides, and GPS tracking remain in the browser IndexedDB. An explicit RouteTimer handoff temporarily processes readable GPX bytes in the relay database for no more than ten minutes; successful consumption deletes them immediately and expired rows are cleaned automatically. TLS protects transit. The relay has no backup or restore path, and logs exclude tokens, URLs, names, signatures, credentials, and route bytes.

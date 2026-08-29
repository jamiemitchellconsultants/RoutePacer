const databaseName = "routepacer";
// Version 2 drops the ride history stores. RoutePacer is a pacing aide, not a recorder -- the rider
// already has something recording the ride -- so finished rides are no longer kept, and the upgrade
// deletes any that version 1 left behind rather than stranding them on the device forever.
// Version 3 adds rider preferences, which outlive both the route and the ride.
const databaseVersion = 3;

function openDatabase() {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(databaseName, databaseVersion);
    request.onupgradeneeded = () => {
      const db = request.result;
      const routes = db.objectStoreNames.contains("routes") ? request.transaction.objectStore("routes") : db.createObjectStore("routes", { keyPath: "routeId" });
      const points = db.objectStoreNames.contains("route_points") ? request.transaction.objectStore("route_points") : db.createObjectStore("route_points", { keyPath: ["routeId", "index"] });
      if (!points.indexNames.contains("routeId")) points.createIndex("routeId", "routeId");

      for (const stale of ["rides", "ride_points"]) if (db.objectStoreNames.contains(stale)) db.deleteObjectStore(stale);

      // One in-progress ride, so a reload or an evicted tab does not end a ride mid-route. Cleared on
      // stop: nothing about a finished ride is kept.
      if (!db.objectStoreNames.contains("active_ride")) db.createObjectStore("active_ride", { keyPath: "rideId" });
      if (!db.objectStoreNames.contains("active_ride_points")) db.createObjectStore("active_ride_points", { keyPath: ["rideId", "sequence"] });
      if (!db.objectStoreNames.contains("settings")) db.createObjectStore("settings", { keyPath: "key" });
    };
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(new Error(`openDatabase:${request.error?.name ?? "UnknownError"}`));
  });
}

function transactionError(transaction, operation) { return new Error(`${operation}:${transaction.error?.name ?? "UnknownError"}`); }
function withTransaction(stores, mode, operation) {
  return openDatabase().then(db => new Promise((resolve, reject) => {
    const transaction = db.transaction(stores, mode);
    transaction.oncomplete = () => { db.close(); resolve(); };
    transaction.onerror = () => { db.close(); reject(transactionError(transaction, operation)); };
    transaction.onabort = () => { db.close(); reject(transactionError(transaction, operation)); };
    operation(transaction);
  }));
}

// The app holds exactly one route. Clearing and writing in ONE transaction is what makes that true:
// a failure part way through leaves the previous route intact rather than no route at all.
export const saveRoute = (summary, points) => withTransaction(["routes", "route_points"], "readwrite", tx => {
  tx.objectStore("routes").clear();
  const store = tx.objectStore("route_points");
  store.clear();
  tx.objectStore("routes").put(summary);
  for (const point of points) store.put(point);
});

export const getRoute = () => openDatabase().then(db => new Promise((resolve, reject) => {
  const tx = db.transaction(["routes", "route_points"]);
  const summary = tx.objectStore("routes").getAll();
  const points = tx.objectStore("route_points").getAll();
  tx.oncomplete = () => {
    db.close();
    resolve(summary.result.length ? { summary: summary.result[0], points: points.result.sort((a, b) => a.index - b.index) } : null);
  };
  tx.onerror = () => { db.close(); reject(transactionError(tx, "getRoute")); };
}));

export const clearRoute = () => withTransaction(["routes", "route_points"], "readwrite", tx => {
  tx.objectStore("routes").clear();
  tx.objectStore("route_points").clear();
});

// Starting replaces any earlier in-progress ride in the same transaction, so an abandoned one can
// never be mistaken for the current ride.
export const startRide = ride => withTransaction(["active_ride", "active_ride_points"], "readwrite", tx => {
  tx.objectStore("active_ride").clear();
  tx.objectStore("active_ride_points").clear();
  tx.objectStore("active_ride").put(ride);
});
export const saveActiveRide = ride => withTransaction(["active_ride"], "readwrite", tx => tx.objectStore("active_ride").put(ride));
export const appendRidePoint = point => withTransaction(["active_ride_points"], "readwrite", tx => tx.objectStore("active_ride_points").put(point));

export const getActiveRide = () => openDatabase().then(db => new Promise((resolve, reject) => {
  const tx = db.transaction(["active_ride", "active_ride_points"]);
  const ride = tx.objectStore("active_ride").getAll();
  const points = tx.objectStore("active_ride_points").getAll();
  tx.oncomplete = () => {
    db.close();
    resolve(ride.result.length ? { summary: ride.result[0], points: points.result.sort((a, b) => a.sequence - b.sequence) } : null);
  };
  tx.onerror = () => { db.close(); reject(transactionError(tx, "getActiveRide")); };
}));

export const clearRide = () => withTransaction(["active_ride", "active_ride_points"], "readwrite", tx => {
  tx.objectStore("active_ride").clear();
  tx.objectStore("active_ride_points").clear();
});

// One row, so the key is a constant. Absent means the rider has never chosen, which the caller
// reads as the default rather than as an error.
export const getAutoPause = () => openDatabase().then(db => new Promise((resolve, reject) => {
  const tx = db.transaction(["settings"]);
  const row = tx.objectStore("settings").get("autoPause");
  tx.oncomplete = () => { db.close(); resolve(row.result ?? null); };
  tx.onerror = () => { db.close(); reject(transactionError(tx, "getAutoPause")); };
}));

export const saveAutoPause = settings => withTransaction(["settings"], "readwrite", tx =>
  tx.objectStore("settings").put({ key: "autoPause", enabled: settings.enabled, thresholdSeconds: settings.thresholdSeconds }));

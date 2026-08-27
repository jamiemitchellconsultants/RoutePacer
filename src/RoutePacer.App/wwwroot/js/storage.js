const databaseName = "routepacer";
const databaseVersion = 1;

function openDatabase() {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(databaseName, databaseVersion);
    request.onupgradeneeded = () => {
      const db = request.result;
      const routes = db.objectStoreNames.contains("routes") ? request.transaction.objectStore("routes") : db.createObjectStore("routes", { keyPath: "routeId" });
      const points = db.objectStoreNames.contains("route_points") ? request.transaction.objectStore("route_points") : db.createObjectStore("route_points", { keyPath: ["routeId", "index"] });
      if (!points.indexNames.contains("routeId")) points.createIndex("routeId", "routeId");
      const rides = db.objectStoreNames.contains("rides") ? request.transaction.objectStore("rides") : db.createObjectStore("rides", { keyPath: "rideId" });
      if (!rides.indexNames.contains("routeId")) rides.createIndex("routeId", "routeId");
      const ridePoints = db.objectStoreNames.contains("ride_points") ? request.transaction.objectStore("ride_points") : db.createObjectStore("ride_points", { keyPath: ["rideId", "sequence"] });
      if (!ridePoints.indexNames.contains("rideId")) ridePoints.createIndex("rideId", "rideId");
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

export const saveRoute = (summary, points) => withTransaction(["routes", "route_points"], "readwrite", tx => {
  tx.objectStore("routes").put(summary);
  const store = tx.objectStore("route_points");
  for (const point of points) store.put(point);
});
export const listRoutes = () => openDatabase().then(db => new Promise((resolve, reject) => { const r = db.transaction("routes").objectStore("routes").getAll(); r.onsuccess = () => { db.close(); resolve(r.result.sort((a,b) => b.importedAtUtc.localeCompare(a.importedAtUtc))); }; r.onerror = () => { db.close(); reject(r.error); }; }));
export const getRoute = routeId => openDatabase().then(db => new Promise((resolve, reject) => { const tx = db.transaction(["routes", "route_points"]); const summary = tx.objectStore("routes").get(routeId); const points = tx.objectStore("route_points").index("routeId").getAll(routeId); tx.oncomplete = () => { db.close(); resolve(summary.result ? { summary: summary.result, points: points.result.sort((a,b) => a.index - b.index) } : null); }; tx.onerror = () => { db.close(); reject(tx.error); }; }));
export const deleteRoute = routeId => withTransaction(["routes", "route_points"], "readwrite", tx => { tx.objectStore("routes").delete(routeId); const index = tx.objectStore("route_points").index("routeId"); index.openKeyCursor(routeId).onsuccess = e => { const cursor = e.target.result; if (cursor) { cursor.delete(); cursor.continue(); } }; });
export const createRide = ride => withTransaction(["rides"], "readwrite", tx => tx.objectStore("rides").put(ride));
export const appendRidePoint = point => withTransaction(["ride_points"], "readwrite", tx => tx.objectStore("ride_points").put(point));
export const completeRide = ride => withTransaction(["rides"], "readwrite", tx => tx.objectStore("rides").put(ride));
export const listRides = () => openDatabase().then(db => new Promise((resolve, reject) => { const r = db.transaction("rides").objectStore("rides").getAll(); r.onsuccess = () => { db.close(); resolve(r.result.sort((a,b) => b.startedAtUtc.localeCompare(a.startedAtUtc))); }; r.onerror = () => { db.close(); reject(r.error); }; }));
export const getRidePoints = rideId => openDatabase().then(db => new Promise((resolve, reject) => { const r = db.transaction("ride_points").objectStore("ride_points").index("rideId").getAll(rideId); r.onsuccess = () => { db.close(); resolve(r.result.sort((a,b) => a.sequence - b.sequence)); }; r.onerror = () => { db.close(); reject(r.error); }; }));
export const deleteRide = rideId => withTransaction(["rides", "ride_points"], "readwrite", tx => { tx.objectStore("rides").delete(rideId); const index = tx.objectStore("ride_points").index("rideId"); index.openKeyCursor(rideId).onsuccess = e => { const cursor = e.target.result; if (cursor) { cursor.delete(); cursor.continue(); } }; });

let watchId = null;
export function startTracking(dotNetReference) {
  if (watchId !== null) return;
  if (!navigator.geolocation) { dotNetReference.invokeMethodAsync("OnError", 0); return; }
  watchId = navigator.geolocation.watchPosition(p => dotNetReference.invokeMethodAsync("OnPosition", p.timestamp, p.coords.latitude, p.coords.longitude, p.coords.accuracy, Number.isFinite(p.coords.speed) ? p.coords.speed : null), e => dotNetReference.invokeMethodAsync("OnError", e.code), { enableHighAccuracy: true, timeout: 5000, maximumAge: 0 });
}
export function stopTracking() { if (watchId !== null) navigator.geolocation.clearWatch(watchId); watchId = null; }

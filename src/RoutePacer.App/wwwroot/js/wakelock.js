let requested = false; let sentinel = null; let dotNetReference = null;
async function acquire() {
  if (!requested || document.visibilityState !== "visible" || sentinel) return;
  if (!navigator.wakeLock?.request) { await dotNetReference?.invokeMethodAsync("OnStatus", "Unsupported"); return; }
  try { sentinel = await navigator.wakeLock.request("screen"); sentinel.addEventListener("release", async () => { sentinel = null; if (requested) await dotNetReference?.invokeMethodAsync("OnStatus", "Revoked"); }); await dotNetReference?.invokeMethodAsync("OnStatus", "Acquired"); }
  catch { await dotNetReference?.invokeMethodAsync("OnStatus", "Failed"); }
}
export async function acquireWakeLock(reference) { requested = true; dotNetReference = reference; await acquire(); }
export async function releaseWakeLock() { requested = false; const current = sentinel; sentinel = null; if (current) await current.release(); await dotNetReference?.invokeMethodAsync("OnStatus", "Released"); }
document.addEventListener("visibilitychange", acquire);

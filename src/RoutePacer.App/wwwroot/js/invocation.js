function decode(value) { const text = atob(value.replace(/-/g, "+").replace(/_/g, "/") + "=".repeat((4 - value.length % 4) % 4)); return Uint8Array.from(text, c => c.charCodeAt(0)); }
export async function verifySignature(publicJwk, signature, canonicalBytes) {
  const jwk = typeof publicJwk === "string" ? JSON.parse(publicJwk) : publicJwk;
  if (jwk.kty !== "EC" || jwk.crv !== "P-256" || jwk.d || !jwk.x || !jwk.y) return false;
  const key = await crypto.subtle.importKey("jwk", jwk, { name: "ECDSA", namedCurve: "P-256" }, false, ["verify"]);
  return crypto.subtle.verify({ name: "ECDSA", hash: "SHA-256" }, key, decode(signature), canonicalBytes);
}

import { timingSafeEqual } from "node:crypto";

export function requireIngestAuth(request: Request): Response | null {
  const expected = process.env.INGEST_API_KEY;

  if (!expected) {
    return null;
  }

  const header = request.headers.get("authorization") ?? "";
  const token = header.startsWith("Bearer ") ? header.slice("Bearer ".length) : "";

  const a = Buffer.from(token);
  const b = Buffer.from(expected);

  if (a.length !== b.length || !timingSafeEqual(a, b)) {
    return new Response("Unauthorized", { status: 401 });
  }

  return null;
}

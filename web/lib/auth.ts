export function requireIngestAuth(request: Request): Response | null {
  const expected = process.env.INGEST_API_KEY;

  if (!expected) {
    return null;
  }

  const header = request.headers.get("authorization") ?? "";
  const token = header.startsWith("Bearer ") ? header.slice("Bearer ".length) : "";

  if (token !== expected) {
    return new Response("Unauthorized", { status: 401 });
  }

  return null;
}

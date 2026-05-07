import { NextResponse } from "next/server";
import { z } from "zod";
import { getMePrimaryId, setMePrimaryId } from "@/lib/settings";

export const runtime = "nodejs";
export const dynamic = "force-dynamic";

const bodySchema = z.object({ primaryId: z.string().nullable() });

export async function GET() {
  const id = await getMePrimaryId();
  return NextResponse.json({ primaryId: id });
}

export async function PUT(request: Request) {
  const body = await request.json().catch(() => null);
  const parsed = bodySchema.safeParse(body);
  if (!parsed.success) {
    return NextResponse.json({ error: "Invalid body" }, { status: 400 });
  }
  await setMePrimaryId(parsed.data.primaryId);
  return NextResponse.json({ ok: true });
}

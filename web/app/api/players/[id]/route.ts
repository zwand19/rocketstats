import { NextResponse } from "next/server";
import { z } from "zod";
import { setShowOnDashboard } from "@/lib/queries/players";
import { setMePrimaryId } from "@/lib/settings";
import { getPlayerDetail } from "@/lib/queries/player-detail";
import { getMePrimaryId } from "@/lib/settings";

export const runtime = "nodejs";
export const dynamic = "force-dynamic";

const patchSchema = z
  .object({
    showOnDashboard: z.boolean().optional(),
    isMe: z.boolean().optional(),
  })
  .refine((v) => v.showOnDashboard !== undefined || v.isMe !== undefined, {
    message: "At least one of showOnDashboard or isMe is required",
  });

export async function GET(_request: Request, { params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  const me = await getMePrimaryId();
  const detail = await getPlayerDetail(id, me);
  return NextResponse.json(detail);
}

export async function PATCH(request: Request, { params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  const body = await request.json().catch(() => null);
  const parsed = patchSchema.safeParse(body);

  if (!parsed.success) {
    return NextResponse.json({ error: "Invalid body", issues: parsed.error.issues }, { status: 400 });
  }

  if (parsed.data.showOnDashboard !== undefined) {
    await setShowOnDashboard(id, parsed.data.showOnDashboard);
  }

  if (parsed.data.isMe !== undefined) {
    await setMePrimaryId(parsed.data.isMe ? id : null);
  }

  return NextResponse.json({ ok: true });
}

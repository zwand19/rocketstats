import { NextResponse } from "next/server";
import { deleteMatch } from "@/lib/queries/games";

export const runtime = "nodejs";
export const dynamic = "force-dynamic";

export async function DELETE(_request: Request, { params }: { params: Promise<{ matchGuid: string }> }) {
  const { matchGuid } = await params;
  await deleteMatch(matchGuid);
  return NextResponse.json({ ok: true });
}

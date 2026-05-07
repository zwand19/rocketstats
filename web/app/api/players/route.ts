import { NextResponse } from "next/server";
import { listObservedPlayers } from "@/lib/queries/players";

export const runtime = "nodejs";
export const dynamic = "force-dynamic";

export async function GET() {
  const players = await listObservedPlayers();
  return NextResponse.json(players);
}

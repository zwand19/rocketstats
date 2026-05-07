import { NextResponse } from "next/server";
import { getDashboard } from "@/lib/queries/get-dashboard";

export const runtime = "nodejs";
export const dynamic = "force-dynamic";

export async function GET(request: Request) {
  const url = new URL(request.url);
  const modeParam = url.searchParams.get("gameMode");
  const gameMode = modeParam == null || modeParam === "" ? null : Number.parseInt(modeParam, 10);
  const safeMode = gameMode == null || Number.isNaN(gameMode) ? null : gameMode;

  const data = await getDashboard(safeMode);
  return NextResponse.json(data);
}

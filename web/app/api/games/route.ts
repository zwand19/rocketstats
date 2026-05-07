import { NextResponse } from "next/server";
import { getMatchesPaged } from "@/lib/queries/games";

export const runtime = "nodejs";
export const dynamic = "force-dynamic";

export async function GET(request: Request) {
  const url = new URL(request.url);
  const page = Number.parseInt(url.searchParams.get("page") ?? "1", 10) || 1;
  const pageSize = Number.parseInt(url.searchParams.get("pageSize") ?? "20", 10) || 20;
  const data = await getMatchesPaged(page, pageSize);
  return NextResponse.json(data);
}

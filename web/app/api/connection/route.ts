import { NextResponse } from "next/server";
import { getConnection } from "@/lib/queries/connection";

export const runtime = "nodejs";
export const dynamic = "force-dynamic";

export async function GET() {
  const data = await getConnection();
  return NextResponse.json(data);
}

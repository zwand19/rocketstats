import { eq, sql } from "drizzle-orm";
import { db } from "@/db/client";
import { appSettings } from "@/db/schema";

const ME_KEY = "me_primary_id";

export async function getMePrimaryId(): Promise<string | null> {
  const [row] = await db
    .select({ value: appSettings.value })
    .from(appSettings)
    .where(eq(appSettings.key, ME_KEY))
    .limit(1);

  return row?.value ?? null;
}

export async function setMePrimaryId(primaryId: string | null): Promise<void> {
  if (primaryId == null) {
    await db.delete(appSettings).where(eq(appSettings.key, ME_KEY));
    return;
  }

  await db
    .insert(appSettings)
    .values({ key: ME_KEY, value: primaryId })
    .onConflictDoUpdate({
      target: appSettings.key,
      set: { value: sql`excluded.value` },
    });
}

import { getHeadToHead, getPlayerAverages } from "./dashboard";
import type { HeadToHeadRecord, PlayerAverages } from "../api-types";

export type PlayerDetailPayload = {
  averages: PlayerAverages | null;
  headToHead: HeadToHeadRecord | null;
};

export async function getPlayerDetail(
  primaryId: string,
  mePrimaryId: string | null,
): Promise<PlayerDetailPayload> {
  const [averagesList, h2hList] = await Promise.all([
    getPlayerAverages([primaryId], null),
    mePrimaryId && mePrimaryId.toLowerCase() !== primaryId.toLowerCase()
      ? getHeadToHead(mePrimaryId, [primaryId], null)
      : Promise.resolve([]),
  ]);

  return {
    averages: averagesList[0] ?? null,
    headToHead: h2hList[0] ?? null,
  };
}

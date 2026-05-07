"use client";

import { useEffect, useMemo, useState } from "react";
import { formatGameMode, formatLocalDate, teamClass, teamLabel } from "@/lib/format";
import { getArenaDisplayName } from "@/lib/arena-names";
import type { MatchesPage, MatchSummary, MatchSummaryPlayer } from "@/lib/api-types";

const PAGE_SIZE = 20;

export function GamesView() {
  const [page, setPage] = useState(1);
  const [data, setData] = useState<MatchesPage | null>(null);
  const [pendingDeleteId, setPendingDeleteId] = useState<string | null>(null);

  async function load(targetPage: number) {
    const response = await fetch(`/api/games?page=${targetPage}&pageSize=${PAGE_SIZE}`, {
      cache: "no-store",
    });
    const json: MatchesPage = await response.json();

    if (json.totalCount > 0 && json.items.length === 0 && json.page > 1) {
      await load(json.page - 1);
      return;
    }

    setData(json);
    setPage(json.page);
    setPendingDeleteId(null);
  }

  useEffect(() => {
    load(1);
  }, []);

  const totalPages = useMemo(() => {
    if (!data) {
      return 1;
    }
    return Math.max(1, Math.ceil(data.totalCount / PAGE_SIZE));
  }, [data]);

  async function confirmDelete(matchGuid: string) {
    await fetch(`/api/games/${encodeURIComponent(matchGuid)}`, { method: "DELETE" });
    await load(page);
  }

  return (
    <>
      <section className="page-header">
        <div>
          <p className="eyebrow">Match history</p>
          <h1>Games</h1>
          <p>Every completed match the Stats API has captured. Newest first.</p>
        </div>
        <div className="actions">
          <button className="link-button" onClick={() => load(page)}>
            Refresh
          </button>
        </div>
      </section>

      <section className="card">
        <div className="section-title">
          <h2>Matches</h2>
          <span className="muted">{data?.totalCount ?? 0} total</span>
        </div>

        {data == null ? (
          <p className="muted">Loading…</p>
        ) : data.totalCount === 0 ? (
          <p className="muted">No matches recorded yet. Play or watch a replay with the Stats API enabled.</p>
        ) : (
          <>
            <div className="games-list">
              {data.items.map((match) => (
                <GameRow
                  key={match.matchGuid}
                  match={match}
                  isPendingDelete={
                    pendingDeleteId != null &&
                    pendingDeleteId.toLowerCase() === match.matchGuid.toLowerCase()
                  }
                  onRequestDelete={() => setPendingDeleteId(match.matchGuid)}
                  onCancelDelete={() => setPendingDeleteId(null)}
                  onConfirmDelete={() => confirmDelete(match.matchGuid)}
                />
              ))}
            </div>

            {totalPages > 1 ? (
              <div className="pagination">
                <button
                  className="link-button"
                  disabled={page <= 1}
                  onClick={() => load(page - 1)}
                >
                  Previous
                </button>
                <span className="muted">
                  Page {page} of {totalPages}
                </span>
                <button
                  className="link-button"
                  disabled={page >= totalPages}
                  onClick={() => load(page + 1)}
                >
                  Next
                </button>
              </div>
            ) : null}
          </>
        )}
      </section>
    </>
  );
}

function GameRow({
  match,
  isPendingDelete,
  onRequestDelete,
  onCancelDelete,
  onConfirmDelete,
}: {
  match: MatchSummary;
  isPendingDelete: boolean;
  onRequestDelete: () => void;
  onCancelDelete: () => void;
  onConfirmDelete: () => void;
}) {
  const teams = useMemo(() => groupTeams(match.players), [match.players]);

  return (
    <article className="game-row">
      <div className="game-meta">
        <strong>{formatLocalDate(match.endedAt)}</strong>
        <span className="muted">{getArenaDisplayName(match.arena)}</span>
        <span className="muted">{formatGameMode(match.gameMode)}</span>
      </div>

      <div className="game-teams">
        {teams.map((team) => {
          const isWinner = match.winningTeam === team.teamNum;
          return (
            <div
              key={team.teamNum}
              className={`game-team ${teamClass(team.teamNum)} ${isWinner ? "game-team-winner" : ""}`}
            >
              <div className="game-team-header">
                <span className="team-dot" />
                <strong>{teamLabel(team.teamNum)}</strong>
                {isWinner ? <span className="chip chip-up">W</span> : null}
              </div>
              {team.players.map((p) => (
                <span key={p.primaryId} className="game-player">
                  <strong>{p.name}</strong>
                  <span className="muted">
                    {p.goals} G · {p.assists} A · {p.saves} S
                  </span>
                </span>
              ))}
            </div>
          );
        })}
      </div>

      <div className="game-actions">
        {isPendingDelete ? (
          <>
            <span className="muted">Delete this match?</span>
            <button className="secondary-button" onClick={onConfirmDelete}>
              Confirm
            </button>
            <button className="link-button" onClick={onCancelDelete}>
              Cancel
            </button>
          </>
        ) : (
          <button className="link-button" onClick={onRequestDelete}>
            Delete
          </button>
        )}
      </div>
    </article>
  );
}

function groupTeams(players: MatchSummaryPlayer[]) {
  const map = new Map<number, MatchSummaryPlayer[]>();
  for (const p of players) {
    const list = map.get(p.teamNum) ?? [];
    list.push(p);
    map.set(p.teamNum, list);
  }
  return Array.from(map.entries())
    .sort((a, b) => a[0] - b[0])
    .map(([teamNum, ps]) => ({
      teamNum,
      players: ps.slice().sort((a, b) => b.score - a.score),
    }));
}

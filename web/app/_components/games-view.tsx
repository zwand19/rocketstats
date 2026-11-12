"use client";

import { useEffect, useMemo, useState } from "react";
import { formatGameMode, formatLocalDate, formatShortDateTime, teamClass, teamLabel } from "@/lib/format";
import { TableSkeleton } from "./skeleton";
import { getArenaDisplayName } from "@/lib/arena-names";
import type { MatchesPage, MatchSummary, MatchSummaryPlayer } from "@/lib/api-types";

const PAGE_SIZE = 20;

export function GamesView() {
  const [page, setPage] = useState(1);
  const [data, setData] = useState<MatchesPage | null>(null);
  const [pendingDeleteId, setPendingDeleteId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function load(targetPage: number) {
    let json: MatchesPage;

    try {
      const response = await fetch(`/api/games?page=${targetPage}&pageSize=${PAGE_SIZE}`, {
        cache: "no-store",
      });
      if (!response.ok) {
        throw new Error(`${response.status} ${response.statusText}`);
      }
      json = await response.json();
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
      return;
    }

    setError(null);

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
    const response = await fetch(`/api/games/${encodeURIComponent(matchGuid)}`, { method: "DELETE" });
    if (!response.ok) {
      setError(`Delete failed (${response.status})`);
      return;
    }
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

        {error ? <div className="alert">Couldn&apos;t load matches: {error}</div> : null}

        {data == null ? (
          error ? null : <TableSkeleton rows={4} />
        ) : data.totalCount === 0 ? (
          <p className="empty">No matches recorded yet. Play or watch a replay with the Stats API enabled.</p>
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
  const hasScore = teams.length === 2;

  function renderTeam(team: (typeof teams)[number], alignEnd: boolean) {
    const decided = match.winningTeam != null;
    const isWinner = match.winningTeam === team.teamNum;
    return (
      <div
        key={team.teamNum}
        className={`game-team ${teamClass(team.teamNum)}${alignEnd ? " align-end" : ""}${decided && !isWinner ? " is-loser" : ""}`}
        aria-label={`${teamLabel(team.teamNum)}${isWinner ? " (won)" : ""}`}
      >
        {team.players.map((p) => (
          <span key={p.primaryId} className="game-player">
            <strong>{p.name}</strong>
            <span className="muted" title="Goals / assists / saves">
              {p.goals}G {p.assists}A {p.saves}S
            </span>
          </span>
        ))}
      </div>
    );
  }

  function renderScore(team: (typeof teams)[number]) {
    return (
      <span
        className={`score ${teamClass(team.teamNum)}${match.winningTeam === team.teamNum ? " is-winner" : ""}`}
      >
        {team.goals}
      </span>
    );
  }

  return (
    <article className="game-row">
      <div className="game-meta">
        <strong title={formatLocalDate(match.endedAt)}>{formatShortDateTime(match.endedAt)}</strong>
        <span className="muted">{getArenaDisplayName(match.arena)}</span>
        <span className="chip chip-neutral">{formatGameMode(match.gameMode)}</span>
      </div>

      {hasScore ? (
        <div className="game-body">
          {renderTeam(teams[0], true)}
          <div className="game-score">
            {renderScore(teams[0])}
            <span className="dash">–</span>
            {renderScore(teams[1])}
          </div>
          {renderTeam(teams[1], false)}
        </div>
      ) : (
        <div className="game-body no-score">{teams.map((team) => renderTeam(team, false))}</div>
      )}

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
          <button className="link-button ghost-button" onClick={onRequestDelete}>
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
      goals: ps.reduce((sum, p) => sum + p.goals, 0),
      players: ps.slice().sort((a, b) => b.score - a.score),
    }));
}

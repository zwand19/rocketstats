"use client";

import { useEffect, useMemo, useState } from "react";
import Link from "next/link";
import { usePoll } from "@/lib/use-poll";
import {
  formatGameMode,
  formatLocalTime,
  formatNullable,
  formatShortDateTime,
  formatTime,
  teamClass,
  teamLabel,
} from "@/lib/format";
import { getArenaDisplayName } from "@/lib/arena-names";
import { TableSkeleton } from "./skeleton";
import type {
  DashboardPayload,
  HeadToHeadRecord,
  LivePlayerStatsView,
  PlayerAverages,
  SessionAverages,
} from "@/lib/api-types";

const MODE_CHIPS: { mode: number | null; label: string }[] = [
  { mode: null, label: "Total" },
  { mode: 6, label: "3v3" },
  { mode: 4, label: "2v2" },
  { mode: 2, label: "1v1" },
];

export function DashboardView() {
  const [selectedMode, setSelectedMode] = useState<number | null>(null);
  const [userChose, setUserChose] = useState(false);

  const url = `/api/dashboard${selectedMode == null ? "" : `?gameMode=${selectedMode}`}`;
  const { data, error } = usePoll<DashboardPayload>(url, 2000);

  const detectedMode = useMemo(() => {
    if (!data) {
      return null;
    }
    if (data.live && data.live.players.length > 0) {
      return data.live.players.length;
    }
    return data.currentSession?.gameMode ?? null;
  }, [data]);

  useEffect(() => {
    if (!userChose && detectedMode != null && detectedMode !== selectedMode && isKnownChip(detectedMode)) {
      setSelectedMode(detectedMode);
    }
  }, [detectedMode, selectedMode, userChose]);

  const effectiveMode = selectedMode;

  function pickMode(mode: number | null) {
    setUserChose(true);
    setSelectedMode(mode);
  }

  return (
    <>
      <section className="page-header">
        <div>
          <p className="eyebrow">Tracked stats</p>
          <h1>Dashboard</h1>
          <p>Live match state and per-game averages for the players you&apos;re tracking.</p>
        </div>
      </section>

      {error && !data ? (
        <section className="alert">Couldn&apos;t load the dashboard ({error.message}). Retrying…</section>
      ) : null}

      <LiveCard payload={data} />

      {data?.currentSession ? (
        <SessionCard
          payload={data}
          session={data.currentSession}
          sessionAverages={data.sessionAverages}
          careerForCompare={data.careerForSessionCompare}
        />
      ) : null}

      <StatsCard
        payload={data}
        effectiveMode={effectiveMode}
        onSelectMode={pickMode}
      />
    </>
  );
}

function LiveCard({ payload }: { payload: DashboardPayload | null }) {
  const live = payload?.live ?? null;
  const meTeamNum = useMemo(() => deriveMyTeamNum(payload), [payload]);
  const headToHeadMap = useMemo(() => {
    const map = new Map<string, HeadToHeadRecord>();
    for (const r of payload?.headToHead ?? []) {
      map.set(r.opponentId.toLowerCase(), r);
    }
    return map;
  }, [payload]);

  const teams = live ? groupTeams(live.players) : [];
  const isTwoTeams = teams.length === 2 && teams[0].teamNum === 0 && teams[1].teamNum === 1;
  const me = payload?.mePrimaryId?.toLowerCase() ?? null;
  const clock = live ? `${live.isOvertime ? "+" : ""}${formatTime(live.timeSeconds)}` : "";

  return (
    <section className="card">
      <div className="section-title">
        <h2>
          {live ? <span className="live-dot" title="Receiving live data" /> : null}
          Live match
        </h2>
        {live ? <span className="muted">Updated {formatLocalTime(live.updatedAt)}</span> : null}
      </div>

      {payload == null ? (
        <TableSkeleton rows={2} />
      ) : live == null ? (
        <p className="empty">
          No match in progress. Start a match or replay in Rocket League with the Stats API
          enabled and it will show up here within a few seconds.
        </p>
      ) : (
        <>
          <div className="scoreboard">
            {isTwoTeams ? (
              <div className="score-side team-blue">
                <span className="score-team">Blue</span>
                <span className="score-num">{teams[0].goals}</span>
              </div>
            ) : (
              <span />
            )}
            <div className="score-center">
              <span className="score-clock">{clock}</span>
              <span className="score-meta">{getArenaDisplayName(live.arena)}</span>
              {live.isOvertime ? <span className="chip chip-ot">OVERTIME</span> : null}
            </div>
            {isTwoTeams ? (
              <div className="score-side team-orange">
                <span className="score-team">Orange</span>
                <span className="score-num">{teams[1].goals}</span>
              </div>
            ) : (
              <span />
            )}
          </div>

          {teams.length > 0 ? (
            <div className="live-teams">
              {teams.map((team) => (
                <div key={team.teamNum} className={`live-team ${teamClass(team.teamNum)}`}>
                  <div className="live-team-header">
                    <span className="team-dot" />
                    <span>{teamLabel(team.teamNum)}</span>
                  </div>

                  {team.players.map((player) => {
                    const isOpponent = meTeamNum >= 0 && player.teamNum !== meTeamNum;
                    const h2h = isOpponent ? headToHeadMap.get(player.primaryId.toLowerCase()) : undefined;
                    return (
                      <article key={player.primaryId} className="live-player">
                        <div className="live-player-top">
                          <strong className="player-name">
                            <span>{player.name}</span>
                            {me != null && player.primaryId.toLowerCase() === me ? (
                              <span className="chip chip-me">You</span>
                            ) : null}
                          </strong>
                          {h2h ? (
                            <div
                              className="head-to-head"
                              title={`Your lifetime record against ${player.name} over ${h2h.gamesPlayed} ${h2h.gamesPlayed === 1 ? "game" : "games"}`}
                            >
                              <span>vs</span>
                              <strong className="record-text">
                                {h2h.wins}-{h2h.losses}
                              </strong>
                            </div>
                          ) : null}
                        </div>
                        <div className="live-stats">
                          <LiveStat label="Score" value={player.score} />
                          <LiveStat label="Goals" value={player.goals} />
                          <LiveStat label="Assists" value={player.assists} />
                          <LiveStat label="Saves" value={player.saves} />
                          <LiveStat label="Shots" value={player.shots} />
                          <div className="live-stat live-stat-boost">
                            <small>Boost {player.boost}</small>
                            <div className="boost-meter">
                              <span style={{ width: `${Math.max(0, Math.min(100, player.boost))}%` }} />
                            </div>
                          </div>
                        </div>
                      </article>
                    );
                  })}
                </div>
              ))}
            </div>
          ) : null}

          {payload?.rematch && payload.rematch.allTimeGames > 0 ? (
            <div className="rematch-card">
              <div className="rematch-title">
                <strong>Rematch vs this lineup</strong>
                <span className="chip chip-neutral">{formatGameMode(payload.rematch.gameMode)}</span>
              </div>
                <div className="rematch-row">
                  <span className="muted">All time</span>
                  <strong className="record-text">
                    {payload.rematch.allTimeWins}-{payload.rematch.allTimeLosses}
                  </strong>
                  <span className="muted">
                    {payload.rematch.allTimeGames}{" "}
                    {payload.rematch.allTimeGames === 1 ? "game" : "games"}
                  </span>
                </div>
                {showSessionRow(payload.rematch) ? (
                  <div className="rematch-row">
                    <span className="muted">This session</span>
                    <strong className="record-text">
                      {payload.rematch.sessionWins}-{payload.rematch.sessionLosses}
                    </strong>
                    <span className="muted">
                      {payload.rematch.sessionGames}{" "}
                      {payload.rematch.sessionGames === 1 ? "game" : "games"}
                    </span>
                  </div>
                ) : null}
                {payload.rematch.streakGames > 0 ? (
                  <div className="rematch-row">
                    <span className="muted">Streak</span>
                    <strong className="record-text">
                      {payload.rematch.streakWins}-{payload.rematch.streakLosses}
                    </strong>
                    <span className="muted">
                      last {payload.rematch.streakGames} in a row
                    </span>
                  </div>
                ) : null}
            </div>
          ) : null}
        </>
      )}
    </section>
  );
}

function SessionCard({
  payload,
  session,
  sessionAverages,
  careerForCompare,
}: {
  payload: DashboardPayload;
  session: NonNullable<DashboardPayload["currentSession"]>;
  sessionAverages: SessionAverages[];
  careerForCompare: PlayerAverages[];
}) {
  return (
    <section className="card">
      <div className="section-title">
        <h2>Current session</h2>
        <span className="muted">
          Started {formatShortDateTime(session.startedAt)} · {formatGameMode(session.gameMode)} ·{" "}
          {session.matchGuids.length} {session.matchGuids.length === 1 ? "game" : "games"}
          {payload.meStreak ? (
            <>
              {" "}· Streak:{" "}
              <span className={`chip ${payload.meStreak.isWinning ? "chip-up" : "chip-down"}`}>
                {payload.meStreak.count}
                {payload.meStreak.isWinning ? "W" : "L"}
              </span>
            </>
          ) : null}
        </span>
      </div>

      {sessionAverages.length === 0 ? (
        <p className="empty">No tracked players have games in this session yet.</p>
      ) : (
        <div className="table-scroll">
        <div className="averages-table session-table">
          <div className="averages-row averages-row-header">
            <span>Player</span>
            <span title="Games played">GP</span>
            <span title="Wins-Losses">W-L</span>
            <span title="Share of games with the highest score in the match">MVP %</span>
            <span title="Score per game">Score</span>
            <span title="Goals per game">Goals</span>
            <span title="Assists per game">Assists</span>
            <span title="Saves per game">Saves</span>
            <span title="Shots per game">Shots</span>
            <span title="Touches per game">Touches</span>
            <span title="Demos per game">Demos</span>
            <span title="Times demoed per game">Demoed</span>
            <span title="Average speed (km/h, while in car)">Speed</span>
            <span title="Percent of time at supersonic">Sonic %</span>
            <span title="Average boost amount">Boost</span>
          </div>

          {sessionAverages.map((s) => {
            const allTime = careerForCompare.find(
              (c) => c.primaryId.toLowerCase() === s.primaryId.toLowerCase(),
            );
            return (
              <div key={s.primaryId} className="averages-row">
                <strong className="player-name">
                  <span>{s.name}</span>
                </strong>
                <span>{s.gamesPlayed}</span>
                <span>
                  {s.wins}-{s.losses}
                </span>
                <SessionStat value={s.mvpPercent} all={allTime?.mvpPercent} digits={0} suffix="%" />
                <SessionStat value={s.score} all={allTime?.score} digits={1} />
                <SessionStat value={s.goals} all={allTime?.goals} digits={2} />
                <SessionStat value={s.assists} all={allTime?.assists} digits={2} />
                <SessionStat value={s.saves} all={allTime?.saves} digits={2} />
                <SessionStat value={s.shots} all={allTime?.shots} digits={2} />
                <SessionStat value={s.touches} all={allTime?.touches} digits={1} />
                <SessionStat value={s.demos} all={allTime?.demos} digits={2} />
                <SessionStat value={s.demoed} all={allTime?.demoed} digits={2} />
                <SessionStat value={s.averageSpeedKph} all={allTime?.averageSpeedKph} digits={0} />
                <SessionStat
                  value={s.supersonicPercent}
                  all={allTime?.supersonicPercent}
                  digits={0}
                  suffix="%"
                />
                <SessionStat value={s.boost} all={allTime?.boost} digits={1} />
              </div>
            );
          })}
        </div>
        </div>
      )}
      {sessionAverages.length > 0 ? (
        <p className="table-note">Per-game averages. Badges compare this session with the all-time average in this mode.</p>
      ) : null}
    </section>
  );
}

function StatsCard({
  payload,
  effectiveMode,
  onSelectMode,
}: {
  payload: DashboardPayload | null;
  effectiveMode: number | null;
  onSelectMode: (mode: number | null) => void;
}) {
  const dashboardPlayers = payload?.dashboardPlayers ?? [];
  const careerForCard = payload?.careerAverages ?? [];

  return (
    <section className="card">
      <div className="section-title">
        <h2>Career averages</h2>
        <div className="mode-chips" role="group" aria-label="Game mode">
        {MODE_CHIPS.map((option) => (
          <button
            key={option.label}
            type="button"
            className={`chip mode-chip ${option.mode === effectiveMode ? "chip-selected" : ""}`}
            aria-pressed={option.mode === effectiveMode}
            onClick={() => onSelectMode(option.mode)}
          >
            {option.label}
          </button>
        ))}
        </div>
      </div>

      {payload == null ? (
        <TableSkeleton rows={3} />
      ) : dashboardPlayers.length === 0 ? (
        <p className="empty">
          No players are tracked yet. Open <Link href="/players">Players</Link> and track someone
          to see their averages here.
        </p>
      ) : careerForCard.length === 0 ? (
        <p className="empty">
          No completed matches yet for tracked players in {modeLabel(effectiveMode)}. Averages
          appear after a match ends.
        </p>
      ) : (
        <div className="table-scroll">
        <div className="averages-table">
          <div className="averages-row averages-row-header">
            <span>Player</span>
            <span title="Games played">GP</span>
            <span title="Share of games with the highest score in the match">MVP %</span>
            <span title="Score per game">Score</span>
            <span title="Goals per game">Goals</span>
            <span title="Assists per game">Assists</span>
            <span title="Saves per game">Saves</span>
            <span title="Shots per game">Shots</span>
            <span title="Touches per game">Touches</span>
            <span title="Demos per game">Demos</span>
            <span title="Times demoed per game">Demoed</span>
            <span title="Average speed (km/h, while in car)">Speed</span>
            <span title="Percent of time at supersonic">Sonic %</span>
            <span title="Average boost amount">Boost</span>
          </div>

          {dashboardPlayers.map((player) => {
            const averages = careerForCard.find(
              (a) => a.primaryId.toLowerCase() === player.primaryId.toLowerCase(),
            );

            return (
              <div key={player.primaryId} className="averages-row">
                <strong className="player-name">
                  <span>{player.name}</span>
                </strong>
                {averages == null ? (
                  <>
                    <span>0</span>
                    {Array.from({ length: 12 }).map((_, i) => (
                      <span key={i} className="muted">—</span>
                    ))}
                  </>
                ) : (
                  <>
                    <span>{averages.gamesPlayed}</span>
                    <span>{averages.mvpPercent.toFixed(0)}%</span>
                    <span>{averages.score.toFixed(1)}</span>
                    <span>{averages.goals.toFixed(2)}</span>
                    <span>{averages.assists.toFixed(2)}</span>
                    <span>{averages.saves.toFixed(2)}</span>
                    <span>{averages.shots.toFixed(2)}</span>
                    <span>{averages.touches.toFixed(1)}</span>
                    <span>{averages.demos.toFixed(2)}</span>
                    <span>{formatNullable(averages.demoed, 2)}</span>
                    <span>{formatNullable(averages.averageSpeedKph, 0)}</span>
                    <span>{formatNullable(averages.supersonicPercent, 0, "%")}</span>
                    <span>{averages.boost.toFixed(1)}</span>
                  </>
                )}
              </div>
            );
          })}
        </div>
        </div>
      )}
    </section>
  );
}

function LiveStat({ label, value }: { label: string; value: number }) {
  return (
    <div className="live-stat">
      <small>{label}</small>
      <strong>{value}</strong>
    </div>
  );
}

function SessionStat({
  value,
  all,
  digits,
  suffix = "",
}: {
  value: number | null | undefined;
  all: number | null | undefined;
  digits: number;
  suffix?: string;
}) {
  if (value == null) {
    return <span className="muted">—</span>;
  }

  const showAll = all != null;
  const diff = showAll ? value - all : 0;
  const threshold = Math.pow(10, -digits) / 2;

  return (
    <span className="session-stat">
      <span>
        {value.toFixed(digits)}
        {suffix}
      </span>
      {showAll && diff > threshold ? (
        <span className="chip chip-up">
          +{diff.toFixed(digits)}
          {suffix}
        </span>
      ) : null}
      {showAll && diff < -threshold ? (
        <span className="chip chip-down">
          {diff.toFixed(digits)}
          {suffix}
        </span>
      ) : null}
    </span>
  );
}

function deriveMyTeamNum(payload: DashboardPayload | null): number {
  if (!payload?.live || payload.live.players.length === 0 || payload.dashboardPlayers.length === 0) {
    return -1;
  }
  const tracked = new Set(payload.dashboardPlayers.map((p) => p.primaryId.toLowerCase()));
  const me = payload.live.players.find((p) => tracked.has(p.primaryId.toLowerCase()));
  return me ? me.teamNum : -1;
}

function groupTeams(players: LivePlayerStatsView[]) {
  const map = new Map<number, LivePlayerStatsView[]>();
  for (const p of players) {
    const list = map.get(p.teamNum) ?? [];
    list.push(p);
    map.set(p.teamNum, list);
  }
  return Array.from(map.entries())
    .sort((a, b) => a[0] - b[0])
    .map(([teamNum, plist]) => ({
      teamNum,
      goals: plist.reduce((sum, p) => sum + p.goals, 0),
      players: plist.slice().sort((a, b) => b.score - a.score),
    }));
}

function showSessionRow(rematch: NonNullable<DashboardPayload["rematch"]>) {
  if (rematch.sessionGames === 0) {
    return false;
  }
  return rematch.sessionWins !== rematch.streakWins || rematch.sessionLosses !== rematch.streakLosses;
}

function isKnownChip(mode: number) {
  return MODE_CHIPS.some((c) => c.mode === mode);
}

function modeLabel(mode: number | null): string {
  return MODE_CHIPS.find((c) => c.mode === mode)?.label ?? "Total";
}


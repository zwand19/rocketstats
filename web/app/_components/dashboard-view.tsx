"use client";

import { useEffect, useMemo, useState } from "react";
import { usePoll } from "@/lib/use-poll";
import {
  formatGameMode,
  formatLocalTime,
  formatNullable,
  formatTime,
  teamClass,
  teamLabel,
} from "@/lib/format";
import { getArenaDisplayName } from "@/lib/arena-names";
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
  const { data } = usePoll<DashboardPayload>(url, 2000);

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

  return (
    <section className="card">
      <div className="section-title">
        <h2>
          {live ? <span className="live-dot" title="Receiving live data" /> : null}
          Live match
        </h2>
        <span className="muted">{live ? formatLocalTime(live.updatedAt) : "No match state yet"}</span>
      </div>

      {live == null ? (
        <p className="muted">
          Start Rocket League with the Stats API enabled, then start a match or replay.
        </p>
      ) : (
        <>
          <div className="rank-list">
            <div className="rank-row">
              <strong>{formatTime(live.timeSeconds)}</strong>
              <span>
                {live.isOvertime ? "Overtime" : "Regulation"} · {getArenaDisplayName(live.arena)}
              </span>
              <span className="muted live-match-guid">{live.matchGuid ?? "Local"}</span>
            </div>
          </div>

          {live.players.length > 0 ? (
            <div className="live-teams">
              {groupTeams(live.players).map((team) => (
                <div key={team.teamNum} className={`live-team ${teamClass(team.teamNum)}`}>
                  <div className="live-team-header">
                    <span className="team-dot" />
                    <strong>{teamLabel(team.teamNum)}</strong>
                    <span className="muted">
                      {team.players.length} {team.players.length === 1 ? "player" : "players"}
                    </span>
                  </div>

                  {team.players.map((player) => {
                    const isOpponent = meTeamNum >= 0 && player.teamNum !== meTeamNum;
                    const h2h = isOpponent ? headToHeadMap.get(player.primaryId.toLowerCase()) : undefined;
                    return (
                      <article
                        key={player.primaryId}
                        className={`live-player-card ${teamClass(team.teamNum)}`}
                      >
                        <div className="live-player-card-top">
                          <strong>{player.name}</strong>
                          {h2h ? (
                            <div className="head-to-head">
                              <span className="muted">Lifetime</span>
                              <strong className="record-text">
                                {h2h.wins}-{h2h.losses}
                              </strong>
                              <span className="muted">
                                ({h2h.gamesPlayed} {h2h.gamesPlayed === 1 ? "game" : "games"})
                              </span>
                            </div>
                          ) : null}
                        </div>
                        <div className="mini-stats">
                          <span>{player.score} pts</span>
                          <span>{player.goals} G</span>
                          <span>{player.assists} A</span>
                          <span>{player.saves} S</span>
                          <span>{player.shots} shots</span>
                          <span>{player.boost} boost</span>
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
                <span className="muted">{formatGameMode(payload.rematch.gameMode)}</span>
              </div>
              <div className="rematch-rows">
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
                      last {payload.rematch.streakGames} consecutive{" "}
                      {payload.rematch.streakGames === 1 ? "game" : "games"}
                    </span>
                  </div>
                ) : null}
              </div>
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
          Started {new Date(session.startedAt).toLocaleString()} · {formatGameMode(session.gameMode)} ·{" "}
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
        <p className="muted">No tracked players have games in this session yet.</p>
      ) : (
        <div className="averages-table session-table">
          <div className="averages-row averages-row-header">
            <span>Player</span>
            <span title="Games played">GP</span>
            <span title="Wins-Losses">W-L</span>
            <span title="MVP percentage (most points in match)">MVP %</span>
            <span>Score/g</span>
            <span>Goals/g</span>
            <span>Assists/g</span>
            <span>Saves/g</span>
            <span>Shots/g</span>
            <span>Touches/g</span>
            <span>Demos/g</span>
            <span title="Times demoed per game">Demoed/g</span>
            <span title="Average speed (km/h, while in car)">Speed</span>
            <span title="Percent of time at supersonic">SS %</span>
            <span>Boost avg</span>
          </div>

          {sessionAverages.map((s) => {
            const allTime = careerForCompare.find(
              (c) => c.primaryId.toLowerCase() === s.primaryId.toLowerCase(),
            );
            return (
              <div key={s.primaryId} className="averages-row">
                <div>
                  <strong>{s.name}</strong>
                </div>
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
      )}
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
        <h2>Stats</h2>
        <span className="muted">
          {dashboardPlayers.length} {dashboardPlayers.length === 1 ? "player" : "players"} tracked ·{" "}
          {modeLabel(effectiveMode)}
        </span>
      </div>

      <div className="mode-chips">
        {MODE_CHIPS.map((option) => (
          <button
            key={option.label}
            type="button"
            className={`chip mode-chip ${option.mode === effectiveMode ? "chip-selected" : ""}`}
            onClick={() => onSelectMode(option.mode)}
          >
            {option.label}
          </button>
        ))}
      </div>

      {dashboardPlayers.length === 0 ? (
        <p className="muted">
          No players are tracked yet. Open <a href="/players">Players</a> and Track someone to see
          their averages here.
        </p>
      ) : careerForCard.length === 0 ? (
        <p className="muted">
          No completed matches yet for tracked players in {modeLabel(effectiveMode)}. Averages
          appear after a match ends.
        </p>
      ) : (
        <div className="averages-table">
          <div className="averages-row averages-row-header">
            <span>Player</span>
            <span title="Games played">GP</span>
            <span title="MVP percentage (most points in match)">MVP %</span>
            <span>Score/g</span>
            <span>Goals/g</span>
            <span>Assists/g</span>
            <span>Saves/g</span>
            <span>Shots/g</span>
            <span>Touches/g</span>
            <span>Demos/g</span>
            <span title="Times demoed per game">Demoed/g</span>
            <span title="Average speed (km/h, while in car)">Speed</span>
            <span title="Percent of time at supersonic">SS %</span>
            <span>Boost avg</span>
          </div>

          {dashboardPlayers.map((player) => {
            const averages = careerForCard.find(
              (a) => a.primaryId.toLowerCase() === player.primaryId.toLowerCase(),
            );

            return (
              <div key={player.primaryId} className="averages-row">
                <div>
                  <strong>{player.name}</strong>
                </div>
                {averages == null ? (
                  <>
                    {Array.from({ length: 12 }).map((_, i) => (
                      <span key={i} className="muted">—</span>
                    ))}
                    <span className="muted">No matches yet</span>
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
      )}
    </section>
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


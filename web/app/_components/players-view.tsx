"use client";

import { useCallback, useMemo, useState } from "react";
import { usePoll } from "@/lib/use-poll";
import { formatLocalDate, formatNullable } from "@/lib/format";
import type {
  ConnectionPayload,
  HeadToHeadRecord,
  ObservedPlayerView,
  PlayerAverages,
} from "@/lib/api-types";

export function PlayersView() {
  const { data: players } = usePoll<ObservedPlayerView[]>("/api/players", 5000, []);
  const { data: connection } = usePoll<ConnectionPayload>("/api/connection", 5000);
  const [search, setSearch] = useState("");
  const [selected, setSelected] = useState<ObservedPlayerView | null>(null);
  const [refreshTick, setRefreshTick] = useState(0);

  const filtered = useMemo(() => {
    const list = players ?? [];
    if (search.trim().length === 0) {
      return list;
    }
    const term = search.toLowerCase();
    return list.filter((p) => p.name.toLowerCase().includes(term));
  }, [players, search]);

  const meIsSet = (players ?? []).some((p) => p.isMe);

  const refreshSelected = useCallback(() => {
    setRefreshTick((t) => t + 1);
  }, []);

  async function patchPlayer(primaryId: string, body: { showOnDashboard?: boolean; isMe?: boolean }) {
    await fetch(`/api/players/${encodeURIComponent(primaryId)}`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    });
  }

  return (
    <>
      <section className="page-header">
        <div>
          <p className="eyebrow">Tracking management</p>
          <h1>Players</h1>
          <p>
            Everyone seen by the Stats API is collected. Click a player for stats; toggle whether
            they appear on the dashboard.
          </p>
        </div>
      </section>

      <section className="card">
        <div className="section-title">
          <h2>Observed players</h2>
          <span className="muted">
            {filtered.length} of {(players ?? []).length} · {connection?.state ?? "—"}
          </span>
        </div>

        {connection?.lastError ? <section className="alert">{connection.lastError}</section> : null}

        <div className="player-search">
          <input
            className="input"
            type="search"
            placeholder="Search by name…"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
          {search.length > 0 ? (
            <button className="link-button" onClick={() => setSearch("")}>
              Clear
            </button>
          ) : null}
        </div>

        {(players ?? []).length === 0 ? (
          <p className="muted">
            No players have been observed yet. Enable the Stats API in Rocket League, then start a
            match or replay.
          </p>
        ) : filtered.length === 0 ? (
          <p className="muted">No players match &quot;{search}&quot;.</p>
        ) : (
          <div className="player-table">
            <div className="player-row player-row-header">
              <span>Name</span>
              <span>Status</span>
              <span>Last seen</span>
            </div>
            {filtered.map((player) => (
              <button
                key={player.primaryId}
                type="button"
                className="player-row player-row-button"
                onClick={() => setSelected(player)}
              >
                <strong>
                  {player.name}
                  {player.isMe ? <span className="chip chip-me">Me</span> : null}
                </strong>
                <span>
                  {player.showOnDashboard ? (
                    <span className="chip chip-tracked">Tracked</span>
                  ) : (
                    <span className="muted">—</span>
                  )}
                </span>
                <span>{formatLocalDate(player.lastSeenAt)}</span>
              </button>
            ))}
          </div>
        )}
      </section>

      <section className="card">
        <h2>Setup reminder</h2>
        <p className="muted">
          Rocket League must be restarted after enabling the Stats API. Set <code>PacketSendRate</code>
          {" "}above 0 in <code>TAGame\Config\DefaultStatsAPI.ini</code>. The default socket port is{" "}
          <code>49123</code>.
        </p>
      </section>

      {selected ? (
        <PlayerDetailsModal
          player={selected}
          meIsSet={meIsSet}
          refreshTick={refreshTick}
          onClose={() => setSelected(null)}
          onToggleTrack={async () => {
            await patchPlayer(selected.primaryId, { showOnDashboard: !selected.showOnDashboard });
            setSelected({ ...selected, showOnDashboard: !selected.showOnDashboard });
            refreshSelected();
          }}
          onSetMe={async () => {
            await patchPlayer(selected.primaryId, { isMe: true });
            setSelected({ ...selected, isMe: true });
            refreshSelected();
          }}
        />
      ) : null}
    </>
  );
}

function PlayerDetailsModal({
  player,
  meIsSet,
  refreshTick,
  onClose,
  onToggleTrack,
  onSetMe,
}: {
  player: ObservedPlayerView;
  meIsSet: boolean;
  refreshTick: number;
  onClose: () => void;
  onToggleTrack: () => Promise<void>;
  onSetMe: () => Promise<void>;
}) {
  const detailUrl = `/api/players/${encodeURIComponent(player.primaryId)}?_=${refreshTick}`;
  const { data } = usePoll<{ averages: PlayerAverages | null; headToHead: HeadToHeadRecord | null }>(
    detailUrl,
    10000,
  );

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-card" onClick={(e) => e.stopPropagation()}>
        <header className="modal-header">
          <div className="modal-title">
            <h2>{player.name}</h2>
            <div className="modal-chips">
              {player.isMe ? <span className="chip chip-me">Me</span> : null}
              {player.showOnDashboard ? <span className="chip chip-tracked">Tracked</span> : null}
            </div>
          </div>
          <button className="link-button" onClick={onClose}>
            Close
          </button>
        </header>

        <div className="modal-actions">
          <button
            className={`link-button ${player.showOnDashboard ? "accent-danger" : "accent-positive"}`}
            onClick={onToggleTrack}
          >
            {player.showOnDashboard ? "Untrack" : "Track"}
          </button>
          {!player.isMe && !meIsSet ? (
            <button className="link-button accent-info" onClick={onSetMe}>
              Set as me
            </button>
          ) : null}
          <span className="muted modal-meta">Last seen {formatLocalDate(player.lastSeenAt)}</span>
        </div>

        {data == null ? (
          <p className="muted">Loading…</p>
        ) : data.averages == null ? (
          <p className="muted">No completed matches recorded for this player yet.</p>
        ) : (
          <section className="modal-section">
            <h3>Career averages</h3>
            <div className="modal-stats-grid">
              <div><span className="muted">GP</span><strong>{data.averages.gamesPlayed}</strong></div>
              <div><span className="muted">MVP %</span><strong>{data.averages.mvpPercent.toFixed(0)}%</strong></div>
              <div><span className="muted">Score/g</span><strong>{data.averages.score.toFixed(1)}</strong></div>
              <div><span className="muted">Goals/g</span><strong>{data.averages.goals.toFixed(2)}</strong></div>
              <div><span className="muted">Assists/g</span><strong>{data.averages.assists.toFixed(2)}</strong></div>
              <div><span className="muted">Saves/g</span><strong>{data.averages.saves.toFixed(2)}</strong></div>
              <div><span className="muted">Shots/g</span><strong>{data.averages.shots.toFixed(2)}</strong></div>
              <div><span className="muted">Touches/g</span><strong>{data.averages.touches.toFixed(1)}</strong></div>
              <div><span className="muted">Demos/g</span><strong>{data.averages.demos.toFixed(2)}</strong></div>
              <div><span className="muted">Demoed/g</span><strong>{formatNullable(data.averages.demoed, 2)}</strong></div>
              <div><span className="muted">Speed (km/h)</span><strong>{formatNullable(data.averages.averageSpeedKph, 0)}</strong></div>
              <div><span className="muted">SS %</span><strong>{formatNullable(data.averages.supersonicPercent, 0, "%")}</strong></div>
              <div><span className="muted">Boost avg</span><strong>{data.averages.boost.toFixed(1)}</strong></div>
            </div>
          </section>
        )}

        {!player.showOnDashboard && !player.isMe && meIsSet && data ? (
          <section className="modal-section">
            <h3>Lifetime vs you</h3>
            {data.headToHead == null ? (
              <p className="muted">You haven&apos;t faced {player.name} in any recorded match.</p>
            ) : (
              <div className="modal-h2h">
                <strong className="record-text">
                  {data.headToHead.wins}-{data.headToHead.losses}
                </strong>
                <span className="muted">
                  over {data.headToHead.gamesPlayed}{" "}
                  {data.headToHead.gamesPlayed === 1 ? "game" : "games"}
                </span>
              </div>
            )}
          </section>
        ) : null}
      </div>
    </div>
  );
}

"use client";

import { usePoll } from "@/lib/use-poll";
import { formatLocalTime } from "@/lib/format";
import type { ConnectionPayload } from "@/lib/api-types";

export function ConnectionView() {
  const { data } = usePoll<ConnectionPayload>("/api/connection", 2000);

  const liveDetail = data?.liveUpdatedAt
    ? `Updated ${formatLocalTime(data.liveUpdatedAt)}`
    : "Waiting for UpdateState";

  return (
    <>
      <section className="page-header">
        <div>
          <p className="eyebrow">Local Stats API</p>
          <h1>Connection</h1>
          <p>Inspect the agent&apos;s status and the latest events it forwarded to the server.</p>
        </div>
      </section>

      <section className="grid">
        <SummaryCard title="Connection" value={data?.state ?? "—"} detail={liveDetail} />
        <SummaryCard
          title="Observed Players"
          value={String(data?.observedPlayerCount ?? 0)}
          detail="Collected from UpdateState"
        />
        <SummaryCard
          title="Tracked Players"
          value={String(data?.dashboardPlayerCount ?? 0)}
          detail="Enabled on Players screen"
        />
        <SummaryCard
          title="Last ingest"
          value={data?.lastIngestAt ? formatLocalTime(data.lastIngestAt) : "—"}
          detail="From local agent"
        />
      </section>

      {data?.lastError ? <section className="alert">{data.lastError}</section> : null}

      <section className="card">
        <div className="section-title">
          <h2>Recent events</h2>
          <span className="muted">Latest 10</span>
        </div>

        {(data?.events ?? []).length === 0 ? (
          <p className="muted">No Stats API messages have been logged yet.</p>
        ) : (
          <div className="match-list">
            {data!.events.map((entry) => (
              <article key={entry.id} className="match-row">
                <div>
                  <strong>{entry.eventName}</strong>
                  <span>{entry.matchGuid ?? "No match id"}</span>
                </div>
                <span>{formatLocalTime(entry.receivedAt)}</span>
              </article>
            ))}
          </div>
        )}
      </section>

      <section className="card">
        <h2>Setup reminder</h2>
        <p className="muted">
          Rocket League must be restarted after enabling the Stats API. Set{" "}
          <code>PacketSendRate</code> above 0 in <code>TAGame\Config\DefaultStatsAPI.ini</code>. The
          default socket port is <code>49123</code>.
        </p>
      </section>
    </>
  );
}

function SummaryCard({
  title,
  value,
  detail,
}: {
  title: string;
  value: string;
  detail?: string;
}) {
  return (
    <article className="stat-card">
      <span>{title}</span>
      <strong>{value}</strong>
      {detail ? <small>{detail}</small> : null}
    </article>
  );
}

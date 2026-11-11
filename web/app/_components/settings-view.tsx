"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { usePoll } from "@/lib/use-poll";
import type { ObservedPlayerView } from "@/lib/api-types";

export function SettingsView() {
  const { data: players } = usePoll<ObservedPlayerView[]>("/api/players", 5000, []);
  const [selected, setSelected] = useState<string>("");
  const [savedMessage, setSavedMessage] = useState<string | null>(null);

  const me = useMemo(() => (players ?? []).find((p) => p.isMe) ?? null, [players]);

  // Pre-fill the dropdown once. Doing it on every change would snap "None" straight back.
  const prefilled = useRef(false);

  useEffect(() => {
    if (!prefilled.current && me) {
      prefilled.current = true;
      setSelected(me.primaryId);
    }
  }, [me]);

  async function save() {
    const primaryId = selected.length === 0 ? null : selected;
    const response = await fetch(`/api/settings/me`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ primaryId }),
    });
    setSavedMessage(response.ok ? "Saved." : "Failed to save.");
    prefilled.current = true;
  }

  async function clearMe() {
    const response = await fetch(`/api/settings/me`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ primaryId: null }),
    });
    prefilled.current = true;
    setSelected("");
    setSavedMessage(response.ok ? "Cleared." : "Failed to clear.");
  }

  return (
    <>
      <section className="page-header">
        <div>
          <p className="eyebrow">Configuration</p>
          <h1>Settings</h1>
          <p>Pick which observed player is &quot;me&quot;. Used for streaks, head-to-head, and rematch summaries.</p>
        </div>
      </section>

      <section className="card">
        <div className="section-title">
          <h2>Me</h2>
          {me ? <span className="muted">Currently: {me.name}</span> : <span className="muted">No me set</span>}
        </div>

        <div className="form-grid">
          <label>
            <span>Player</span>
            <select className="input" value={selected} onChange={(e) => setSelected(e.target.value)}>
              <option value="">— None —</option>
              {(players ?? []).map((p) => (
                <option key={p.primaryId} value={p.primaryId}>
                  {p.name}
                </option>
              ))}
            </select>
          </label>
          <button type="button" className="primary-button" onClick={save}>
            Save
          </button>
          <button type="button" className="link-button" onClick={clearMe}>
            Clear
          </button>
        </div>

        {savedMessage ? <p className="me-status muted">{savedMessage}</p> : null}
      </section>

      <section className="card">
        <h2>Rocket League Stats API config</h2>
        <p className="muted">
          The Rocket League <code>DefaultStatsAPI.ini</code> file lives on the local machine, so it
          is now configured from the agent rather than the web UI.
        </p>
        <p className="muted">From the repo root, run:</p>
        <pre className="code-block">
{`dotnet run --project RocketStats.Agent -- \\
  --write-rl-config \"<path to>\\DefaultStatsAPI.ini\" 60 49123`}
        </pre>
        <p className="muted">
          Or hand-edit the file directly (set <code>PacketSendRate</code> above 0 and the{" "}
          <code>Port</code> the agent should listen on, default <code>49123</code>).
        </p>
      </section>
    </>
  );
}

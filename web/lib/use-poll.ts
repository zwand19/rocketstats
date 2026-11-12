"use client";

import { useEffect, useState } from "react";

/**
 * Polls `url`, waiting `intervalMs` between the end of one request and the start of the
 * next. Requests never overlap, so a slow response is not cancelled by the next tick, and
 * polling pauses while the tab is hidden.
 */
export function usePoll<T>(url: string, intervalMs: number = 2000, initial: T | null = null) {
  const [data, setData] = useState<T | null>(initial);
  const [error, setError] = useState<Error | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    let timer: ReturnType<typeof setTimeout> | null = null;
    let inFlight = false;

    async function tick() {
      timer = null;

      if (document.hidden) {
        return;
      }

      inFlight = true;

      try {
        const response = await fetch(url, { signal: controller.signal, cache: "no-store" });
        if (!response.ok) {
          throw new Error(`${response.status} ${response.statusText}`);
        }
        const json = (await response.json()) as T;
        setData(json);
        setError(null);
      } catch (err) {
        if (controller.signal.aborted) {
          return;
        }
        setError(err instanceof Error ? err : new Error(String(err)));
      } finally {
        inFlight = false;
      }

      if (!controller.signal.aborted) {
        timer = setTimeout(tick, intervalMs);
      }
    }

    function onVisibilityChange() {
      if (!document.hidden && !inFlight && timer == null) {
        tick();
      }
    }

    tick();
    document.addEventListener("visibilitychange", onVisibilityChange);

    return () => {
      controller.abort();
      if (timer != null) {
        clearTimeout(timer);
      }
      document.removeEventListener("visibilitychange", onVisibilityChange);
    };
  }, [url, intervalMs]);

  return { data, error };
}

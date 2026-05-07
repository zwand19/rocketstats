"use client";

import { useEffect, useRef, useState } from "react";

export function usePoll<T>(url: string, intervalMs: number = 2000, initial: T | null = null) {
  const [data, setData] = useState<T | null>(initial);
  const [error, setError] = useState<Error | null>(null);
  const aborter = useRef<AbortController | null>(null);

  useEffect(() => {
    let cancelled = false;

    async function fetchOnce() {
      aborter.current?.abort();
      const ac = new AbortController();
      aborter.current = ac;

      try {
        const response = await fetch(url, { signal: ac.signal, cache: "no-store" });
        if (!response.ok) {
          throw new Error(`${response.status} ${response.statusText}`);
        }
        const json = (await response.json()) as T;
        if (!cancelled) {
          setData(json);
          setError(null);
        }
      } catch (err) {
        if (cancelled || (err instanceof DOMException && err.name === "AbortError")) {
          return;
        }
        setError(err instanceof Error ? err : new Error(String(err)));
      }
    }

    fetchOnce();
    const id = setInterval(fetchOnce, intervalMs);

    return () => {
      cancelled = true;
      clearInterval(id);
      aborter.current?.abort();
    };
  }, [url, intervalMs]);

  return { data, error };
}

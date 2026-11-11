export function formatGameMode(playerCount: number): string {
  if (playerCount > 0 && playerCount % 2 === 0) {
    return `${playerCount / 2}v${playerCount / 2}`;
  }
  if (playerCount === 0) {
    return "Unknown";
  }
  return `${playerCount} players`;
}

export function teamClass(teamNum: number): string {
  if (teamNum === 0) {
    return "team-blue";
  }
  if (teamNum === 1) {
    return "team-orange";
  }
  return "team-unknown";
}

export function teamLabel(teamNum: number): string {
  if (teamNum === 0) {
    return "Blue";
  }
  if (teamNum === 1) {
    return "Orange";
  }
  return `Team ${teamNum}`;
}

export function formatNullable(value: number | null | undefined, fractionDigits: number, suffix = ""): string {
  if (value == null) {
    return "—";
  }
  return value.toFixed(fractionDigits) + suffix;
}

export function formatNumber(value: number, fractionDigits: number, suffix = ""): string {
  return value.toFixed(fractionDigits) + suffix;
}

export function formatTime(seconds: number): string {
  const m = Math.floor(seconds / 60);
  const s = Math.abs(seconds) % 60;
  return `${m}:${s.toString().padStart(2, "0")}`;
}

export function formatLocalDate(iso: string): string {
  const date = new Date(iso);
  return date.toLocaleString();
}

export function formatLocalTime(iso: string): string {
  const date = new Date(iso);
  return date.toLocaleTimeString();
}

export function formatShortDateTime(iso: string): string {
  return new Date(iso).toLocaleString(undefined, {
    month: "short",
    day: "numeric",
    hour: "numeric",
    minute: "2-digit",
  });
}

export function formatRelative(iso: string): string {
  const seconds = Math.max(0, Math.round((Date.now() - new Date(iso).getTime()) / 1000));
  if (seconds < 60) {
    return "just now";
  }
  const minutes = Math.round(seconds / 60);
  if (minutes < 60) {
    return `${minutes} min ago`;
  }
  const hours = Math.round(minutes / 60);
  if (hours < 24) {
    return `${hours} hr ago`;
  }
  const days = Math.round(hours / 24);
  return days < 30 ? `${days}d ago` : formatShortDateTime(iso);
}

export function initial(name: string): string {
  const match = name.match(/[\p{L}\p{N}]/u);
  return match ? match[0] : "?";
}

/** Stats API ids look like "Steam|7656…|0"; the first segment is the platform. */
export function platformLabel(primaryId: string): string | null {
  const [platform, rest] = primaryId.split("|");
  return rest != null && platform ? platform : null;
}

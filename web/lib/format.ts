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

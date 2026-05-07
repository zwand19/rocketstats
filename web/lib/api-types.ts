export type ObservedPlayerView = {
  primaryId: string;
  name: string;
  teamNum: number;
  firstSeenAt: string;
  lastSeenAt: string;
  showOnDashboard: boolean;
  isMe: boolean;
};

export type LivePlayerStatsView = {
  primaryId: string;
  name: string;
  teamNum: number;
  score: number;
  goals: number;
  shots: number;
  assists: number;
  saves: number;
  touches: number;
  demos: number;
  boost: number;
  updatedAt: string;
  speed: number;
  isSupersonic: boolean;
  hasCar: boolean;
};

export type LiveMatchStateView = {
  matchGuid: string | null;
  arena: string | null;
  timeSeconds: number;
  isOvertime: boolean;
  hasWinner: boolean;
  winner: string | null;
  players: LivePlayerStatsView[];
  updatedAt: string;
};

export type MatchSessionView = {
  id: string;
  startedAt: string;
  endedAt: string;
  gameMode: number;
  matchGuids: string[];
};

export type PlayerAverages = {
  primaryId: string;
  name: string;
  gamesPlayed: number;
  score: number;
  goals: number;
  assists: number;
  saves: number;
  shots: number;
  touches: number;
  demos: number;
  boost: number;
  mvps: number;
  mvpPercent: number;
  averageSpeedKph: number | null;
  supersonicPercent: number | null;
  demoed: number | null;
};

export type SessionAverages = {
  sessionId: string;
  primaryId: string;
  name: string;
  gamesPlayed: number;
  wins: number;
  losses: number;
  score: number;
  goals: number;
  assists: number;
  saves: number;
  shots: number;
  touches: number;
  demos: number;
  boost: number;
  mvps: number;
  mvpPercent: number;
  averageSpeedKph: number | null;
  supersonicPercent: number | null;
  demoed: number | null;
};

export type PlayerStreak = {
  primaryId: string;
  count: number;
  isWinning: boolean;
};

export type HeadToHeadRecord = {
  opponentId: string;
  name: string;
  wins: number;
  losses: number;
  gamesPlayed: number;
};

export type TeamRematchSummary = {
  gameMode: number;
  streakGames: number;
  streakWins: number;
  streakLosses: number;
  allTimeGames: number;
  allTimeWins: number;
  allTimeLosses: number;
  sessionGames: number;
  sessionWins: number;
  sessionLosses: number;
};

export type DashboardPayload = {
  live: LiveMatchStateView | null;
  dashboardPlayers: ObservedPlayerView[];
  mePrimaryId: string | null;
  meStreak: PlayerStreak | null;
  currentSession: MatchSessionView | null;
  sessionAverages: SessionAverages[];
  careerAverages: PlayerAverages[];
  careerForSessionCompare: PlayerAverages[];
  headToHead: HeadToHeadRecord[];
  rematch: TeamRematchSummary | null;
  selectedGameMode: number | null;
};

export type MatchSummaryPlayer = {
  primaryId: string;
  name: string;
  teamNum: number;
  score: number;
  goals: number;
  assists: number;
  saves: number;
};

export type MatchSummary = {
  matchGuid: string;
  endedAt: string;
  arena: string | null;
  gameMode: number;
  winningTeam: number | null;
  players: MatchSummaryPlayer[];
};

export type MatchesPage = {
  items: MatchSummary[];
  page: number;
  pageSize: number;
  totalCount: number;
};

export type EventLogEntryView = {
  id: string;
  eventName: string;
  matchGuid: string | null;
  receivedAt: string;
};

export type ConnectionPayload = {
  state: string;
  lastError: string | null;
  lastIngestAt: string | null;
  observedPlayerCount: number;
  dashboardPlayerCount: number;
  events: EventLogEntryView[];
  liveUpdatedAt: string | null;
};

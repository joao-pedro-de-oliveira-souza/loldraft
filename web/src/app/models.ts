export type Role = 'Top' | 'Jungle' | 'Mid' | 'Adc' | 'Support';
export const ROLES: Role[] = ['Top', 'Jungle', 'Mid', 'Adc', 'Support'];

export type Status = 'Registration' | 'CheckIn' | 'Drafting' | 'Drafted' | 'Running' | 'Finished' | 'Cancelled';

export interface Player {
  id: number;
  discordId: string;
  name: string;
  avatarUrl: string | null;
  primaryRole: Role;
  secondaryRole: Role | null;
  fill: boolean;
  riotId: string | null;
  riotVerified: boolean;
  elo: string | null;
  rating: number | null;
  isFake: boolean;
}

export interface Entry {
  id: number;
  player: Player;
  isCaptain: boolean;
  checkedIn: boolean;
  teamId: number | null;
  assignedRole: Role | null;
}

export interface Team {
  id: number;
  name: string;
  draftPosition: number;
  color: string;
  captainEntryId: number;
  memberEntryIds: number[];
  missingRoles: Role[];
  hasDiscordRole: boolean;
  avgElo: string | null;
}

export interface Pick {
  pickNumber: number;
  round: number;
  teamId: number;
  entryId: number;
  auto: boolean;
  at: string;
}

export interface Match {
  number: number;
  round: number;
  totalRounds: number;
  roundName: string;
  position: number;
  format: string;
  winsNeeded: number;
  teamAId: number | null;
  teamBId: number | null;
  winsA: number;
  winsB: number;
  winnerTeamId: number | null;
  isBye: boolean;
  isReady: boolean;
  isFinished: boolean;
}

export interface Tournament {
  id: number;
  name: string;
  status: Status;
  teamCount: number;
  teamSize: number;
  defaultFormat: string;
  semifinalFormat: string | null;
  finalFormat: string | null;
  pickTimerSeconds: number;
  enforceRoles: boolean;
  requireCheckIn: boolean;
  currentPickDeadline: string | null;
  serverNow: string;
  pickNumber: number;
  totalPicks: number;
  currentRound: number;
  currentTeamId: number | null;
  entries: Entry[];
  teams: Team[];
  picks: Pick[];
  matches: Match[];
  eligibleEntryIds: number[];
  championTeamId: number | null;
}

export interface Me {
  loginEnabled: boolean;
  authenticated: boolean;
  discordId: string | null;
  name: string | null;
  avatarUrl: string | null;
  isAdmin: boolean;
  player: Player | null;
}

export const ROLE_INFO: Record<Role, { label: string; color: string; short: string }> = {
  Top: { label: 'Top', color: '#4C7BD9', short: 'Top' },
  Jungle: { label: 'Jungle', color: '#2F9A5A', short: 'Jg' },
  Mid: { label: 'Mid', color: '#8A5CD6', short: 'Mid' },
  Adc: { label: 'ADC', color: '#D08A1C', short: 'Adc' },
  Support: { label: 'Support', color: '#D0568C', short: 'Sup' },
};

export const STATUS_LABEL: Record<Status, string> = {
  Registration: 'Inscrições abertas',
  CheckIn: 'Check-in aberto',
  Drafting: 'Draft em andamento',
  Drafted: 'Times definidos',
  Running: 'Partidas rolando',
  Finished: 'Encerrado',
  Cancelled: 'Cancelado',
};

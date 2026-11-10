import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { firstValueFrom } from 'rxjs';
import { Entry, Me, Player, Role, Team, Tournament } from './models';

export interface Toast {
  id: number;
  text: string;
  kind: 'info' | 'error' | 'pick';
}

/** Estado global do painel: usuário, campeonato atual e conexão em tempo real. */
@Injectable({ providedIn: 'root' })
export class Store {
  private http = inject(HttpClient);
  private hub?: signalR.HubConnection;
  private watching: number | null = null;
  private clockOffset = 0; // servidor - cliente (ms)
  private toastSeq = 0;

  readonly me = signal<Me | null>(null);
  readonly tournament = signal<Tournament | null>(null);
  readonly loaded = signal(false);
  readonly live = signal(false);
  readonly toasts = signal<Toast[]>([]);
  readonly now = signal(Date.now());
  /** Último pick, para o destaque visual no quadro. */
  readonly lastPickedEntryId = signal<number | null>(null);

  readonly entries = computed(() => new Map((this.tournament()?.entries ?? []).map((e) => [e.id, e])));
  readonly teams = computed(() => new Map((this.tournament()?.teams ?? []).map((t) => [t.id, t])));

  readonly myEntry = computed<Entry | undefined>(() => {
    const id = this.me()?.discordId;
    return id ? this.tournament()?.entries.find((e) => e.player.discordId === id) : undefined;
  });

  readonly currentTeam = computed<Team | undefined>(() => {
    const t = this.tournament();
    return t?.currentTeamId ? this.teams().get(t.currentTeamId) : undefined;
  });

  readonly isMyTurn = computed(() => {
    const team = this.currentTeam();
    const mine = this.myEntry();
    return !!team && !!mine && team.captainEntryId === mine.id;
  });

  readonly canPick = computed(
    () => this.tournament()?.status === 'Drafting' && (this.isMyTurn() || !!this.me()?.isAdmin),
  );

  readonly secondsLeft = computed(() => {
    const deadline = this.tournament()?.currentPickDeadline;
    if (!deadline) return null;
    return Math.max(0, Math.ceil((Date.parse(deadline) - (this.now() + this.clockOffset)) / 1000));
  });

  constructor() {
    setInterval(() => this.now.set(Date.now()), 250);
  }

  async init() {
    await Promise.all([this.loadMe(), this.loadCurrent()]);
    this.loaded.set(true);
    this.connect();
  }

  captainOf(team: Team): Entry | undefined {
    return this.entries().get(team.captainEntryId);
  }

  membersByRole(team: Team): Map<Role, Entry> {
    const map = new Map<Role, Entry>();
    for (const id of team.memberEntryIds) {
      const e = this.entries().get(id);
      if (e?.assignedRole) map.set(e.assignedRole, e);
    }
    return map;
  }

  isCaptainOfTeam(teamId: number | null): boolean {
    if (!teamId) return false;
    const team = this.teams().get(teamId);
    const mine = this.myEntry();
    return !!team && !!mine && team.captainEntryId === mine.id;
  }

  // ---------------- carga ----------------

  async loadMe() {
    try {
      this.me.set(await firstValueFrom(this.http.get<Me>('/api/me')));
    } catch {
      this.me.set({ loginEnabled: false, authenticated: false, discordId: null, name: null, avatarUrl: null, isAdmin: false, player: null });
    }
  }

  async loadCurrent() {
    try {
      const t = await firstValueFrom(this.http.get<Tournament | null>('/api/tournaments/current'));
      this.setTournament(t ?? null);
    } catch {
      this.setTournament(null);
    }
  }

  private setTournament(t: Tournament | null) {
    if (t) this.clockOffset = Date.parse(t.serverNow) - Date.now();
    this.tournament.set(t);
    this.watch(t?.id ?? null);
  }

  // ---------------- tempo real ----------------

  private connect() {
    this.hub = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/tournaments')
      .withAutomaticReconnect()
      .build();

    this.hub.on('tournamentUpdated', (t: Tournament, kind: string, message: string | null) => {
      if (this.tournament()?.id !== t.id) return;
      const before = this.tournament();
      this.setTournament(t);
      if (kind === 'pick' && t.picks.length > (before?.picks.length ?? 0)) {
        this.lastPickedEntryId.set(t.picks[t.picks.length - 1].entryId);
      }
      if (message) this.toast(message, kind === 'pick' ? 'pick' : 'info');
    });

    this.hub.on('tournamentsChanged', (id: number) => {
      if (this.tournament()?.id !== id) this.loadCurrent();
    });

    this.hub.onreconnected(() => {
      this.live.set(true);
      this.watching = null;
      this.loadCurrent(); // recarrega e volta a assistir
    });
    this.hub.onreconnecting(() => this.live.set(false));
    this.hub.onclose(() => this.live.set(false));

    this.hub
      .start()
      .then(() => {
        this.live.set(true);
        this.watching = null;
        this.watch(this.tournament()?.id ?? null);
      })
      .catch(() => this.live.set(false));
  }

  private watch(id: number | null) {
    if (!this.hub || this.hub.state !== signalR.HubConnectionState.Connected) {
      this.watching = null; // será refeito quando conectar
      return;
    }
    if (id === this.watching) return;
    if (this.watching !== null) this.hub.invoke('Unwatch', this.watching).catch(() => {});
    this.watching = id;
    if (id !== null) this.hub.invoke('Watch', id).catch(() => {});
  }

  // ---------------- ações ----------------

  private async post(url: string, body: unknown = {}, okMessage?: string): Promise<boolean> {
    try {
      await firstValueFrom(this.http.post(url, body));
      if (okMessage) this.toast(okMessage, 'info');
      return true;
    } catch (e) {
      this.toast(this.errorText(e), 'error');
      return false;
    }
  }

  private errorText(e: unknown): string {
    if (e instanceof HttpErrorResponse) {
      if (e.status === 401) return 'Entre com o Discord para fazer isso.';
      return e.error?.error ?? e.error?.detail ?? 'Não deu certo. Tente de novo.';
    }
    return 'Não deu certo. Tente de novo.';
  }

  private tid() {
    return this.tournament()?.id;
  }

  join = () => this.post(`/api/tournaments/${this.tid()}/join`, {}, 'Inscrição feita.');
  leave = () => this.post(`/api/tournaments/${this.tid()}/leave`, {}, 'Você saiu do campeonato.');
  checkIn = () => this.post(`/api/tournaments/${this.tid()}/checkin`, {}, 'Presença confirmada.');
  pick = (entryId: number) => this.post(`/api/tournaments/${this.tid()}/pick`, { entryId });
  autoPick = () => this.post(`/api/tournaments/${this.tid()}/autopick`);
  report = (matchNumber: number, winnerTeamId: number) =>
    this.post(`/api/tournaments/${this.tid()}/matches/${matchNumber}/report`, { winnerTeamId });

  setCaptain = (discordId: string, isCaptain: boolean) =>
    this.post(`/api/tournaments/${this.tid()}/admin/captain`, { discordId, isCaptain });
  openCheckIn = () => this.post(`/api/tournaments/${this.tid()}/admin/checkin-open`, {}, 'Check-in aberto.');
  startDraft = (shuffle: boolean) => this.post(`/api/tournaments/${this.tid()}/admin/start-draft`, { shuffle }, 'Draft iniciado.');
  balance = () => this.post(`/api/tournaments/${this.tid()}/admin/balance`, {}, 'Times montados por elo.');
  generateBracket = (shuffle: boolean) => this.post(`/api/tournaments/${this.tid()}/admin/bracket`, { shuffle }, 'Chave gerada.');

  async saveProfile(primaryRole: Role, secondaryRole: Role | null, fill: boolean): Promise<boolean> {
    try {
      const player = await firstValueFrom(this.http.put<Player>('/api/me/profile', { primaryRole, secondaryRole, fill }));
      const me = this.me();
      if (me) this.me.set({ ...me, player });
      this.toast('Perfil salvo.', 'info');
      return true;
    } catch (e) {
      this.toast(this.errorText(e), 'error');
      return false;
    }
  }

  async logout() {
    await firstValueFrom(this.http.post('/auth/logout', {})).catch(() => {});
    await this.loadMe();
  }

  login() {
    location.href = `/auth/login?returnUrl=${encodeURIComponent(location.pathname)}`;
  }

  toast(text: string, kind: Toast['kind'] = 'info') {
    const id = ++this.toastSeq;
    this.toasts.update((list) => [...list.slice(-3), { id, text, kind }]);
    setTimeout(() => this.toasts.update((list) => list.filter((t) => t.id !== id)), kind === 'error' ? 6000 : 3500);
  }
}

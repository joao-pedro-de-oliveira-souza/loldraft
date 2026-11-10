import { Component, computed, effect, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Entry, ROLE_INFO, ROLES, Role } from '../models';
import { Store } from '../store';
import { PlayerName, RoleTag } from '../ui';

@Component({
  selector: 'draft-page',
  imports: [RouterLink, PlayerName, RoleTag],
  template: `
    <section class="page">
      @if (store.tournament(); as t) {
        @if (t.teams.length === 0) {
          <div class="empty">
            <h2>O draft ainda não começou</h2>
            <p>Quando um admin iniciar, os times aparecem aqui e cada escolha chega na hora.
              Enquanto isso, veja os inscritos em <a routerLink="/">Campeonato</a>.</p>
          </div>
        } @else {
          <header class="head">
            @if (t.status === 'Drafting' && store.currentTeam(); as team) {
              <div class="turn">
                @if (store.isMyTurn()) { <span class="badge">Sua vez</span> }
                <h1>{{ store.isMyTurn() ? 'Escolha um jogador' : team.name + ' está escolhendo' }}</h1>
                <p class="sub">
                  Pick {{ t.pickNumber }} de {{ t.totalPicks }}
                  · falta {{ missingText(team.missingRoles) }}
                </p>
              </div>
              <div class="timer" [class.hurry]="(store.secondsLeft() ?? 99) <= 10">
                <span class="secs">{{ store.secondsLeft() ?? '–' }}</span><span class="unit">s</span>
              </div>
            } @else {
              <div class="turn">
                <h1>{{ t.status === 'Cancelled' ? 'Draft cancelado' : 'Times definidos' }}</h1>
                <p class="sub">{{ t.teams.length }} times · {{ t.picks.length }} escolhas
                  @if (t.matches.length) { · <a routerLink="/chave">ver a chave</a> }
                </p>
              </div>
            }
          </header>
          @if (t.status === 'Drafting') {
            <div class="track" [class.hurry]="(store.secondsLeft() ?? 99) <= 10"><i [style.width.%]="timePct()"></i></div>
          }

          <div class="layout" [class.solo]="t.status !== 'Drafting'">
            <div class="board card">
              <table [style.--n]="t.teams.length">
                <thead>
                  <tr>
                    <th class="rolecol"></th>
                    @for (team of t.teams; track team.id) {
                      <th [class.now]="team.id === t.currentTeamId">
                        <span class="tn"><i [style.background]="team.color"></i>{{ team.name }}</span>
                        @if (team.avgElo) { <span class="avg">média {{ team.avgElo }}</span> }
                      </th>
                    }
                  </tr>
                </thead>
                <tbody>
                  @for (role of roles; track role) {
                    <tr>
                      <th class="rolecol"><role-tag [role]="role" /></th>
                      @for (team of t.teams; track team.id) {
                        @let e = slots().get(team.id)?.get(role);
                        <td [class.now]="team.id === t.currentTeamId" [class.fresh]="e && e.id === store.lastPickedEntryId()">
                          @if (e) {
                            <player-name [player]="e.player" [captain]="e.id === team.captainEntryId" />
                          } @else if (team.id === t.currentTeamId && team.missingRoles.includes(role)) {
                            <span class="open">a escolher</span>
                          } @else {
                            <span class="blank">—</span>
                          }
                        </td>
                      }
                    </tr>
                  }
                </tbody>
              </table>
            </div>

            @if (t.status === 'Drafting') {
              <aside class="pool card">
                <div class="pool-head">
                  <div class="row1">
                    <h2>Disponíveis</h2>
                    <span class="muted">{{ pool().length }}</span>
                  </div>
                  <input type="search" placeholder="Buscar" [value]="query()" (input)="query.set($any($event.target).value)" />
                  <div class="seg" role="group" aria-label="Filtrar por role">
                    <button [class.on]="!roleFilter()" (click)="roleFilter.set(null)">Todas</button>
                    @for (r of roles; track r) {
                      <button [class.on]="roleFilter() === r" (click)="roleFilter.set(r)">{{ roleInfo[r].short }}</button>
                    }
                  </div>
                </div>
                <ul>
                  @for (e of filteredPool(); track e.id) {
                    @let ok = eligible().has(e.id);
                    <li [class.blocked]="!ok" [class.sel]="selected() === e.id">
                      <button class="item" [disabled]="!store.canPick() || !ok" (click)="toggle(e.id)"
                              [title]="ok ? '' : 'Não completa nenhuma role que falta no time da vez'">
                        <player-name [player]="e.player" />
                        <span class="meta">
                          <role-tag [role]="e.player.primaryRole" [short]="true" />
                          <span class="elo">{{ e.player.elo ?? '' }}</span>
                        </span>
                      </button>
                      @if (selected() === e.id && store.canPick()) {
                        <div class="confirm">
                          <span class="muted">
                            {{ roleInfo[e.player.primaryRole].label }}@if (e.player.secondaryRole) {, {{ roleInfo[e.player.secondaryRole].label }}}@if (e.player.fill) {, fill}
                          </span>
                          <button class="primary" (click)="confirm(e)">Escolher</button>
                        </div>
                      }
                    </li>
                  } @empty {
                    <li class="none muted">Ninguém encontrado.</li>
                  }
                </ul>
                @if (store.canPick()) {
                  <div class="pool-foot">
                    <button class="quiet" (click)="store.autoPick()">Escolher automaticamente</button>
                  </div>
                }
              </aside>
            }
          </div>

          @if (t.picks.length) {
            <section class="history">
              <h2>Escolhas</h2>
              <ol>
                @for (p of picksDesc(); track p.pickNumber) {
                  <li>
                    <span class="n muted">{{ p.pickNumber }}</span>
                    <span><i class="sq" [style.background]="store.teams().get(p.teamId)?.color"></i>{{ store.teams().get(p.teamId)?.name }}</span>
                    <span class="sub">{{ store.entries().get(p.entryId)?.player?.name }}</span>
                    @if (p.auto) { <span class="muted small">automático</span> }
                  </li>
                }
              </ol>
            </section>
          }
        }
      } @else {
        <div class="empty"><h2>Nenhum campeonato ativo</h2></div>
      }
    </section>
  `,
  styles: `
    .head { display: flex; align-items: flex-end; justify-content: space-between; gap: 2rem; }
    .turn { display: grid; gap: .4rem; min-width: 0; }
    .badge { justify-self: start; font-size: .78rem; font-weight: 600; color: var(--accent); background: var(--accent-soft); padding: .15rem .55rem; border-radius: 99px; }
    .timer { display: flex; align-items: baseline; gap: .15rem; color: var(--ink); }
    .secs { font-size: 2.6rem; font-weight: 600; letter-spacing: -0.03em; line-height: 1; }
    .unit { color: var(--muted); font-size: 1rem; }
    .timer.hurry .secs { color: var(--warn); }
    .track { height: 3px; background: var(--sunken); border-radius: 99px; overflow: hidden; margin: 1.1rem 0 0; }
    .track i { display: block; height: 100%; background: var(--accent); transition: width 250ms linear; }
    .track.hurry i { background: var(--warn); }

    .layout { display: grid; grid-template-columns: minmax(0, 1fr) 320px; gap: 1.25rem; align-items: start; margin-top: 1.5rem; }
    .layout.solo { grid-template-columns: 1fr; }

    .board { overflow-x: auto; }
    table { width: 100%; border-collapse: collapse; min-width: calc(110px + var(--n, 4) * 150px); }
    th, td { text-align: left; padding: .7rem .9rem; border-bottom: 1px solid var(--line); white-space: nowrap; }
    tbody tr:last-child th, tbody tr:last-child td { border-bottom: 0; }
    thead th { font-weight: 500; font-size: .9rem; color: var(--ink); }
    .rolecol { width: 110px; font-weight: 400; }
    .tn { display: inline-flex; align-items: center; gap: .5rem; }
    .avg { display: block; font-size: .75rem; font-weight: 400; color: var(--muted); margin-top: .1rem; }
    .tn i, .sq { width: 9px; height: 9px; border-radius: 3px; display: inline-block; flex: none; }
    .sq { margin-right: .45rem; }
    th.now, td.now { background: var(--accent-soft); }
    .open { color: var(--accent); font-size: .85rem; }
    .blank { color: var(--line-strong); }
    td.fresh { animation: fresh 1.4s ease-out; }
    @keyframes fresh { from { background: color-mix(in srgb, var(--accent) 22%, transparent); } }

    .pool { display: flex; flex-direction: column; max-height: calc(100vh - 140px); position: sticky; top: 76px; }
    .pool-head { padding: 1rem 1rem .75rem; display: grid; gap: .65rem; border-bottom: 1px solid var(--line); }
    .row1 { display: flex; justify-content: space-between; align-items: baseline; }
    .seg { display: flex; background: var(--sunken); border-radius: var(--radius-sm); padding: 3px; gap: 2px; }
    .seg button { flex: 1; border: 0; background: transparent; padding: .3rem 0; font-size: .82rem; color: var(--ink-2); border-radius: 6px; }
    .seg button.on { background: var(--surface); color: var(--ink); box-shadow: var(--shadow); }
    .pool ul { list-style: none; margin: 0; padding: .35rem; overflow-y: auto; }
    .item { width: 100%; display: flex; justify-content: space-between; align-items: center; gap: .6rem; text-align: left;
      border: 0; background: transparent; padding: .55rem .6rem; border-radius: var(--radius-sm); }
    .item:disabled { opacity: 1; cursor: default; }
    li.blocked .item { opacity: .38; }
    li.sel .item { background: var(--sunken); }
    .meta { display: flex; flex-direction: column; align-items: flex-end; gap: .05rem; flex: none; }
    .elo { color: var(--muted); font-size: .75rem; }
    .confirm { display: flex; justify-content: space-between; align-items: center; gap: .5rem; padding: .3rem .6rem .6rem; font-size: .85rem; }
    .none { padding: 1rem; }
    .pool-foot { border-top: 1px solid var(--line); padding: .5rem; display: flex; justify-content: center; }

    .history { margin-top: 2.5rem; max-width: 640px; }
    .history h2 { margin-bottom: .75rem; }
    .history ol { list-style: none; margin: 0; padding: 0; }
    .history li { display: grid; grid-template-columns: 2rem 1fr 1fr auto; gap: .75rem; padding: .45rem 0; border-bottom: 1px solid var(--line); align-items: center; }
    .n { text-align: right; }
    .small { font-size: .8rem; }

    @media (max-width: 980px) {
      .layout { grid-template-columns: 1fr; }
      .pool { position: static; max-height: none; }
    }
    @media (max-width: 600px) {
      .head { flex-direction: column; align-items: flex-start; gap: .75rem; }
      .history li { grid-template-columns: 1.5rem 1fr 1fr; }
    }
  `,
})
export class DraftPage {
  store = inject(Store);
  roles = ROLES;
  roleInfo = ROLE_INFO;

  query = signal('');
  roleFilter = signal<Role | null>(null);
  selected = signal<number | null>(null);

  constructor() {
    effect(() => {
      this.store.tournament()?.picks.length;
      this.selected.set(null);
    });
  }

  slots = computed(() => {
    const map = new Map<number, Map<Role, Entry>>();
    for (const team of this.store.tournament()?.teams ?? []) map.set(team.id, this.store.membersByRole(team));
    return map;
  });

  eligible = computed(() => new Set(this.store.tournament()?.eligibleEntryIds ?? []));

  pool = computed(() =>
    (this.store.tournament()?.entries ?? []).filter(
      (e) => !e.isCaptain && e.teamId === null && (e.checkedIn || !this.store.tournament()?.requireCheckIn),
    ),
  );

  filteredPool = computed(() => {
    const q = this.query().trim().toLowerCase();
    const role = this.roleFilter();
    const eligible = this.eligible();
    return this.pool()
      .filter((e) => !q || e.player.name.toLowerCase().includes(q))
      .filter((e) => !role || e.player.primaryRole === role || e.player.secondaryRole === role)
      .sort((a, b) => Number(eligible.has(b.id)) - Number(eligible.has(a.id)) || (b.player.rating ?? -1) - (a.player.rating ?? -1));
  });

  picksDesc = computed(() => [...(this.store.tournament()?.picks ?? [])].reverse());

  timePct = computed(() => {
    const t = this.store.tournament();
    const left = this.store.secondsLeft();
    if (!t || left === null) return 0;
    return Math.max(0, Math.min(100, (left / t.pickTimerSeconds) * 100));
  });

  missingText(roles: Role[]) {
    return roles.map((r) => ROLE_INFO[r].label).join(', ') || 'nada';
  }

  toggle(id: number) {
    this.selected.set(this.selected() === id ? null : id);
  }

  async confirm(e: Entry) {
    if (await this.store.pick(e.id)) this.selected.set(null);
  }
}

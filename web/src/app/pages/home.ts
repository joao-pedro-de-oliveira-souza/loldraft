import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Entry, ROLES, Role, STATUS_LABEL } from '../models';
import { Store } from '../store';
import { PlayerName, RoleTag } from '../ui';

@Component({
  selector: 'home-page',
  imports: [RouterLink, PlayerName, RoleTag],
  template: `
    <section class="page">
      @if (store.tournament(); as t) {
        <header class="head">
          <div class="title">
            <span class="status" [attr.data-s]="t.status">{{ statusLabel[t.status] }}</span>
            <h1>{{ t.name }}</h1>
            <p class="sub">
              {{ t.teamCount }} times de {{ t.teamSize }} · séries {{ t.defaultFormat }}@if (t.finalFormat) {, final {{ t.finalFormat }}} · {{ t.pickTimerSeconds }}s por pick
            </p>
          </div>
          <div class="progress">
            <p><b>{{ t.entries.length }}</b><span class="muted"> / {{ t.teamCount * t.teamSize }} jogadores</span></p>
            <div class="bar"><i [style.width.%]="fill()"></i></div>
            <p class="muted small">{{ captains() }} de {{ t.teamCount }} capitães definidos</p>
          </div>
        </header>

        <div class="actions">
          @switch (t.status) {
            @case ('Registration') {
              @if (!store.me()?.authenticated) {
                <p class="sub">Entre com o Discord para se inscrever.</p>
              } @else if (!store.me()?.player) {
                <p class="sub">Escolha suas roles em <a routerLink="!jogadores">Jogadores</a> para se inscrever.</p>
              } @else if (store.myEntry()) {
                <p class="ok">Você está inscrito</p>
                <button class="quiet" (click)="store.leave()">Cancelar inscrição</button>
              } @else {
                <button class="primary" (click)="store.join()">Inscrever-me</button>
              }
            }
            @case ('CheckIn') {
              @if (store.myEntry(); as mine) {
                @if (mine.checkedIn) { <p class="ok">Presença confirmada</p> }
                @else { <button class="primary" (click)="store.checkIn()">Confirmar presença</button> }
              } @else if (store.me()?.player) {
                <button (click)="store.join()">Inscrever-me</button>
              }
            }
            @case ('Drafting') { <a class="link" routerLink="/draft">Acompanhar o draft</a> }
            @case ('Drafted') { <a class="link" routerLink="/draft">Ver os times</a> }
            @case ('Running') { <a class="link" routerLink="/chave">Ver a chave</a> }
            @case ('Finished') { <a class="link" routerLink="/chave">Ver o campeão</a> }
          }

          @if (store.me()?.isAdmin) {
            <div class="admin">
              @if (t.status === 'Registration' && t.requireCheckIn) {
                <button (click)="store.openCheckIn()">Abrir check-in</button>
              }
              @if (t.status === 'Registration' || t.status === 'CheckIn') {
                <button (click)="store.balance()" title="Monta os times sem draft manual, equilibrando o elo">Balancear por elo</button>
                <button class="primary" (click)="store.startDraft(true)" [disabled]="captains() !== t.teamCount">Começar draft</button>
              }
              @if (t.status === 'Drafted') {
                <button class="primary" (click)="store.generateBracket(true)">Gerar chave</button>
              }
            </div>
          }
        </div>

        @if (canToggleCaptain()) {
          <p class="hint muted">Admin: clique num jogador para definir ou remover capitão.</p>
        }

        <div class="roster card">
          @for (role of roles; track role) {
            <section class="col">
              <h2><role-tag [role]="role" /><span class="muted">{{ byRole().get(role)?.length ?? 0 }}</span></h2>
              <ul>
                @for (e of byRole().get(role) ?? []; track e.id) {
                  <li [class.click]="canToggleCaptain()" (click)="toggleCaptain(e)"
                      [attr.role]="canToggleCaptain() ? 'button' : null" [attr.tabindex]="canToggleCaptain() ? 0 : null"
                      (keydown.enter)="toggleCaptain(e)">
                    <player-name [player]="e.player" [captain]="e.isCaptain" />
                    @if (t.status === 'CheckIn') {
                      <span class="dot" [class.yes]="e.checkedIn" [title]="e.checkedIn ? 'Confirmado' : 'Ainda não confirmou'"></span>
                    } @else if (e.player.secondaryRole) {
                      <span class="sec muted">{{ roleShort(e.player.secondaryRole) }}</span>
                    }
                  </li>
                } @empty {
                  <li class="none muted">—</li>
                }
              </ul>
            </section>
          }
        </div>
      } @else {
        <div class="empty">
          <h2>Nenhum campeonato ainda</h2>
          <p>Um admin cria o primeiro no Discord com <code>!camp criar</code>. Ele aparece aqui automaticamente.</p>
        </div>
      }
    </section>
  `,
  styles: `
    .head { display: flex; justify-content: space-between; align-items: flex-end; gap: 2rem; }
    .title { display: grid; gap: .45rem; }
    .status { font-size: .85rem; font-weight: 500; color: var(--accent); }
    .status[data-s='Cancelled'] { color: var(--warn); }
    .status[data-s='Finished'] { color: var(--ok); }
    .progress { width: 240px; display: grid; gap: .45rem; }
    .progress b { font-size: 1.4rem; font-weight: 600; }
    .bar { height: 4px; border-radius: 99px; background: var(--sunken); overflow: hidden; }
    .bar i { display: block; height: 100%; background: var(--accent); transition: width 300ms; }
    .small { font-size: .85rem; }

    .actions { display: flex; flex-wrap: wrap; align-items: center; gap: .75rem 1rem; margin: 1.75rem 0 1.5rem; min-height: 38px; }
    .ok { color: var(--ok); font-weight: 500; }
    .link { font-weight: 500; }
    .admin { margin-left: auto; display: flex; gap: .5rem; }
    .hint { font-size: .85rem; margin: -0.75rem 0 .75rem; }

    .roster { display: grid; grid-template-columns: repeat(5, minmax(0, 1fr)); }
    .col { padding: 1.1rem 1rem 1rem; min-width: 0; }
    .col + .col { border-left: 1px solid var(--line); }
    .col h2 { display: flex; justify-content: space-between; align-items: center; font-size: .9rem; font-weight: 500; margin-bottom: .75rem; }
    ul { list-style: none; margin: 0; padding: 0; display: grid; gap: .15rem; }
    li { display: flex; align-items: center; justify-content: space-between; gap: .5rem; padding: .35rem .4rem; margin: 0 -.4rem; border-radius: var(--radius-sm); min-width: 0; }
    li.click { cursor: pointer; }
    li.click:hover { background: var(--sunken); }
    .sec { font-size: .8rem; flex: none; }
    .dot { width: 8px; height: 8px; border-radius: 50%; border: 1.5px solid var(--line-strong); flex: none; }
    .dot.yes { background: var(--ok); border-color: var(--ok); }

    @media (max-width: 960px) {
      .roster { grid-template-columns: repeat(2, minmax(0, 1fr)); }
      .col + .col { border-left: 0; }
      .col { border-top: 1px solid var(--line); }
    }
    @media (max-width: 600px) {
      .head { flex-direction: column; align-items: stretch; }
      .progress { width: 100%; }
      .roster { grid-template-columns: 1fr; }
      .admin { margin-left: 0; }
    }
  `,
})
export class HomePage {
  store = inject(Store);
  roles = ROLES;
  statusLabel = STATUS_LABEL;

  byRole = computed(() => {
    const map = new Map<Role, Entry[]>();
    for (const e of this.store.tournament()?.entries ?? []) {
      const list = map.get(e.player.primaryRole) ?? [];
      list.push(e);
      map.set(e.player.primaryRole, list);
    }
    for (const list of map.values()) list.sort((a, b) => Number(b.isCaptain) - Number(a.isCaptain));
    return map;
  });

  captains = computed(() => this.store.tournament()?.entries.filter((e) => e.isCaptain).length ?? 0);

  fill = computed(() => {
    const t = this.store.tournament();
    return t ? Math.min(100, (t.entries.length / (t.teamCount * t.teamSize)) * 100) : 0;
  });

  canToggleCaptain = computed(() => {
    const s = this.store.tournament()?.status;
    return !!this.store.me()?.isAdmin && (s === 'Registration' || s === 'CheckIn');
  });

  roleShort(r: Role) {
    return { Top: 'Top', Jungle: 'Jg', Mid: 'Mid', Adc: 'Adc', Support: 'Sup' }[r];
  }

  toggleCaptain(e: Entry) {
    if (this.canToggleCaptain()) this.store.setCaptain(e.player.discordId, !e.isCaptain);
  }
}

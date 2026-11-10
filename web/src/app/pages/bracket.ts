import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Match, Team } from '../models';
import { Store } from '../store';
import { PlayerName } from '../ui';

@Component({
  selector: 'bracket-page',
  imports: [RouterLink, PlayerName],
  template: `
    <section class="page">
      @if (store.tournament(); as t) {
        <header class="head">
          <h1>Chave</h1>
          <p class="sub">{{ t.name }} · eliminação simples</p>
        </header>

        @if (champion(); as champ) {
          <section class="champ card">
            <p class="muted">Campeão</p>
            <h2><i [style.background]="champ.color"></i>{{ champ.name }}</h2>
            <div class="members">
              @for (id of champ.memberEntryIds; track id) {
                @if (store.entries().get(id); as e) { <player-name [player]="e.player" [captain]="id === champ.captainEntryId" /> }
              }
            </div>
          </section>
        }

        @if (t.matches.length === 0) {
          <div class="empty">
            @if (t.status === 'Drafted') {
              <h2>Os times estão prontos</h2>
              @if (store.me()?.isAdmin) {
                <p>Gere a chave para sortear os confrontos.</p>
                <p><button class="primary" (click)="store.generateBracket(true)">Gerar chave</button></p>
              } @else {
                <p>A chave aparece aqui quando um admin gerar.</p>
              }
            } @else {
              <h2>A chave aparece depois do draft</h2>
              <p><a routerLink="/draft">Ir para o draft</a></p>
            }
          </div>
        } @else {
          <div class="rounds" [style.--rounds]="rounds().length">
            @for (round of rounds(); track round.number) {
              <section class="round">
                <h3 class="muted">{{ round.name }}</h3>
                <div class="list">
                  @for (m of round.matches; track m.number) {
                    <article class="match card" [class.live]="m.isReady" [class.bye]="m.isBye">
                      @for (side of sides(m); track side.key) {
                        <div class="side" [class.win]="m.winnerTeamId && side.team?.id === m.winnerTeamId"
                             [class.lose]="m.winnerTeamId && side.team && side.team.id !== m.winnerTeamId">
                          <span class="tn">
                            @if (side.team) { <i [style.background]="side.team.color"></i> }
                            {{ side.team?.name ?? (m.isBye ? '—' : 'A definir') }}
                          </span>
                          @if (m.isReady && side.team && canReport(m)) {
                            <button class="quiet plus" (click)="store.report(m.number, side.team.id)"
                                    [attr.aria-label]="'Registrar vitória de ' + side.team.name + ' em um jogo'">+1</button>
                          }
                          @if (!m.isBye) { <span class="score">{{ side.wins }}</span> }
                        </div>
                      }
                      <footer>
                        <span>#{{ m.number }}</span>
                        <span>{{ m.isBye ? 'avança direto' : m.isReady ? m.format + ' · primeiro a ' + m.winsNeeded : m.format }}</span>
                      </footer>
                    </article>
                  }
                </div>
              </section>
            }
          </div>
          @if (anyReport()) {
            <p class="muted tip">Clique em +1 no time que venceu cada jogo da série.</p>
          }
        }
      } @else {
        <div class="empty"><h2>Nenhum campeonato ainda</h2></div>
      }
    </section>
  `,
  styles: `
    .head { display: grid; gap: .4rem; margin-bottom: 1.75rem; }
    .champ { padding: 1.25rem 1.4rem; margin-bottom: 2rem; display: grid; gap: .5rem; }
    .champ h2 { font-size: 1.5rem; display: flex; align-items: center; gap: .6rem; }
    .champ h2 i { width: 12px; height: 12px; border-radius: 4px; }
    .members { display: flex; flex-wrap: wrap; gap: .5rem 1.4rem; margin-top: .4rem; }

    .rounds { display: grid; grid-template-columns: repeat(var(--rounds), minmax(240px, 1fr)); gap: 2rem; overflow-x: auto; padding-bottom: 1rem; }
    .round { display: flex; flex-direction: column; }
    .round h3 { font-size: .85rem; font-weight: 500; margin-bottom: .9rem; }
    .list { flex: 1; display: flex; flex-direction: column; justify-content: space-around; gap: 1rem; }
    .match { overflow: hidden; }
    .match.live { border-color: var(--accent); }
    .match.bye { opacity: .55; box-shadow: none; }
    .side { display: flex; align-items: center; gap: .5rem; padding: .65rem .9rem; }
    .side + .side { border-top: 1px solid var(--line); }
    .tn { flex: 1; min-width: 0; display: flex; align-items: center; gap: .5rem; overflow: hidden; white-space: nowrap; text-overflow: ellipsis; }
    .tn i { width: 9px; height: 9px; border-radius: 3px; flex: none; }
    .score { font-weight: 600; font-size: 1.05rem; min-width: 1ch; text-align: right; }
    .side.win { font-weight: 600; }
    .side.lose { color: var(--muted); }
    .plus { padding: .15rem .5rem; font-size: .82rem; color: var(--accent); }
    footer { display: flex; justify-content: space-between; padding: .45rem .9rem; font-size: .78rem; color: var(--muted); background: var(--sunken); }
    .tip { margin-top: 1rem; font-size: .9rem; }
  `,
})
export class BracketPage {
  store = inject(Store);

  rounds = computed(() => {
    const matches = this.store.tournament()?.matches ?? [];
    const byRound = new Map<number, Match[]>();
    for (const m of matches) byRound.set(m.round, [...(byRound.get(m.round) ?? []), m]);
    return [...byRound.entries()]
      .sort(([a], [b]) => a - b)
      .map(([number, list]) => ({ number, name: list[0].roundName, matches: list.sort((a, b) => a.position - b.position) }));
  });

  champion = computed<Team | undefined>(() => {
    const id = this.store.tournament()?.championTeamId;
    return id ? this.store.teams().get(id) : undefined;
  });

  sides(m: Match) {
    return [
      { key: 'a', team: m.teamAId ? this.store.teams().get(m.teamAId) : undefined, wins: m.winsA },
      { key: 'b', team: m.teamBId ? this.store.teams().get(m.teamBId) : undefined, wins: m.winsB },
    ];
  }

  canReport(m: Match) {
    return !!this.store.me()?.isAdmin || this.store.isCaptainOfTeam(m.teamAId) || this.store.isCaptainOfTeam(m.teamBId);
  }

  anyReport = computed(() => (this.store.tournament()?.matches ?? []).some((m) => m.isReady && this.canReport(m)));
}

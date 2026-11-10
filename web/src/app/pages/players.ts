import { Component, computed, effect, inject, OnInit, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { Player, ROLE_INFO, ROLES, Role } from '../models';
import { Store } from '../store';
import { PlayerName, RoleTag } from '../ui';

@Component({
  selector: 'players-page',
  imports: [PlayerName, RoleTag],
  template: `
    <section class="page">
      <div class="split">
        <section class="profile card">
          <h2>Seu perfil</h2>
          @if (!store.me()?.authenticated) {
            <p class="sub">Entre com o Discord para escolher suas roles. No Discord, o mesmo cadastro é feito com <code>!registrar</code>.</p>
            @if (store.me()?.loginEnabled) { <button class="primary" (click)="store.login()">Entrar com Discord</button> }
          } @else {
            <div class="field">
              <label>Role principal</label>
              <div class="seg">
                @for (r of roles; track r) {
                  <button [class.on]="primary() === r" (click)="setPrimary(r)" [attr.aria-pressed]="primary() === r">{{ info[r].short }}</button>
                }
              </div>
            </div>

            <div class="field">
              <label>Secundária</label>
              <div class="seg">
                <button [class.on]="secondary() === null" (click)="secondary.set(null)" [attr.aria-pressed]="secondary() === null">—</button>
                @for (r of roles; track r) {
                  <button [class.on]="secondary() === r" [disabled]="r === primary()" (click)="secondary.set(r)" [attr.aria-pressed]="secondary() === r">{{ info[r].short }}</button>
                }
              </div>
            </div>

            <label class="check">
              <input type="checkbox" [checked]="fill()" (change)="fill.set($any($event.target).checked)" />
              Jogo em qualquer posição se precisar
            </label>

            <button class="primary" [disabled]="!primary() || saving()" (click)="save()">Salvar</button>

            <p class="muted small">
              @if (store.me()?.player?.riotId; as riot) {
                Conta Riot: {{ riot }}
              } @else {
                Vincule sua conta Riot no Discord com <code>!vincular Nome#TAG</code>.
              }
            </p>
          }
        </section>

        <section class="list">
          <header>
            <h1>Jogadores</h1>
            <p class="sub">{{ players().length }} cadastrados</p>
          </header>
          <div class="card">
            <table>
              <thead><tr><th>Jogador</th><th>Principal</th><th>Secundária</th><th class="r">Elo</th></tr></thead>
              <tbody>
                @for (p of sorted(); track p.id) {
                  <tr [class.me]="p.discordId === store.me()?.discordId">
                    <td><player-name [player]="p" /></td>
                    <td><role-tag [role]="p.primaryRole" /></td>
                    <td>
                      @if (p.secondaryRole) { <role-tag [role]="p.secondaryRole" [dim]="true" /> }
                      @else if (!p.fill) { <span class="muted">—</span> }
                      @if (p.fill) { <span class="muted small"> fill</span> }
                    </td>
                    <td class="r muted">{{ p.elo ?? '—' }}</td>
                  </tr>
                } @empty {
                  <tr><td colspan="4" class="muted">Ninguém cadastrado ainda.</td></tr>
                }
              </tbody>
            </table>
          </div>
        </section>
      </div>
    </section>
  `,
  styles: `
    .split { display: grid; grid-template-columns: minmax(0, 1fr) 320px; gap: 2rem; align-items: start; }
    .list { order: 1; display: grid; gap: 1.25rem; }
    .list header { display: grid; gap: .35rem; }
    .profile { order: 2; padding: 1.25rem; display: grid; gap: 1.1rem; position: sticky; top: 80px; }
    .field { display: grid; gap: .45rem; }
    .field label { font-size: .85rem; color: var(--ink-2); }
    .seg { display: flex; background: var(--sunken); border-radius: var(--radius-sm); padding: 3px; gap: 2px; }
    .seg button { flex: 1; border: 0; background: transparent; padding: .38rem 0; font-size: .85rem; color: var(--ink-2); border-radius: 6px; }
    .seg button.on { background: var(--surface); color: var(--ink); box-shadow: var(--shadow); }
    .check { display: flex; gap: .55rem; align-items: center; font-size: .9rem; }
    .small { font-size: .82rem; }

    table { width: 100%; border-collapse: collapse; }
    th { text-align: left; font-weight: 500; font-size: .82rem; color: var(--muted); padding: .7rem 1rem; border-bottom: 1px solid var(--line); }
    td { padding: .6rem 1rem; border-bottom: 1px solid var(--line); }
    tbody tr:last-child td { border-bottom: 0; }
    .r { text-align: right; }
    tr.me td { background: var(--accent-soft); }
    .ver { color: var(--ok); margin-left: .3rem; }

    @media (max-width: 880px) {
      .split { grid-template-columns: 1fr; }
      .profile { order: 0; position: static; }
    }
  `,
})
export class PlayersPage implements OnInit {
  store = inject(Store);
  private http = inject(HttpClient);
  roles = ROLES;
  info = ROLE_INFO;

  players = signal<Player[]>([]);
  primary = signal<Role | null>(null);
  secondary = signal<Role | null>(null);
  fill = signal(false);
  saving = signal(false);

  sorted = computed(() =>
    [...this.players()].sort((a, b) => ROLES.indexOf(a.primaryRole) - ROLES.indexOf(b.primaryRole) || a.name.localeCompare(b.name)),
  );

  constructor() {
    effect(() => {
      const p = this.store.me()?.player;
      if (!p) return;
      this.primary.set(p.primaryRole);
      this.secondary.set(p.secondaryRole);
      this.fill.set(p.fill);
    });
  }

  async ngOnInit() {
    await this.reload();
  }

  private async reload() {
    this.players.set(await firstValueFrom(this.http.get<Player[]>('/api/players')).catch(() => []));
  }

  setPrimary(r: Role) {
    this.primary.set(r);
    if (this.secondary() === r) this.secondary.set(null);
  }

  async save() {
    const p = this.primary();
    if (!p) return;
    this.saving.set(true);
    if (await this.store.saveProfile(p, this.secondary(), this.fill())) await this.reload();
    this.saving.set(false);
  }
}

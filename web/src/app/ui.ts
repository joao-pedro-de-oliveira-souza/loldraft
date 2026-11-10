import { Component, computed, input } from '@angular/core';
import { Player, ROLE_INFO, Role } from './models';

/** Role com um ponto discreto da cor da posição. */
@Component({
  selector: 'role-tag',
  template: `<span class="role" [class.dim]="dim()"><i [style.background]="info().color"></i>{{ short() ? info().short : info().label }}</span>`,
  styles: `
    .role { display: inline-flex; align-items: center; gap: .4rem; color: var(--ink-2); font-size: .85rem; white-space: nowrap; }
    i { width: 7px; height: 7px; border-radius: 50%; flex: none; }
    .dim { color: var(--muted); }
    .dim i { opacity: .55; }
  `,
})
export class RoleTag {
  role = input.required<Role>();
  short = input(false);
  dim = input(false);
  info = computed(() => ROLE_INFO[this.role()]);
}

/** Avatar + nome. Jogadores de teste mostram um robô no avatar. */
@Component({
  selector: 'player-name',
  template: `
    @if (player().avatarUrl) {
      <img class="av" [src]="player().avatarUrl" alt="" />
    } @else {
      <span class="av" aria-hidden="true">{{ player().isFake ? '🤖' : initial() }}</span>
    }
    <span class="name">{{ player().name }}</span>
    @if (captain()) { <span class="cap" title="Capitão">C</span> }
  `,
  styles: `
    :host { display: inline-flex; align-items: center; gap: .6rem; min-width: 0; }
    .av { width: 26px; height: 26px; border-radius: 50%; flex: none; object-fit: cover;
      display: grid; place-items: center; background: var(--sunken); color: var(--ink-2); font-size: .75rem; font-weight: 600; }
    .name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .cap { flex: none; font-size: .68rem; font-weight: 600; color: var(--accent); background: var(--accent-soft);
      width: 18px; height: 18px; border-radius: 5px; display: grid; place-items: center; }
  `,
})
export class PlayerName {
  player = input.required<Player>();
  captain = input(false);
  initial = computed(() => this.player().name.trim().charAt(0).toUpperCase());
}

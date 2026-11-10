import { Component, inject, OnInit } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Store } from './store';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <header class="bar">
      <div class="inner">
        <a class="brand" routerLink="/">LolDraft</a>
        <nav>
          <a routerLink="/" routerLinkActive="on" [routerLinkActiveOptions]="{ exact: true }">Campeonato</a>
          <a routerLink="/draft" routerLinkActive="on">Draft</a>
          <a routerLink="/chave" routerLinkActive="on">Chave</a>
          <a routerLink="/jogadores" routerLinkActive="on">Jogadores</a>
        </nav>
        <div class="right">
          <span class="live" [class.on]="store.live()" [title]="store.live() ? 'Conectado: atualiza sozinho' : 'Sem conexão em tempo real'"></span>
          @if (store.me(); as me) {
            @if (me.authenticated) {
              <span class="who">
                @if (me.avatarUrl) { <img [src]="me.avatarUrl" alt="" /> }
                <span>{{ me.name }}</span>
              </span>
              <button class="quiet" (click)="store.logout()">Sair</button>
            } @else if (me.loginEnabled) {
              <button class="primary" (click)="store.login()">Entrar com Discord</button>
            }
          }
        </div>
      </div>
    </header>

    <main>
      @if (store.loaded()) {
        <router-outlet />
      }
    </main>

    <div class="toasts" aria-live="polite">
      @for (t of store.toasts(); track t.id) {
        <div class="toast" [class.error]="t.kind === 'error'">{{ t.text }}</div>
      }
    </div>
  `,
  styles: `
    .bar { position: sticky; top: 0; z-index: 10; background: color-mix(in srgb, var(--bg) 85%, transparent);
      backdrop-filter: saturate(1.4) blur(10px); border-bottom: 1px solid var(--line); }
    .inner { max-width: 1160px; margin: 0 auto; padding: 0 1.5rem; height: 56px; display: flex; align-items: center; gap: 2rem; }
    .brand { font-weight: 650; font-size: 1.05rem; letter-spacing: -0.02em; color: var(--ink); }
    .brand:hover { text-decoration: none; }
    nav { display: flex; gap: 1.4rem; height: 100%; }
    nav a { display: flex; align-items: center; color: var(--muted); font-weight: 500; border-bottom: 2px solid transparent; margin-bottom: -1px; }
    nav a:hover { color: var(--ink); text-decoration: none; }
    nav a.on { color: var(--ink); border-bottom-color: var(--ink); }
    .right { margin-left: auto; display: flex; align-items: center; gap: .75rem; }
    .live { width: 7px; height: 7px; border-radius: 50%; background: var(--line-strong); }
    .live.on { background: var(--ok); }
    .who { display: flex; align-items: center; gap: .5rem; color: var(--ink-2); }
    .who img { width: 24px; height: 24px; border-radius: 50%; }

    .toasts { position: fixed; left: 50%; bottom: 1.25rem; transform: translateX(-50%); display: grid; gap: .4rem; z-index: 20; justify-items: center; }
    .toast { background: var(--ink); color: var(--bg); padding: .55rem .95rem; border-radius: 999px; font-size: .9rem; box-shadow: 0 6px 20px rgb(0 0 0 / 18%); animation: in 160ms ease-out; }
    .toast.error { background: var(--warn); color: #fff; }
    @keyframes in { from { transform: translateY(6px); opacity: 0; } }

    @media (max-width: 720px) {
      .inner { gap: 1rem; height: auto; padding: .7rem 1rem; flex-wrap: wrap; }
      nav { order: 3; width: 100%; overflow-x: auto; gap: 1.1rem; height: 34px; }
      .who span { display: none; }
    }
  `,
})
export class App implements OnInit {
  store = inject(Store);
  ngOnInit() {
    this.store.init();
  }
}

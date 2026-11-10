import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', loadComponent: () => import('./pages/home').then((m) => m.HomePage), title: 'Campeonato · LolDraft' },
  { path: 'draft', loadComponent: () => import('./pages/draft').then((m) => m.DraftPage), title: 'Draft · LolDraft' },
  { path: 'chave', loadComponent: () => import('./pages/bracket').then((m) => m.BracketPage), title: 'Chave · LolDraft' },
  { path: 'jogadores', loadComponent: () => import('./pages/players').then((m) => m.PlayersPage), title: 'Jogadores · LolDraft' },
  { path: '**', redirectTo: '' },
];

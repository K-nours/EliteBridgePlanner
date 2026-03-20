import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', loadComponent: () => import('./features/dashboard/dashboard.component').then(m => m.DashboardComponent) },
  { path: 'archive/legacy-broken', loadComponent: () => import('./features/dashboard/archive/dashboard-legacy-broken.component').then(m => m.DashboardLegacyBrokenComponent) },
];

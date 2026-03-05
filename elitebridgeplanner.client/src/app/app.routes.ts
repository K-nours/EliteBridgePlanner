import { Routes } from '@angular/router';
import { authGuard } from './core/auth/auth.guard';

export const routes: Routes = [
  {
    path: '',
    redirectTo: 'bridges',
    pathMatch: 'full'
  },
  {
    path: 'login',
    loadComponent: () =>
      import('./features/auth/login/login.component').then(m => m.LoginComponent)
  },
  {
    path: 'register',
    loadComponent: () =>
      import('./features/auth/register/register.component').then(m => m.RegisterComponent)
  },
  {
    path: 'bridges',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/bridge/bridge.component').then(m => m.BridgeComponent)
  },
  {
    path: '**',
    redirectTo: 'bridges'
  }
];

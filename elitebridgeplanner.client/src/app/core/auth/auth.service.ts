import { Injectable, signal, computed, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { tap } from 'rxjs/operators';
import { Observable } from 'rxjs';
import { AuthResponse, CurrentUser, LoginRequest, RegisterRequest } from '../models/models';

const AUTH_STORAGE_KEY = 'elite_bridge_auth';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  // Signal privé — état interne
  private readonly _currentUser = signal<CurrentUser | null>(this.loadStoredUser());

  // Signaux publics en lecture seule
  readonly currentUser = this._currentUser.asReadonly();
  readonly isAuthenticated = computed(() => {
    const user = this._currentUser();
    return user !== null && new Date(user.expiresAt) > new Date();
  });
  readonly commanderName = computed(() => this._currentUser()?.commanderName ?? '');
  readonly token = computed(() => this._currentUser()?.token ?? null);

  login(request: LoginRequest): Observable<AuthResponse> {
    return this.http.post<AuthResponse>('/api/auth/login', request).pipe(
      tap(response => this.storeUser(response))
    );
  }

  register(request: RegisterRequest): Observable<AuthResponse> {
    return this.http.post<AuthResponse>('/api/auth/register', request).pipe(
      tap(response => this.storeUser(response))
    );
  }

  logout(): void {
    localStorage.removeItem(AUTH_STORAGE_KEY);
    this._currentUser.set(null);
    this.router.navigate(['/login']);
  }

  private storeUser(response: AuthResponse): void {
    const user: CurrentUser = {
      token: response.token,
      commanderName: response.commanderName,
      email: response.email,
      preferredLanguage: response.preferredLanguage,
      preferredTimeZone: response.preferredTimeZone,
      expiresAt: new Date(response.expiresAt)
    };
    localStorage.setItem(AUTH_STORAGE_KEY, JSON.stringify(user));
    this._currentUser.set(user);
  }

  private loadStoredUser(): CurrentUser | null {
    try {
      const raw = localStorage.getItem(AUTH_STORAGE_KEY);
      if (!raw) return null;
      const user: CurrentUser = JSON.parse(raw);
      // Token expiré — ne pas charger
      if (new Date(user.expiresAt) <= new Date()) {
        localStorage.removeItem(AUTH_STORAGE_KEY);
        return null;
      }
      return user;
    } catch {
      return null;
    }
  }
}

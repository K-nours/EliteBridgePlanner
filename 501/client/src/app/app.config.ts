import { ApplicationConfig, provideBrowserGlobalErrorListeners, APP_INITIALIZER } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';

import { routes } from './app.routes';
import { CurrentGuildService } from './core/services/current-guild.service';
import { API_BASE_URL, resolveApiBaseUrl } from './core/config/api-base-url';
import { apiBaseUrlInterceptor } from './core/config/api-base-url.interceptor';
import { environment } from '../environments/environment';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideHttpClient(withInterceptors([apiBaseUrlInterceptor])),
    { provide: API_BASE_URL, useFactory: () => resolveApiBaseUrl(environment) },
    {
      provide: APP_INITIALIZER,
      useFactory: (currentGuild: CurrentGuildService) => () => currentGuild.loadAsync(),
      deps: [CurrentGuildService],
      multi: true,
    },
  ],
};

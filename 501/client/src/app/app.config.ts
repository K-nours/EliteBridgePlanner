import { ApplicationConfig, provideBrowserGlobalErrorListeners, APP_INITIALIZER } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';

import { routes } from './app.routes';
import { CurrentGuildService } from './core/services/current-guild.service';
import { API_BASE_URL, getApiBaseUrlFromWindow } from './core/config/api-base-url';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideHttpClient(),
    { provide: API_BASE_URL, useFactory: () => getApiBaseUrlFromWindow() },
    {
      provide: APP_INITIALIZER,
      useFactory: (currentGuild: CurrentGuildService) => () => currentGuild.loadAsync(),
      deps: [CurrentGuildService],
      multi: true,
    },
  ],
};

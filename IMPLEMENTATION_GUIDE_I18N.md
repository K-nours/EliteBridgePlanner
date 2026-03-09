# Guide Implémentation : Multilingue + Gestion des Dates (UTC → Local)

## 📋 Vue d'ensemble

Cette guide vous montre comment implémenter :
1. ✅ Détection automatique de la langue au 1er login
2. ✅ Changement de langue dynamique
3. ✅ Dates stockées en UTC, affichées en timezone local
4. ✅ Persistance des préférences utilisateur

---

## 🔧 BACK-END (.NET 10) 

### 1️⃣ Modifier le modèle `AppUser`

**Fichier :** `EliteBridgePlanner.Server\Models\AppUser.cs`

Ajouter :
```csharp
public string PreferredLanguage { get; set; } = "en-GB";
public string PreferredTimeZone { get; set; } = "UTC"; // Ex: "Europe/Paris", "America/New_York"
```

**Pourquoi ?**
- `PreferredLanguage` : Stocke la langue choisie par l'utilisateur
- `PreferredTimeZone` : Nécessaire pour convertir UTC → local (ex: 2025-03-05 10:00 UTC → 11:00 CET)

---

### 2️⃣ Créer une migration EF Core

**Fichier :** Terminal → `EliteBridgePlanner.Server`

```bash
dotnet ef migrations add AddLanguageAndTimeZonePreferences
dotnet ef database update
```

Cela génère :
```csharp
// Migrations/202503XX_AddLanguageAndTimeZonePreferences.cs
migrationBuilder.AddColumn<string>(
    name: "PreferredLanguage",
    table: "AspNetUsers",
    type: "nvarchar(10)",
    nullable: false,
    defaultValue: "en-GB");

migrationBuilder.AddColumn<string>(
    name: "PreferredTimeZone",
    table: "AspNetUsers",
    type: "nvarchar(100)",
    nullable: false,
    defaultValue: "UTC");
```

---

### 3️⃣ Modifier les DTOs

**Fichier :** `EliteBridgePlanner.Server\DTOs\Dtos.cs`

Ajouter/Modifier :
```csharp
// Pour la réponse d'auth (inclure les préférences)
public record AuthResponse(
    string Token,
    string CommanderName,
    string Email,
    string PreferredLanguage,    // 🆕
    string PreferredTimeZone,    // 🆕
    DateTime ExpiresAt
);

// Pour mettre à jour les préférences
public record UpdateUserPreferencesRequest(
    [MaxLength(10)] string? PreferredLanguage,  // null = pas de changement
    [MaxLength(100)] string? PreferredTimeZone   // null = pas de changement
);

// Pour récupérer les infos utilisateur
public record UserProfileDto(
    string Email,
    string CommanderName,
    string PreferredLanguage,
    string PreferredTimeZone,
    DateTime CreatedAt
);
```

---

### 4️⃣ Ajouter un contrôleur `UserController`

**Fichier :** `EliteBridgePlanner.Server\Controllers\UserController.cs` (CRÉER)

```csharp
using EliteBridgePlanner.Server.DTOs;
using EliteBridgePlanner.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace EliteBridgePlanner.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UserController : ControllerBase
{
    private readonly UserManager<AppUser> _userManager;

    public UserController(UserManager<AppUser> userManager)
    {
        _userManager = userManager;
    }

    /// <summary>Récupère le profil de l'utilisateur actuel</summary>
    [HttpGet("profile")]
    [ProducesResponseType<UserProfileDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProfile()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        return Ok(new UserProfileDto(
            user.Email!,
            user.CommanderName,
            user.PreferredLanguage,
            user.PreferredTimeZone,
            user.CreatedAt
        ));
    }

    /// <summary>Met à jour les préférences de l'utilisateur (langue, timezone)</summary>
    [HttpPut("preferences")]
    [ProducesResponseType<UserProfileDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdatePreferences([FromBody] UpdateUserPreferencesRequest request)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        // Validation : vérifier que la langue est autorisée
        var allowedLanguages = new[] { "en-GB", "fr-FR" };
        if (request.PreferredLanguage is not null && !allowedLanguages.Contains(request.PreferredLanguage))
            return BadRequest("Language not supported");

        // Validation : vérifier que la timezone est valide
        if (request.PreferredTimeZone is not null)
        {
            try
            {
                TimeZoneInfo.FindSystemTimeZoneById(request.PreferredTimeZone);
            }
            catch
            {
                return BadRequest("Invalid time zone");
            }
        }

        if (request.PreferredLanguage is not null)
            user.PreferredLanguage = request.PreferredLanguage;

        if (request.PreferredTimeZone is not null)
            user.PreferredTimeZone = request.PreferredTimeZone;

        await _userManager.UpdateAsync(user);

        return Ok(new UserProfileDto(
            user.Email!,
            user.CommanderName,
            user.PreferredLanguage,
            user.PreferredTimeZone,
            user.CreatedAt
        ));
    }
}
```

---

### 5️⃣ Modifier `AuthService` pour inclure les préférences

**Fichier :** `EliteBridgePlanner.Server\Services\AuthService.cs`

Chercher la méthode `LoginAsync` et `RegisterAsync`, modifier le return pour inclure :
```csharp
return new AuthResponse(
    token,
    user.CommanderName,
    user.Email!,
    user.PreferredLanguage,    // 🆕
    user.PreferredTimeZone,    // 🆕
    expiresAt
);
```

---

### 6️⃣ Ajouter un Middleware pour les headers i18n

**Fichier :** `EliteBridgePlanner.Server\Middleware\LocalizationMiddleware.cs` (CRÉER)

```csharp
using System.Globalization;
using EliteBridgePlanner.Server.Models;
using Microsoft.AspNetCore.Identity;

namespace EliteBridgePlanner.Server.Middleware;

/// <summary>
/// Middleware qui applique la culture et timezone de l'utilisateur
/// basée sur ses préférences ou le header Accept-Language
/// </summary>
public class LocalizationMiddleware
{
    private readonly RequestDelegate _next;

    public LocalizationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, UserManager<AppUser> userManager)
    {
        var user = context.User.Identity?.IsAuthenticated == true
            ? await userManager.GetUserAsync(context.User)
            : null;

        // Déterminer la culture (langue)
        var culture = user?.PreferredLanguage ?? ExtractLanguageFromHeader(context) ?? "en-GB";

        // Déterminer la timezone
        var timeZone = user?.PreferredTimeZone ?? "UTC";

        CultureInfo.CurrentCulture = new CultureInfo(culture);
        CultureInfo.CurrentUICulture = new CultureInfo(culture);

        // Stocker dans HttpContext.Items pour usage dans les contrôleurs
        context.Items["Culture"] = culture;
        context.Items["TimeZone"] = timeZone;
        context.Items["UserTimeZoneInfo"] = TimeZoneInfo.FindSystemTimeZoneById(timeZone);

        await _next(context);
    }

    private static string? ExtractLanguageFromHeader(HttpContext context)
    {
        var acceptLanguage = context.Request.Headers["Accept-Language"].ToString();
        if (string.IsNullOrEmpty(acceptLanguage))
            return null;

        // Extraire "fr" de "fr-FR,fr;q=0.9"
        var language = acceptLanguage.Split(',')[0].Split('-')[0];
        return language.ToLower() switch
        {
            "fr" => "fr-FR",
            "en" => "en-GB",
            _ => "en-GB"
        };
    }
}
```

Enregistrer le middleware dans `Program.cs` :
```csharp
app.UseMiddleware<LocalizationMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
```

---

### 7️⃣ Helper pour conversion de dates UTC → Local

**Fichier :** `EliteBridgePlanner.Server\Utils\DateTimeHelper.cs` (CRÉER)

```csharp
namespace EliteBridgePlanner.Server.Utils;

/// <summary>
/// Utilitaire pour gérer les conversions de dates UTC ↔ Local
/// </summary>
public static class DateTimeHelper
{
    /// <summary>Convertit une date UTC en format local pour un utilisateur</summary>
    public static DateTime ConvertToUserTimeZone(DateTime utcDateTime, TimeZoneInfo userTimeZone)
    {
        if (utcDateTime.Kind != DateTimeKind.Utc)
            utcDateTime = DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);

        return TimeZoneInfo.ConvertTime(utcDateTime, TimeZoneInfo.Utc, userTimeZone);
    }

    /// <summary>Convertit une date locale en UTC</summary>
    public static DateTime ConvertToUtc(DateTime localDateTime, TimeZoneInfo userTimeZone)
    {
        if (localDateTime.Kind == DateTimeKind.Utc)
            return localDateTime;

        var utc = TimeZoneInfo.ConvertTime(localDateTime, userTimeZone, TimeZoneInfo.Utc);
        return DateTime.SpecifyKind(utc, DateTimeKind.Utc);
    }

    /// <summary>Format standardisé pour les retours API (ISO 8601)</summary>
    public static string FormatForApi(DateTime dateTime)
    {
        return dateTime.ToString("o"); // "2025-03-05T10:00:00Z"
    }
}
```

---

## 🎨 FRONT-END (Angular)

### 8️⃣ Installer ngx-translate

**Terminal :**
```bash
cd elitebridgeplanner.client
npm install @ngx-translate/core @ngx-translate/http-loader
```

Ajouter au `package.json` :
```json
"@ngx-translate/core": "^16.0.0",
"@ngx-translate/http-loader": "^16.0.0"
```

---

### 9️⃣ Créer les fichiers de ressources i18n

**Fichier :** `elitebridgeplanner.client\src\assets\i18n\en-GB.json` (CRÉER)

```json
{
  "app.title": "Elite Bridge Planner",
  "nav.home": "Home",
  "nav.bridges": "Bridges",
  "nav.profile": "Profile",
  "nav.language": "Language",
  "nav.logout": "Logout",
  
  "auth.login": "Login",
  "auth.register": "Register",
  "auth.email": "Email",
  "auth.password": "Password",
  "auth.commanderName": "Commander Name",
  "auth.rememberMe": "Remember me",
  
  "bridge.name": "Bridge Name",
  "bridge.description": "Description",
  "bridge.createDate": "Created on",
  "bridge.add": "Add Bridge",
  "bridge.edit": "Edit Bridge",
  
  "system.name": "System Name",
  "system.type": "Type",
  "system.status": "Status",
  "system.architect": "Architect",
  "system.createdAt": "Created",
  "system.updatedAt": "Updated",
  
  "status.PLANIFIE": "Planned",
  "status.CONSTRUCTION": "In Progress",
  "status.FINI": "Completed",
  
  "type.DEBUT": "Start",
  "type.PILE": "Pillar",
  "type.TABLIER": "Deck",
  "type.FIN": "End",
  
  "error.required": "This field is required",
  "error.minLength": "Minimum length: {{min}}",
  "error.email": "Invalid email address",
  "error.http": "Server error: {{status}}",
  "error.unauthorized": "Unauthorized",
  "error.notFound": "Not found",
  
  "button.save": "Save",
  "button.cancel": "Cancel",
  "button.delete": "Delete",
  "button.edit": "Edit",
  "button.add": "Add",
  "button.close": "Close"
}
```

**Fichier :** `elitebridgeplanner.client\src\assets\i18n\fr-FR.json` (CRÉER)

```json
{
  "app.title": "Planificateur de Ponts Elite",
  "nav.home": "Accueil",
  "nav.bridges": "Ponts",
  "nav.profile": "Profil",
  "nav.language": "Langue",
  "nav.logout": "Déconnexion",
  
  "auth.login": "Connexion",
  "auth.register": "Inscription",
  "auth.email": "Email",
  "auth.password": "Mot de passe",
  "auth.commanderName": "Nom du commandant",
  "auth.rememberMe": "Se souvenir de moi",
  
  "bridge.name": "Nom du pont",
  "bridge.description": "Description",
  "bridge.createDate": "Créé le",
  "bridge.add": "Ajouter un pont",
  "bridge.edit": "Modifier le pont",
  
  "system.name": "Nom du système",
  "system.type": "Type",
  "system.status": "Statut",
  "system.architect": "Architecte",
  "system.createdAt": "Créé",
  "system.updatedAt": "Modifié",
  
  "status.PLANIFIE": "Planifié",
  "status.CONSTRUCTION": "En construction",
  "status.FINI": "Terminé",
  
  "type.DEBUT": "Début",
  "type.PILE": "Pile",
  "type.TABLIER": "Tablier",
  "type.FIN": "Fin",
  
  "error.required": "Ce champ est obligatoire",
  "error.minLength": "Longueur minimale : {{min}}",
  "error.email": "Adresse email invalide",
  "error.http": "Erreur serveur : {{status}}",
  "error.unauthorized": "Non autorisé",
  "error.notFound": "Non trouvé",
  
  "button.save": "Enregistrer",
  "button.cancel": "Annuler",
  "button.delete": "Supprimer",
  "button.edit": "Modifier",
  "button.add": "Ajouter",
  "button.close": "Fermer"
}
```

---

### 🔟 Créer `LanguageService`

**Fichier :** `elitebridgeplanner.client\src\app\core\services\language.service.ts` (CRÉER)

```typescript
import { Injectable, signal, effect, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { TranslateService } from '@ngx-translate/core';
import { Observable } from 'rxjs';
import { tap } from 'rxjs/operators';

export interface UserPreferences {
  preferredLanguage: string;
  preferredTimeZone: string;
}

const LANGUAGE_STORAGE_KEY = 'elite_bridge_language';
const TIMEZONE_STORAGE_KEY = 'elite_bridge_timezone';

@Injectable({ providedIn: 'root' })
export class LanguageService {
  private readonly http = inject(HttpClient);
  private readonly translate = inject(TranslateService);

  // Signaux pour la langue et timezone
  private readonly _language = signal<string>(this.detectInitialLanguage());
  private readonly _timeZone = signal<string>(this.getInitialTimeZone());

  readonly language = this._language.asReadonly();
  readonly timeZone = this._timeZone.asReadonly();

  constructor() {
    // Observer : chaque fois que la langue change, met à jour le traducteur
    effect(() => {
      const lang = this._language();
      this.translate.use(lang);
      localStorage.setItem(LANGUAGE_STORAGE_KEY, lang);
    });

    // Observer : chaque fois que la timezone change, stocke-la
    effect(() => {
      const tz = this._timeZone();
      localStorage.setItem(TIMEZONE_STORAGE_KEY, tz);
    });
  }

  /**
   * Détecte la langue initiale :
   * 1. localStorage (si existant)
   * 2. navigateur (si fr ou en)
   * 3. défaut: en-GB
   */
  private detectInitialLanguage(): string {
    // Déjà sauvegardée ?
    const saved = localStorage.getItem(LANGUAGE_STORAGE_KEY);
    if (saved) return saved;

    // Vérifier le navigateur
    const browserLang = navigator.language; // ex: "fr-FR", "en-US"
    if (browserLang.startsWith('fr')) return 'fr-FR';
    if (browserLang.startsWith('en')) return 'en-GB';

    return 'en-GB'; // Fallback
  }

  /**
   * Détecte la timezone initiale
   * Utilise Intl pour déterminer la timezone du navigateur
   */
  private getInitialTimeZone(): string {
    const saved = localStorage.getItem(TIMEZONE_STORAGE_KEY);
    if (saved) return saved;

    try {
      // Obtenir la timezone système
      const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone;
      return timeZone || 'UTC';
    } catch {
      return 'UTC';
    }
  }

  /**
   * Définit la langue et l'envoie au serveur
   */
  setLanguage(language: string): Observable<any> {
    this._language.set(language);
    return this.http.put('/api/user/preferences', {
      preferredLanguage: language,
      preferredTimeZone: null // On ne change que la langue
    });
  }

  /**
   * Définit la timezone et l'envoie au serveur
   */
  setTimeZone(timeZone: string): Observable<any> {
    this._timeZone.set(timeZone);
    return this.http.put('/api/user/preferences', {
      preferredLanguage: null, // On ne change que la timezone
      preferredTimeZone: timeZone
    });
  }

  /**
   * Met à jour les deux préférences
   */
  setPreferences(language: string, timeZone: string): Observable<any> {
    this._language.set(language);
    this._timeZone.set(timeZone);
    return this.http.put('/api/user/preferences', {
      preferredLanguage: language,
      preferredTimeZone: timeZone
    });
  }
}
```

---

### 1️⃣1️⃣ Créer un pipe pour formater les dates

**Fichier :** `elitebridgeplanner.client\src\app\core\pipes\local-date.pipe.ts` (CRÉER)

```typescript
import { Pipe, PipeTransform, inject } from '@angular/core';
import { LanguageService } from '../services/language.service';

@Pipe({
  name: 'localDate',
  standalone: true
})
export class LocalDatePipe implements PipeTransform {
  private readonly languageService = inject(LanguageService);

  /**
   * Transforme une date UTC en format local
   * 
   * Utilisation dans le template :
   * {{ system.updatedAt | localDate:'short' }}
   * {{ system.createdAt | localDate:'full' }}
   * 
   * Format options:
   * - 'short'   : 05/03/2025 10:30
   * - 'medium'  : 5 Mar 2025 10:30:45
   * - 'long'    : 5 March 2025 at 10:30:45 UTC+1
   * - 'full'    : Wednesday, 5 March 2025 at 10:30:45 Central European Time
   */
  transform(value: string | Date | null, format: string = 'short'): string {
    if (!value) return '';

    const date = typeof value === 'string' ? new Date(value) : value;
    if (isNaN(date.getTime())) return '';

    const lang = this.languageService.language();
    const locale = lang === 'fr-FR' ? 'fr-FR' : 'en-GB';

    // Note: Les navigateurs convertissent automatiquement UTC → timezone locale
    // Donc new Date("2025-03-05T10:00:00Z") affiche déjà l'heure locale
    return new Intl.DateTimeFormat(locale, this.getFormatOptions(format)).format(date);
  }

  private getFormatOptions(format: string): Intl.DateTimeFormatOptions {
    switch (format) {
      case 'short':
        return {
          year: 'numeric',
          month: '2-digit',
          day: '2-digit',
          hour: '2-digit',
          minute: '2-digit'
        };
      case 'medium':
        return {
          year: 'numeric',
          month: 'short',
          day: 'numeric',
          hour: '2-digit',
          minute: '2-digit',
          second: '2-digit'
        };
      case 'long':
        return {
          year: 'numeric',
          month: 'long',
          day: 'numeric',
          hour: '2-digit',
          minute: '2-digit',
          second: '2-digit',
          timeZoneName: 'short'
        };
      case 'full':
        return {
          weekday: 'long',
          year: 'numeric',
          month: 'long',
          day: 'numeric',
          hour: '2-digit',
          minute: '2-digit',
          second: '2-digit',
          timeZoneName: 'long'
        };
      default:
        return {};
    }
  }
}
```

---

### 1️⃣2️⃣ Configurer app.config.ts ou app.module.ts

**Fichier :** `elitebridgeplanner.client\src\app.config.ts` (ou `main.ts`)

```typescript
import { ApplicationConfig, importProvidersFrom } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withXsrfConfiguration } from '@angular/common/http';
import { TranslateModule, TranslateLoader } from '@ngx-translate/core';
import { TranslateHttpLoader } from '@ngx-translate/http-loader';
import { HttpClient } from '@angular/common/http';
import { routes } from './app.routes';

export function HttpLoaderFactory(http: HttpClient) {
  return new TranslateHttpLoader(http, './assets/i18n/', '.json');
}

export const appConfig: ApplicationConfig = {
  providers: [
    provideRouter(routes),
    provideHttpClient(
      withXsrfConfiguration({
        cookieName: 'X-CSRF-TOKEN',
        headerName: 'X-CSRF-TOKEN'
      })
    ),
    importProvidersFrom(
      TranslateModule.forRoot({
        defaultLanguage: 'en-GB',
        loader: {
          provide: TranslateLoader,
          useFactory: HttpLoaderFactory,
          deps: [HttpClient]
        }
      })
    )
  ]
};
```

---

### 1️⃣3️⃣ Modifier `AuthService` pour inclure les préférences

**Fichier :** `elitebridgeplanner.client\src\app\core\auth\auth.service.ts`

Modifier `CurrentUser` :
```typescript
export interface CurrentUser {
  token: string;
  commanderName: string;
  email: string;
  preferredLanguage: string;  // 🆕
  preferredTimeZone: string;  // 🆕
  expiresAt: Date;
}

// Dans storeUser():
private storeUser(response: AuthResponse): void {
  const user: CurrentUser = {
    token: response.token,
    commanderName: response.commanderName,
    email: response.email,
    preferredLanguage: response.preferredLanguage,  // 🆕
    preferredTimeZone: response.preferredTimeZone,  // 🆕
    expiresAt: new Date(response.expiresAt)
  };
  
  localStorage.setItem(AUTH_STORAGE_KEY, JSON.stringify(user));
  this._currentUser.set(user);
}
```

Et initialiser la langue/timezone depuis les préférences :
```typescript
constructor(private languageService: LanguageService) {
  // Après le login, appliquer les préférences
  effect(() => {
    const user = this._currentUser();
    if (user) {
      this.languageService.setLanguage(user.preferredLanguage);
      this.languageService.setTimeZone(user.preferredTimeZone);
    }
  });
}
```

---

### 1️⃣4️⃣ Utiliser dans les templates

**Exemple dans un composant :**

```typescript
// system-detail.component.ts
import { Component, inject } from '@angular/core';
import { TranslateModule } from '@ngx-translate/core';
import { LocalDatePipe } from '@app/core/pipes/local-date.pipe';

@Component({
  selector: 'app-system-detail',
  templateUrl: './system-detail.component.html',
  imports: [TranslateModule, LocalDatePipe],
  standalone: true
})
export class SystemDetailComponent {
  system = {
    id: 1,
    name: 'Sol',
    createdAt: '2025-03-05T10:00:00Z', // UTC depuis le serveur
    updatedAt: '2025-03-06T14:30:00Z'
  };
}
```

```html
<!-- system-detail.component.html -->
<div>
  <h2>{{ 'system.name' | translate }}: {{ system.name }}</h2>
  <p>{{ 'system.createdAt' | translate }}: {{ system.createdAt | localDate:'short' }}</p>
  <p>{{ 'system.updatedAt' | translate }}: {{ system.updatedAt | localDate:'medium' }}</p>
</div>
```

---

### 1️⃣5️⃣ Composant pour le sélecteur de langue

**Fichier :** `elitebridgeplanner.client\src\app\shared\components\language-selector\language-selector.component.ts` (CRÉER)

```typescript
import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TranslateModule } from '@ngx-translate/core';
import { LanguageService } from '@app/core/services/language.service';

@Component({
  selector: 'app-language-selector',
  standalone: true,
  imports: [CommonModule, TranslateModule],
  template: `
    <select 
      [value]="languageService.language()" 
      (change)="onLanguageChange($event)"
      class="language-select"
      [attr.aria-label]="'nav.language' | translate">
      <option value="en-GB">English</option>
      <option value="fr-FR">Français</option>
    </select>
  `,
  styles: [`
    .language-select {
      padding: 0.5rem;
      border-radius: 4px;
      border: 1px solid #ccc;
      cursor: pointer;
    }
  `]
})
export class LanguageSelectorComponent {
  protected languageService = inject(LanguageService);

  onLanguageChange(event: Event): void {
    const language = (event.target as HTMLSelectElement).value;
    this.languageService.setLanguage(language).subscribe();
  }
}
```

---

## 📊 Résumé des fichiers à créer/modifier

### BACK-END (.NET)
| Fichier | Action | Description |
|---------|--------|-------------|
| `Models/AppUser.cs` | Modifier | Ajouter `PreferredLanguage`, `PreferredTimeZone` |
| `Controllers/UserController.cs` | Créer | GET/PUT pour préférences utilisateur |
| `Middleware/LocalizationMiddleware.cs` | Créer | Appliquer culture/timezone au contexte |
| `Utils/DateTimeHelper.cs` | Créer | Convertir UTC ↔ Local |
| `DTOs/Dtos.cs` | Modifier | Ajouter `AuthResponse`, `UserProfileDto`, `UpdateUserPreferencesRequest` |
| `Services/AuthService.cs` | Modifier | Inclure langues/timezones dans la réponse |
| `Program.cs` | Modifier | Enregistrer le middleware |
| Migration EF Core | Créer | `AddLanguageAndTimeZonePreferences` |

### FRONT-END (Angular)
| Fichier | Action | Description |
|---------|--------|-------------|
| `package.json` | Modifier | Ajouter `@ngx-translate/core`, `@ngx-translate/http-loader` |
| `assets/i18n/en-GB.json` | Créer | Ressources anglaises |
| `assets/i18n/fr-FR.json` | Créer | Ressources françaises |
| `core/services/language.service.ts` | Créer | Gestion langue + détection automatique |
| `core/pipes/local-date.pipe.ts` | Créer | Formatage des dates en timezone local |
| `shared/components/language-selector/` | Créer | Sélecteur de langue |
| `core/auth/auth.service.ts` | Modifier | Inclure préférences, initialiser langue |
| `app.config.ts` ou `main.ts` | Modifier | Configurer TranslateModule |

---

## 🧪 Tests suggérés

### Back-end (Unit)
- ✅ `UserController.GetProfile_ReturnsUserPreferences`
- ✅ `UserController.UpdatePreferences_ValidLanguage_Updates`
- ✅ `UserController.UpdatePreferences_InvalidLanguage_Returns400`
- ✅ `DateTimeHelper.ConvertToUserTimeZone_UTC_ReturnsLocalTime`
- ✅ `LocalizationMiddleware_AppliesCultureFromUser`

### Front-end
- ✅ `LanguageService.detectInitialLanguage_BrowserFrench_ReturnsFrFR`
- ✅ `LanguageService.setLanguage_UpdatesLocalStorage`
- ✅ `LocalDatePipe.transform_UTCDate_FormatsInLocalTimeZone`

---

## 🚀 Ordre d'implémentation recommandé

1. ✅ Migrer AppUser (ajouter colonnes)
2. ✅ Créer DTOs, UserController, DateTimeHelper
3. ✅ Modifier AuthService, ajouter Middleware
4. ✅ Installer ngx-translate côté front
5. ✅ Créer fichiers i18n (en-GB.json, fr-FR.json)
6. ✅ Implémenter LanguageService
7. ✅ Créer LocalDatePipe
8. ✅ Configurer app.config.ts / TranslateModule
9. ✅ Ajouter LanguageSelectorComponent
10. ✅ Tester bout en bout

---

## ⚠️ Points critiques

1. **Toujours stocker les dates en UTC** dans la BD (utiliser `DateTime.UtcNow`)
2. **Header Accept-Language** doit être envoyé sur chaque requête
3. **localStorage** est le fallback si l'utilisateur n'est pas authentifié
4. **Timezones valides** : utiliser `TimeZoneInfo.GetSystemTimeZones()` pour valider
5. **i18n des dates** : utiliser `Intl.DateTimeFormat` ou le pipe customisé

---

Voulez-vous que je commence par implémenter ces fichiers dans votre solution ? 🚀

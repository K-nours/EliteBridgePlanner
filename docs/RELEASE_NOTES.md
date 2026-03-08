# Release Notes — Branche `update-graphique` vs `master`

## Vue d'ensemble

Cette version apporte des améliorations majeures en termes d'accessibilité, d'expérience utilisateur, de lisibilité graphique et de fiabilité de l'application. Plus de 1 600 lignes ajoutées, 38 fichiers modifiés.

---

## Nouvelles fonctionnalités

### Composant Custom Select
- **Remplacement des `<select>` natifs** par un menu déroulant personnalisé et stylé
- Style aligné sur le profile menu (fond sombre, bordures, ombre)
- Utilisé pour : Type et Statut (formulaire d'ajout), Type (formulaire d’édition)
- **Accessibilité complète** : navigation clavier (Enter, Espace, flèches, Échap, Home, End), ARIA (combobox, aria-expanded, aria-activedescendant)
- Réutilisable pour tous les futurs selects de l’application

### Profile Menu
- **Menu profil** à la place du simple bouton déconnexion : avatar, dropdown
- Sélecteur de langue **FR / EN** (style cockpit, comme le toggle Tous/En cours)
- Bouton **DÉCONNEXION** intégré au menu
- Service i18n pour le changement de langue

### Script de lancement
- **`start-dev.sh`** : lance backend .NET + frontend Angular en une commande
- Libération des ports 4200 et 7293 avant démarrage
- Frontend en HTTP pour éviter les problèmes de certificat SSL
- Script **`start:http`** dans `package.json`

### Internationalisation
- Service `LanguageService` pour FR / EN
- Préparation pour les futures traductions

---

## Corrections de bugs

### Ajout de système
- **loadFirstBridge()** : charge le premier pont disponible au lieu de l’id 1 codé en dur → corrige l’erreur FK `BridgeId` quand le pont 1 n’existe pas
- **addSystem** : remplacement de `rxMethod` par un `subscribe()` direct → le bouton AJOUTER fonctionne correctement
- **insertAtIndex** : utilisation de `length + 1` au lieu de `length`, reset à 1 → évite les erreurs de validation côté backend

### Custom Select
- **value** passé en signal → le label affiché se met à jour correctement lors d’un changement de valeur
- Focus conservé sur le trigger → sélection par Entrée fiable au clavier

### Backend
- **CORS** : ajout des origines `localhost:4200` et `127.0.0.1:4200` (http et https) pour le développement

---

## Améliorations UI / UX

### Affichage des erreurs
- **Bannière d’erreur** en haut du layout bridge pour les erreurs API
- Bouton **×** pour fermer la bannière
- Extraction des messages backend via `getErrorMessage()` (HttpErrorResponse)

### Visualiseur du pont
- **Mise en page** : 8 px au-dessus, 16 px en dessous de la ligne de métro
- Types Départ/Arrivée : ronds pleins, bordures et fonds ajustés
- Labels : numéros `#order` sous les noms
- Pastilles vertes pour les systèmes opérationnels (FINI)

### Détail du système
- **Bordure supérieure** : dégradé reprenant la couleur du type (comme la bordure gauche de la carte sélectionnée)
- Labels « Départ » / « Arrivée » à la place de « Début » / « Fin »

### Cartes système (sidebar)
- Segments **fictifs** de statut pour la continuité visuelle (flèches)
- Style des badges de statut (Planifié, En construction, Opérationnel)
- **Toggle « Tous / En cours »** (style cockpit) pour masquer les systèmes opérationnels

### Formulaire d’ajout
- **Espacement** : 16 px entre les lignes, 8 px au-dessus du formulaire, 8 px sous le bouton AJOUTER
- Indication de l’état invalide du formulaire

---

## Accessibilité

### Thèmes
- **Contrastes améliorés** : `--status-plan`, `--text-dim`, `--status-done`, `--color-fin` éclaircis
- Ratio de contraste visé ~ 4,5:1 (WCAG AA)
- **Focus clavier** : outline visible pour input, button, select

### Rapport
- Document `RAPPORT_ACCESSIBILITE_THEMES.md` dans `/docs`

---

## Outils et directives

### TruncateTooltipDirective
- **Tooltip unifié** : remplacement des tooltips natifs par une directive réutilisable
- Affichage des noms complets sur survol (noms tronqués, architecte, etc.)
- Classe `.eb-tooltip-floating` pour le style global

### TruncateMiddlePipe
- Troncature des noms au milieu (ex. `Colonia Gat…way`)
- Utilisé dans le visualiseur, la sidebar et le détail

### Mixins et variables
- `btn-critical` : style pour déconnexion et suppression
- Ajustements des variables de thème (`_theme-blue`, `_theme-orange`, `_theme-green-red`)

---

## Autres

### Données
- Système **« Colonia »** renommé en **« Colonia Gateway »** dans le DataSeeder

### Documentation
- Fichier `.cursor/rules/truncated-text-tooltip.mdc` pour la règle tooltip
- Mise à jour de `docs/ai-context.md`

### Nettoyage
- Suppression du rapport d’accessibilité (puis recréation dans `/docs`)
- Nettoyage du `package-lock.json` (peer deps, entrées inutilisées)

---

## Fichiers impactés (principaux)

| Fichier | Changements |
|---------|-------------|
| `bridge.store.ts` | loadFirstBridge, addSystem, getErrorMessage, clearError |
| `bridge-visualizer` | Styles nœuds, espacement, pastilles vertes |
| `system-list` | Custom select, formulaire, toggle Tous/En cours, cartes |
| `system-detail` | Custom select, bordure dégradé, styles |
| `custom-select` | **Nouveau** |
| `profile-menu` | **Nouveau** |
| `truncate-tooltip.directive` | **Nouveau** |
| `language.service` | **Nouveau** |
| `start-dev.sh` | **Nouveau** |

---

*Généré à partir des commits `master..update-graphique`*

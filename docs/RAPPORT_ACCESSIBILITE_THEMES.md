# Rapport d'accessibilité — Thèmes EliteBridgePlanner

**Norme de référence :** WCAG 2.1 niveau AA  
**Seuils :** Texte normal ≥ 4.5:1 | Texte grand ≥ 3:1 | Composants UI ≥ 3:1

---

## 1. Synthèse des contrastes par thème

| Combinaison | Orange | Bleu | Vert | Rouge |
|-------------|--------|------|------|-------|
| **Texte principal / fond** | ✓ 14.6:1 | ✓ 14.1:1 | ✓ 15.9:1 | ✓ 13.3:1 |
| **Texte secondaire (dim) / fond** | ✓ 5.0:1 | ~ 4.5:1 | ✓ 5.9:1 | ~ 3.9:1 |
| **Accent / fond** | ✓ 8.5:1 | ✓ 11.3:1 | ✓ 14.7:1 | ✓ 5.7:1 |
| **Status PLANIFIÉ / fond** | ✗ 2.5:1 | ~ 3.6:1 | ~ 3.1:1 | ✗ 1.9:1 |
| **Status CONSTRUCTION / fond** | ✓ 10.3:1 | ✓ 8.6:1 | ✓ 16.0:1 | ✓ 6.9:1 |
| **Status FINI / fond** | ✓ 9.2:1 | ✓ 9.4:1 | ✓ 14.9:1 | ~ 4.1:1 |
| **Color-fin (rouge) / fond** | ✓ 5.6:1 | ✓ 5.7:1 | ✓ 5.6:1 | ~ 3.5:1 |
| **Bouton inversé (bg sur accent)** | ✓ 8.5:1 | ✓ 11.3:1 | ✓ 14.7:1 | ✓ 5.7:1 |

**Légende :** ✓ Conforme AA | ~ Limite / à surveiller | ✗ Non conforme

---

## 2. Problèmes identifiés

### 2.1 Thème Rouge — Problèmes critiques

- **`--text-dim`** (#9a5a6a) : contraste ~3.9:1 sur fond — **en dessous du seuil 4.5:1** pour le texte secondaire.
- **`--status-plan`** (#6a2a3a) : contraste 1.9:1 — **très insuffisant** pour tout texte.
- **`--status-done`** (#cc3366) : contraste ~4.1:1 — limite pour texte normal.
- **`--color-fin`** (#cc0033) : contraste ~3.5:1 — insuffisant pour erreurs/alertes.

### 2.2 Thème Orange — Problème modéré

- **`--status-plan`** (#6a4a2a) : contraste 2.5:1 — **insuffisant** pour le texte des badges « Planifié ».

### 2.3 Thèmes Bleu et Vert — Points de vigilance

- **`--text-dim`** (bleu) : ~4.5:1 — à la limite du seuil.
- **`--status-plan`** : 3.1 à 3.6:1 — en dessous de 4.5:1 pour du texte.

### 2.4 Focus clavier

- `outline: none` est utilisé sur plusieurs champs et boutons.
- Le focus repose uniquement sur `border-color: var(--accent)`.
- Risque de **perte de visibilité** du focus si la bordure est fine ou peu contrastée.

### 2.5 Badges — Couleurs sémantiques (inchangées)

Les couleurs des badges (DEBUT, FIN, PILE, TABLIER, PLANIFIÉ, CONSTRUCTION, FINI) ont une signification métier. Elles ne sont pas modifiées dans le cadre de ce rapport.

---

## 3. Suggestions d'amélioration

### 3.1 Thème Rouge — Ajustements prioritaires

```scss
// Avant
--text-dim    : #9a5a6a;
--status-plan : #6a2a3a;
--status-done : #cc3366;
--color-fin   : #cc0033;

// Proposition (contrastes améliorés)
--text-dim    : #b87a8a;   // Plus clair → ~5:1
--status-plan : #8a4a5a;   // Plus clair → ~4.5:1
--status-done : #e64d7a;   // Légèrement plus clair → ~5:1
--color-fin   : #ff4466;   // Plus vif → ~4.5:1
```

### 3.2 Thème Orange — Status planifié

```scss
// Avant
--status-plan : #6a4a2a;

// Proposition
--status-plan : #8a6a3a;   // Ou #9a7a4a → ~4.5:1
```

### 3.3 Thème Bleu — Texte secondaire

```scss
// Avant
--text-dim : #5a7a9a;

// Proposition (optionnel, pour plus de marge)
--text-dim : #6a8aaa;   // ~5:1
```

### 3.4 Focus clavier — Indicateur plus visible

```scss
// Remplacer outline: none par un focus visible
input:focus, button:focus, select:focus {
  outline: none;
  border-color: var(--accent);
  box-shadow: 0 0 0 2px var(--bg), 0 0 0 4px var(--accent);  // Anneau focus
}

// Ou utiliser :focus-visible pour ne pas affecter le clic souris
input:focus-visible, button:focus-visible {
  outline: 2px solid var(--accent);
  outline-offset: 2px;
}
```

### 3.5 Vérifications recommandées

1. **Taille de police** : s'assurer que le texte < 18px respecte bien 4.5:1.
2. **États désactivés** : vérifier le contraste des boutons/liens désactivés.
3. **Placeholder** : `--text-dim` en placeholder — valider sur tous les thèmes.
4. **Messages d'erreur** : `--color-fin` doit rester lisible partout.

---

## 4. Résumé des actions

| Priorité | Action |
|----------|--------|
| Haute | Corriger `--status-plan` et `--text-dim` dans le thème Rouge |
| Haute | Corriger `--status-plan` dans le thème Orange |
| Moyenne | Renforcer l'indicateur de focus clavier |
| Basse | Ajuster `--text-dim` dans le thème Bleu si besoin |

---

*Rapport généré le 7 mars 2026 — Calculs de contraste selon WCAG 2.1*

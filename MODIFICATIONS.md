# 📋 Modifications - Architecture BridgeStarSystem

## 🎯 Vue d'ensemble
Refactorisation complète du modèle de données pour supporter :
1. **Coordonnées des systèmes stellaires** (X, Y, Z)
2. **Systèmes dans plusieurs ponts** avec rôles différents
3. **Nom unique des systèmes** comme clé de recherche

## 📊 Changements clés

### 1. **Entité de jonction : `BridgeStarSystem`**
   - **Nouveau fichier** : `EliteBridgePlanner.Server\Models\BridgeStarSystem.cs`
   - Relie un `StarSystem` à un `Bridge` avec :
     - `Type` (le rôle dans **ce pont spécifique**)
     - `Status` (l'état dans **ce pont spécifique**)
     - `PreviousSystemId` (chaînage intra-pont)

### 2. **Modèle `StarSystem` révisé**
   - ❌ Supprimé : `Type`, `Status`, `BridgeId`, `PreviousSystemId`
   - ✅ Ajouté : `X`, `Y`, `Z` (coordonnées)
   - ✅ Ajouté : relation `ICollection<BridgeStarSystem>`
   - 📌 `Name` : index unique pour recherche rapide

### 3. **Modèle `Bridge` révisé**
   - `Systems` : passe de `ICollection<StarSystem>` à `ICollection<BridgeStarSystem>`

### 4. **DTOs actualisés**
   - `StarSystemDto` : ajout de X, Y, Z
   - `CreateSystemRequest` : X?, Y?, Z? (optionnels)
   - `UpdateSystemRequest` : X?, Y?, Z?

### 5. **BridgeService refactorisé**
   - Travaille désormais avec `BridgeStarSystem` au lieu de `StarSystem`
   - `AddSystemAsync` : crée ou réutilise le `StarSystem`, puis crée l'association
   - `UpdateSystemAsync` : met à jour le `StarSystem` (properties communes)
   - Gestion de chaîne : via `BridgeStarSystem.PreviousSystemId`

### 6. **AppDbContext mise à jour**
   - Configuration de l'entité `BridgeStarSystem`
   - Clé composite : `(BridgeId, StarSystemId)`
   - Index unique sur `StarSystem.Name`
   - Cascade delete pour les ponts

### 7. **Migration de base de données**
   - **Nouvelle migration** : `AddBridgeStarSystemJunction`
   - Crée la table `BridgeStarSystems`
   - Refactorise les colonnes de `StarSystems`

### 8. **DataSeeder actualisé**
   - Crée les `StarSystem` indépendamment
   - Crée les `BridgeStarSystem` avec rôles spécifiques
   - Chaîne via `PreviousSystemId` de la jonction

### 9. **Tests unitaires (28 tests, tous passants)**
   - `BridgeServiceTests` : 13 tests
   - `BridgesControllerTests` : 6 tests
   - `SystemsControllerTests` : 5 tests
   - `AuthControllerTests` : 3 tests + 1 supplémentaire
   - Couverture : AddSystem, UpdateSystem, DeleteSystem, MoveSystem, intégration

## 🔄 Cas d'usage couvert

### Cas 1 : Un système dans plusieurs ponts
```csharp
// Sol comme DÉBUT du Pont A
var sol1 = new BridgeStarSystem { BridgeId = 1, StarSystemId = 1, Type = DEBUT };

// Sol comme PILE du Pont B
var sol2 = new BridgeStarSystem { BridgeId = 2, StarSystemId = 1, Type = PILE };
```

### Cas 2 : Recherche par nom unique
```csharp
var sol = await _db.StarSystems
    .FirstOrDefaultAsync(s => s.Name == "Sol");
```

### Cas 3 : Coordonnées et architecte globaux
```csharp
var sol = new StarSystem
{
    Name = "Sol",
    X = 0, Y = 0, Z = 0,
    ArchitectId = "user-1"  // Global pour le système
};
```

## ✅ Validations

- ✅ Build complet : succès
- ✅ Migration EF Core : succès
- ✅ Tests unitaires : 28/28 passants
- ✅ Logique métier : préservée et améliorée
- ✅ Contraintes d'intégrité : appliquées

## 📝 Notes de déploiement

1. Exécuter la migration : `dotnet ef database update`
2. Vérifier les associations dans la table `BridgeStarSystems`
3. Les coordonnées (X, Y, Z) sont des `float` (valeurs par défaut = 0)
4. Les noms de systèmes sont maintenant uniques au niveau global

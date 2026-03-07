# AI Context — EliteBridgePlanner

PROJET
EliteBridgePlanner — application Angular pour planifier les ponts stellaires (Colonia Bridge) dans Elite Dangerous.
Interface composée de :
- une sidebar (PONT ACTIF + liste des systèmes)
- un visualiseur horizontal du pont
- un panneau de détail pour le système sélectionné.

ÉTAT ACTUEL
Le visualiseur du pont utilise une métaphore de ligne de métro futuriste :
- une ligne horizontale représente le pont
- chaque système est une station sur cette ligne
- les stations sont alignées sur la ligne centrale
- les labels des systèmes apparaissent sous la ligne

FONCTIONNALITÉS IMPLÉMENTÉES
- Ligne de progression verte jusqu'au dernier système opérationnel (status FINI).
- Stations alignées sur une ligne horizontale continue.
- Différents types de stations :
  - Départ (terminus de départ)
  - Tablier (station simple)
  - Pile (hub logistique / station majeure)
  - Arrivée (terminus final).
- Pipe TruncateMiddlePipe (shared/pipes) pour tronquer les noms au milieu.
- Layout 50/50 (minmax(0,1fr)) pour Départ / Arrivée dans la sidebar.
- Tooltip CSS personnalisé (data-tooltip + ::after) pour afficher les noms complets.
- title + data-tooltip pour fallback accessibilité.

DÉCISIONS DE DESIGN
- Métaphore visuelle inspirée d'une ligne de métro pour améliorer la lisibilité.
- La ligne verte représente les segments déjà construits du pont.
- Les stations futures restent dans la couleur du thème.
- Les stations ne doivent pas être déformées (pas de flex:1 sur DEBUT/FIN).
- Tooltip CSS utilisé pour éviter la lenteur du tooltip natif.

PROCHAINES ÉTAPES POSSIBLES
- Ajouter éventuellement un halo léger pour les hubs importants (piles).
- Améliorer la lisibilité si le pont contient beaucoup de systèmes (scroll ou compression).
- Ajout d'un avatar à la place du bouton déconnexion et un profile menu pour se déconnecter et changer de langue.
- Ajout d'un deuxième compte CMDR_DEMO_II pour tester avec un multi-compte.
- Affichage de deux boutons sur la login pour se connecter avec l'un ou avec l'autre directement (temporaire).
- Ajout d'une colonne à droite du détail pour afficher les stations en cours de construction dans le système.
- Amélioration des selects natifs HTML pour un dropdown en CSS.
- Sortir les chiffres des bulles dans le visualiseur pour les mettre sous les noms de systèmes.
- Ajouter un max-width aux noms de systèmes, ils sont trop longs.
- Indicateur visuel de construction : petit triangle sous la ligne pour les systèmes en construction.

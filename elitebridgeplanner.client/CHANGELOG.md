Ce fichier explique comment Visual Studio a créé le projet.

Les outils suivants ont été utilisés pour générer ce projet :
- Angular CLI (ng)

Les étapes suivantes ont été utilisées pour générer ce projet :
- Créez un projet Angular avec ng : `ng new elitebridgeplanner.client --defaults --skip-install --skip-git --no-standalone `.
- Ajoutez `proxy.conf.js` aux appels proxy du serveur ASP.NET back-end.
- Ajoutez le script `aspnetcore-https.js` pour installer des certificats https.
- Mettez à jour `package.json` pour appeler `aspnetcore-https.js` et travailler avec https.
- Mettez à jour `angular.json` pour son pointage vers `proxy.conf.js`.
- Mettez à jour le composant app.ts pour récupérer et afficher les informations sur la météo.
- Modifiez app.spec.ts avec des tests mis à jour.
- Mettez à jour app-module.ts pour importer HttpClientModule.
- Créez le fichier projet (`elitebridgeplanner.client.esproj`).
- Créez `launch.json` pour activer le débogage.
- Mettez à jour package.json pour ajouter `jest-editor-support`.
- Mettez à jour package.json pour ajouter `run-script-os`.
- Ajoutez `karma.conf.js` pour les tests unitaires.
- Mettez à jour `angular.json` pour son pointage vers `karma.conf.js`.
- Ajoutez le projet à la solution.
- Mettez à jour le point de terminaison proxy pour qu’il soit le point de terminaison du serveur back-end.
- Ajoutez le projet à la liste des projets de démarrage.
- Écrivez ce fichier.

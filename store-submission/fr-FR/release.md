### Nouveautés de cette version

#### Nouveau

- **Modèles en plusieurs langues :** un modèle peut désormais contenir son texte en plusieurs langues, une par onglet au-dessus de la barre d'outils de l'éditeur, avec un seul titre et un seul jeu d'abréviations. **+** ajoute une langue, et **Traduire ▸ Ajouter comme langue** traduit dans un nouvel onglet. Le premier onglet est celui par défaut, qui est collé ; **Copier la langue** dans le menu contextuel de la liste, **Coller la langue** dans Quick Paste et **← / →** dans la liste des séquences de touches de Quick Paste en choisissent une autre. La recherche porte sur toutes les langues.
- **Extension de navigateur :** avec TTM-Connect 1.2 ou plus récent, vous pouvez aussi coller les autres langues d'un modèle et enregistrer une sélection comme langue d'un modèle. Les versions précédentes de l'extension continuent de fonctionner comme avant.
- **Dossiers synchronisés :** les modèles en plusieurs langues nécessitent TextTemplateManager 2.7 ou plus récent sur chaque PC qui enregistre un fichier partagé ; TextTemplateManager 1 et les versions 2.6 ou antérieures suppriment les autres langues en l'enregistrant.

#### Amélioré

- **Fonctionne avec .NET 10 :** TextTemplateManager utilise désormais .NET 10, pour lequel Microsoft fournit des mises à jour de sécurité jusqu'en novembre 2028. .NET 8, utilisé par les versions précédentes, n'en reçoit plus depuis le 10 novembre 2026. Vos modèles, vos paramètres et votre façon de travailler ne changent pas.
- **Icônes plus claires :** les dossiers et les modèles se distinguent mieux dans la liste et sur les boutons qui les ajoutent.

#### Corrigé

- **Fichiers partagés :** l'enregistrement dans un fichier synchronisé n'écrase plus les modifications faites par un autre PC depuis sa lecture ; elles sont fusionnées à la vérification suivante.
- **Quitter :** en quittant, l'application attend désormais la fin d'un enregistrement des paramètres déjà en cours.
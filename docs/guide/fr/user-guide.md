<!-- Généré depuis web/src/core/docs.fr.ts par `node web/scripts/gen-docs.mjs` (GUIDE). Modifiez la source, pas ce fichier. -->

# Guide d'utilisation

D'une ville Cities: Skylines à une ville Cities: Skylines II, étape par étape.

## Ce qu'il vous faut

**Cities: Skylines (CS1)**

- La ville à convertir, et le mod **CS1 → CS2 Exporter** (aucun autre mod n'est nécessaire).
- Fonctionne avec les villes qui utilisent 81 Tiles, TM:PE, Node Controller (Renewal), Intersection Marking Tool et les routes Vanilla+ ou du Workshop ; leurs données sont exportées quand le mod est activé.

**Cities: Skylines II (CS2)**

- CS2 1.6 ou plus récent, avec le mod **Road Builder** (les types de route convertis sont des routes Road Builder).
- Le mod **CS1 → CS2 Importer**, qui construit le paquet dans votre ville.
- Le mod **Traffic** pour les panneaux de priorité, flèches de voie et connexions de voies de TM:PE (facultatif).

## 1. Installez l'exporteur dans Cities: Skylines

Ajoutez le mod **CS1 → CS2 Exporter** et activez-le dans Content Manager → Mods. En attendant sa publication, compilez-le depuis les sources (voir le README du projet) ; une compilation Release le place dans votre dossier Mods local.

L'exporteur ne fait que lire votre ville. Il ne l'enregistre ni ne la modifie jamais.

## 2. Exportez votre ville

Chargez la ville, puis ouvrez Options → Mod Settings → **CS1 → CS2 Exporter** → **Export city**.

Il écrit `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Cs1Exporter\<ville>-<date>.cs1x` en quelques secondes et affiche un résumé.

## 3. Ouvrez l'export dans l'outil web

Déposez le `.cs1x` sur la page de cs1conv, ou cliquez sur **Choisir un fichier .cs1x…**. Le fichier est lu dans votre navigateur ; rien n'est envoyé.

Si une partie de l'export a échoué (par exemple les données IMT), la page indique laquelle ; tout le reste se convertit quand même.

## 4. Choisissez ce qui est converti

Les cartes de CS2 sont plus petites qu'une carte CS1 de 81 tuiles : vous convertissez un carré de 14,3 km au plus. Faites glisser le carré pour le déplacer et ses coins pour le redimensionner, ou cliquez sur **Auto** pour la zone qui a le plus de routes.

Les routes qui coupent le bord du carré sont laissées de côté (en jaune sur la carte). Placez le bord en pleine campagne si possible. Les autoroutes et voies ferrées qui quittent le carré deviennent des connexions extérieures de CS2.

Au clavier : cliquez sur la carte, puis utilisez les flèches pour déplacer le carré, + et − pour le redimensionner.

## 5. Vérifiez la conversion

**Aperçu** montre ce qui est converti par catégorie avec les chiffres de votre ville, un aperçu du terrain, les options (thème des zones, parties à convertir) et les étapes d'installation.

**Routes** liste chaque type de route avec son équivalent CS2 ; vous pouvez en choisir un autre. **Types RB** montre les routes Road Builder générées et les changements de largeur.

**Bâtiments** liste les bâtiments CS1 avec le bâtiment CS2 choisi pour chaque type, le score de correspondance et la raison ; choisissez-en un autre ou ignorez un type. Pour chaque type, vous pouvez aussi dire ce qui se passe s'il ne tient pas à sa place : chercher un terrain libre à proximité (jusqu'à 400 m), n'importe où dans le carré, ou ne pas le placer. Les très grands bâtiments (universités, centrales, terminaux de fret) cherchent partout par défaut.

**Rapport** liste tout ce qui est approché ou laissé de côté.

## 6. Téléchargez le paquet

Cliquez sur **Télécharger le paquet (.zip)**. La création prend quelques secondes : le terrain, ce sont deux cartes de hauteur de 4096 × 4096.

Décompressez-le dans son propre dossier sous `%LOCALAPPDATA%\cs1-to-cs2\` (collez ce chemin dans la barre d'adresse de l'Explorateur), par exemple `%LOCALAPPDATA%\cs1-to-cs2\ma-ville\`. L'importeur le liste alors dans **Packages on this PC**. Un autre dossier convient aussi ; tapez-le alors dans **Package folder**.

Le dossier contient `cs1conv-plan.json`, `report.json`, les dossiers `roadbuilder` et `terrain` et un `README.txt`.

## 7. Installez les mods et les types de route dans CS2

Activez **Road Builder** et le **CS1 → CS2 Importer**. **Traffic** est facultatif : sans lui, les panneaux de priorité sont ignorés.

Road Builder ne lit ses types de route qu'au lancement du jeu : installez-les donc d'abord. Lancez CS2, ouvrez Options → **CS1 → CS2 importer**, choisissez le paquet (comme à l'étape 9) et cliquez sur **Install Road Builder configs**. Confirmez, attendez "Copied N Road Builder configs", puis **relancez le jeu**. Ou, CS2 fermé, copiez les fichiers du dossier `roadbuilder` du paquet dans `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\ModsData\RoadBuilder\`.

## 8. Commencez une nouvelle ville

Lancez une nouvelle partie (pas l'éditeur). Toutes les cartes conviennent ; Sunshine Peninsula a le moins de routes préconstruites.

L'importeur n'applique pas encore le terrain, l'eau et les connexions extérieures : les routes suivent le sol de la carte, et seules les tranchées de 4 m ou plus gardent leur profondeur CS1. Le paquet les contient déjà (dossier `terrain`) pour le jour où il le fera.

Enregistrez la ville maintenant : l'import ne peut pas être annulé.

## 9. Vérifiez le paquet

Ouvrez Options → **CS1 → CS2 importer** (onglet **Import**). Dans le groupe **Package**, choisissez le paquet dans **Packages on this PC** : la liste montre les paquets sous `%LOCALAPPDATA%\cs1-to-cs2\`, du plus récent au plus ancien, et vérifie celui que vous choisissez. Ou collez le dossier du paquet dans **Package folder** et cliquez sur **Check**.

La ligne d'état indique le contenu du paquet, par exemple "Package: Skibidi.02, 2035 roads to build. All 37 road types are available." S'il manque des types de route, installez les configurations Road Builder et relancez le jeu. S'il y a déjà des routes dans la zone, laissez **Clear the area first** activé.

## 10. Importez dans la ville

Dans le groupe **Import into this city**, toutes les étapes sont activées par défaut : **Clear the area first**, **Zoning**, **Traffic lights, stops, turn bans, crosswalks**, **Priority signs (Traffic mod)**, **Buildings, trees, districts** et **Transit lines**. Désactivez ce que vous ne voulez pas ; les routes sont toujours construites en premier.

Cliquez sur **Import into this city** et confirmez. **Status** montre l'avancement ("Importing… batch 3 of 6", puis chaque étape) et se termine par un résumé par étape, par exemple "Done in 4.33 s: 2035 of 2035 roads built." et "Transit: 9 of 9 lines, 145 of 145 stops.". Il donne aussi le chemin du rapport complet, sous `%LOCALAPPDATA%\cs1-to-cs2\import-reports\`.

Un carré de 3 km prend bien moins d'une minute : les routes environ 4 secondes, les lignes de transport le plus longtemps. Le jeu reste fluide.

## 11. Après l'import

Enregistrez la ville. Elle a encore besoin d'eau, d'électricité et de services pour grandir, comme toute nouvelle ville ; les bâtiments poussent ensuite sur les zones converties.

Lisez l'onglet **Rapport** de l'outil web (ou `report.json`) et le résumé de l'import pour ce qui demande une intervention :

les connexions de voies, flèches de voie et demi-tours de TM:PE ne sont pas appliqués dans cette version (seuls les panneaux de priorité le sont) ; le rapport d'import les liste. Environ 7 % des cellules de zone ne trouvent pas de bloc de zone CS2, car les blocs de CS2 sont moins profonds. Les lignes de métro et de train ont besoin de leurs gares, placées par l'étape des bâtiments. Les dépôts ferroviaires ajoutés pour elles doivent être reliés à la ligne à la main. Quelques routes à faible dénivelé sont réunies en carrefour par CS2.

## Confidentialité

cs1conv tourne entièrement dans votre navigateur. Votre fichier .cs1x est lu sur votre ordinateur et n'est jamais envoyé ; il n'y a ni serveur, ni compte, ni statistiques, ni cookies.

La page garde votre découpe, vos choix de correspondance, vos options et votre langue dans le stockage local du navigateur pour les retrouver la fois suivante. Effacer les données du site les supprime.

Voir aussi : [FAQ et dépannage](faq.md) · [Ce qui est converti](what-converts.md) · [English](../user-guide.md)

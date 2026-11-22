<!-- Généré depuis web/src/core/docs.fr.ts par `node web/scripts/gen-docs.mjs` (GUIDE). Modifiez la source, pas ce fichier. -->

# Guide d'utilisation

D'une ville Cities: Skylines à une ville Cities: Skylines II, étape par étape.

## Ce qu'il vous faut

**Cities: Skylines (CS1)**

- La ville à convertir, et le mod **CS1 → CS2 Exporter** (aucun autre mod n'est nécessaire).
- Fonctionne avec les villes qui utilisent 81 Tiles, TM:PE, Node Controller (Renewal), Intersection Marking Tool et les routes Vanilla+ ou du Workshop ; leurs données sont exportées quand le mod est activé.

**Cities: Skylines II (CS2)**

- CS2 1.6 ou plus récent, avec le mod **Road Builder** (les types de route qui correspondent à une route CS2 vanilla l'utilisent ; les autres deviennent des routes Road Builder).
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

**Routes** liste chaque type de route avec son équivalent CS2 : une route CS2 vanilla quand la route générée a les mêmes voies ("Small Road vanilla (correspondance)"), sinon une route Road Builder, avec la raison dans les deux cas ; vous pouvez en choisir un autre. **Préférer les routes CS2 vanilla** (dans les options, activé par défaut) fait cette correspondance ; désactivez-le pour que toutes les routes soient des routes Road Builder. Chaque type a aussi un réglage **Hauteur** : "le terrain suit la route" (par défaut pour les rues et les voies ferrées : le terrain est modelé à la hauteur CS1 de la route, avec des talus des deux côtés), "hauteur CS1, murs de soutènement" (par défaut pour les autoroutes et les bretelles) ou "la route suit le terrain" (par défaut pour les chemins). **Types RB** montre les routes Road Builder générées et les changements de largeur.

**Bâtiments** liste les bâtiments CS1 avec le bâtiment CS2 choisi pour chaque type, le score de correspondance et la raison ; choisissez-en un autre ou ignorez un type. Pour chaque type, vous pouvez aussi dire ce qui se passe s'il ne tient pas à sa place. Par défaut, il n'est pas déplacé : il glisse un peu ou prend une alternative plus petite, et s'il ne tient toujours pas, le rapport d'import le liste avec sa position CS1 pour que vous le placiez vous-même ("Ne pas le déplacer (me le lister pour que je le place)"). Vous pouvez à la place laisser un type chercher un terrain libre à proximité (jusqu'à 400 m) ou n'importe où dans le carré. Quand le carré contient des bâtiments d'électricité, d'eau ou d'égouts, **Aperçu** affiche un avis avec un bouton, **Les laisser se déplacer à proximité (≤ 400 m) s'ils ne tiennent pas**, qui le fait pour eux tous d'un coup : ceux de CS2 sont souvent bien plus grands que ceux de CS1, et sans eux la ville n'a ni électricité ni eau.

**Rapport** liste tout ce qui est approché ou laissé de côté.

## 6. Téléchargez le paquet

Cliquez sur **Télécharger le paquet (.zip)**. La création prend quelques secondes : le terrain, ce sont deux cartes de hauteur de 4096 × 4096.

Inutile de le décompresser : l'importeur prend le zip et le décompresse lui-même. Il contient `cs1conv-plan.json`, `report.json`, les dossiers `roadbuilder` et `terrain` et un `README.txt`.

## 7. Installez les mods et les types de route dans CS2

Activez **Road Builder** et le **CS1 → CS2 Importer**. **Traffic** est facultatif : il pose les panneaux de priorité, les connexions de voies, les flèches de voie et les règles de demi-tour. La ligne en bas de la page de l'importeur indique si Road Builder et Traffic sont actifs. Les libellés cités ici sont ceux du jeu en français ; si votre jeu est en anglais, voyez le guide anglais.

Lancez CS2 et ouvrez Options → **CS1 → CS2 importer** (onglet **Import**). Cliquez sur **Parcourir…** et choisissez le zip téléchargé : l'importeur le décompresse sous `%LOCALAPPDATA%\cs1-to-cs2\packages\`, affiche "<zip> décompressé dans <dossier>." et le vérifie. **Ouvrir le dossier des paquets** montre ce dossier dans l'Explorateur.

Road Builder ne lit ses types de route qu'au lancement du jeu : installez-les donc maintenant. Cliquez sur **Installer les configurations Road Builder**, confirmez, attendez "N configurations Road Builder copiées", puis **relancez le jeu**. (Ou, CS2 fermé, copiez les fichiers du dossier `roadbuilder` du paquet dans `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\ModsData\RoadBuilder\` ; les configurations d'autres villes converties peuvent y rester.)

## 8. Commencez une nouvelle ville

Lancez une nouvelle partie (pas l'éditeur). Toutes les cartes conviennent, puisque l'importeur remplace leur terrain ; Sunshine Peninsula a le moins de routes préconstruites à retirer.

## 9. Vérifiez le paquet

Ouvrez à nouveau Options → **CS1 → CS2 importer**. Dans le groupe **Paquet**, choisissez le paquet dans **Paquets sur ce PC** (les derniers utilisés en premier), ou utilisez **Parcourir…**, ou collez son dossier, son `cs1conv-plan.json` ou son zip dans **Dossier du paquet** et cliquez sur **Vérifier**.

La ligne d'état indique le contenu du paquet, par exemple "Paquet : Skibidi.02, 2035 routes à construire. Les 37 types de route sont disponibles." et "Carte : terrain avec ses alentours, niveau de la mer 182 m, 5 sources d'eau, 20 connexions extérieures.". Ses dernières lignes disent si le terrain de cette ville est déjà celui du paquet, et si elle roule du même côté que la ville CS1 (dans une nouvelle ville, **Préparer la carte** règle le côté). S'il manque des types de route, installez les configurations Road Builder et relancez le jeu.

## 10. Préparez la carte

Dans le groupe **Nouvelle ville**, cliquez sur **Préparer la carte** et confirmez. Il retire les routes, voies et bâtiments propres à la carte, remplace son terrain et ses environs par ceux de votre ville, règle le niveau de la mer et les sources d'eau de CS1 (un paquet sans données d'eau donne une carte sèche), et fait rouler la circulation du même côté que dans la ville CS1. **État** suit les étapes ("Préparation de la carte… application du terrain", "… mise en place de l'eau") et se termine par "Carte préparée.", la vérification du terrain ("à 0 m près du paquet sur 95 % des 181 points de contrôle"), le côté ("La circulation roule à gauche, comme dans la ville CS1.") et "Ensuite : cliquez sur « Importer dans cette ville ».". Comptez 10 à 20 secondes.

C'est irréversible : utilisez-le dans une nouvelle ville. Sans ville chargée (menu principal, éditeur), il s'arrête avec "Carte non préparée : Commencez d'abord une nouvelle ville.". Un paquet exporté sans le terrain garde le terrain de la carte ("Terrain ignoré : le paquet n'a pas de terrain").

## 11. Importez dans la ville

Dans le groupe **Importer dans cette ville**, choisissez les étapes : **Vider la zone d'abord**, **Zonage**, **Feux, stops, interdictions de tourner, passages piétons**, **Panneaux de priorité (mod Traffic)**, **Connexions de voies (mod Traffic)**, **Bâtiments, arbres, quartiers**, **Lignes de transport** et, en dernier, **Connexions extérieures** sont activées par défaut ; les routes sont toujours construites en premier. **Garder le terrain de cette carte** est désactivé : l'import refuse alors une ville dont le terrain n'est pas celui du paquet ; activez-le pour importer quand même sur le sol de la carte (seules les tranchées de 4 m ou plus gardent leur profondeur). **Importer dans une ville qui roule de l'autre côté** est désactivé aussi : l'import refuse alors une ville où l'on roule de l'autre côté de la route que dans la ville CS1 (**Préparer la carte** règle le bon côté dans une nouvelle ville) ; activez-le pour importer quand même, et routes, sens uniques et connexions de voies sont alors inversés. **Ajouter un poste de transformation à côté de chaque centrale importée** est désactivé par défaut : le rapport d'import nomme alors chaque centrale à charbon, à gaz ou d'incinération qui a besoin d'un poste de transformation (dans CS2, les centrales n'alimentent que le réseau haute tension), et la ville garde son électricité par la connexion extérieure. **Relier les gares de train et de métro et ajouter des dépôts ferroviaires au bord de la zone** est désactivé par défaut (à activer) : une fois activé, les extrémités des quais de chaque gare sont reliées à la voie importée par des courbes douces, et un dépôt ferroviaire ou de métro est placé là où la voie d'une ligne quitte le carré. Limites connues pour l'instant : près des gares de métro aériennes, quelques routes importées peuvent être remplacées, et un dépôt dont le seul accès à sa ligne traverse une route reste non relié (le rapport le signale).

Cliquez sur **Importer dans cette ville** et confirmez. **État** montre l'avancement ("Import… lot 3 sur 6", puis chaque étape) et se termine par un résumé par étape, par exemple "Terminé en 4.33 s : 2035 routes construites sur 2035", "Transports : 9 lignes sur 9, 145 arrêts sur 145." et "Connexions extérieures : 20 sur 20 (13 routières, 7 ferroviaires, 4 électriques, 4 d'eau).". Une étape désactivée indique "désactivé dans les options". Le résumé donne aussi le chemin du rapport complet, sous `%LOCALAPPDATA%\cs1-to-cs2\import-reports\`.

Un carré de 3 km prend moins d'une minute : les routes environ 4 secondes, les lignes de transport le plus longtemps. Le jeu reste fluide, et **Annuler** (groupe **État**) s'arrête après l'étape en cours ; ce qui est construit reste.

## 12. Après l'import

Laissez le jeu tourner quelques minutes : CS2 remplit les rivières et les lacs à partir des sources d'eau. Enregistrez ensuite la ville. Les logements reçoivent électricité, eau et évacuation des eaux usées dès le départ : l'import ajoute des connexions extérieures d'électricité et d'eau au bord des routes importées, en plus de vos centrales et pompes converties. Les bâtiments poussent ensuite sur les zones converties.

Pour revenir en arrière, cliquez sur **Retirer l'import** et confirmez : il supprime les routes, bâtiments, arbres et objets, quartiers et zones, lignes de transport et connexions extérieures créés par les imports, même après enregistrement et rechargement. Le zonage et les règles de circulation partent avec les routes ; la carte de base retirée par **Préparer la carte** ne revient pas.

Lisez l'onglet **Rapport** de l'outil web (ou `report.json`) et le résumé de l'import pour ce qui demande une intervention :

les connexions de voies, flèches de voie et demi-tours s'appliquent aux carrefours ; les divisions de voies là où une route se prolonge dans une autre sans carrefour restent telles que CS2 les construit (environ 8 % des connexions dans nos tests). Dans une ville qui roule à gauche, **Préparer la carte** règle le côté de circulation d'après la ville CS1 ; les routes, les sens uniques et les connexions de voies suivent. Environ 7 % des cellules de zone ne trouvent pas de bloc de zone CS2, car les blocs de CS2 sont moins profonds. Les centrales à charbon, à gaz et les incinérateurs alimentent le réseau haute tension de CS2, qui n'atteint la ville que par un poste de transformation : activez **Ajouter un poste de transformation à côté de chaque centrale importée**, ou placez-en un vous-même (le rapport d'import nomme chaque centrale). Les éoliennes et autres producteurs basse tension sont reliés automatiquement à la route la plus proche. Une centrale loin de toute route ne produit rien tant que vous ne lui en donnez pas une (le rapport la nomme). Les lignes de métro et de train ont besoin de leurs gares, placées par l'étape des bâtiments ; une ligne de métro ou de train dont la voie reste dans le carré ne reçoit pas de dépôt : placez-en un vous-même (le rapport d'import nomme la ligne). Quand deux routes CS1 quittent un carrefour avec un angle très faible, CS2 fusionne leurs premiers mètres en une seule route et les sépare un peu plus loin, connectées comme dans CS1. Quelques morceaux de CS1 qui se chevauchent et que CS2 ne peut pas placer (par exemple une route à quelques mètres d'une route parallèle) sont laissés de côté et listés dans le rapport.

## Confidentialité

cs1conv tourne entièrement dans votre navigateur. Votre fichier .cs1x est lu sur votre ordinateur et n'est jamais envoyé ; il n'y a ni serveur, ni compte, ni statistiques, ni cookies.

La page garde votre découpe, vos choix de correspondance, vos options et votre langue dans le stockage local du navigateur pour les retrouver la fois suivante. Effacer les données du site les supprime.

Voir aussi : [FAQ et dépannage](faq.md) · [Ce qui est converti](what-converts.md) · [English](../user-guide.md)

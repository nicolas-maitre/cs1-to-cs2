<!-- Généré depuis web/src/core/docs.fr.ts par `node web/scripts/gen-docs.mjs` (FAQ). Modifiez la source, pas ce fichier. -->

# FAQ et dépannage

## Ma ville est-elle envoyée quelque part ?

Non. L'outil web tourne entièrement dans votre navigateur ; le fichier ne quitte jamais votre ordinateur. Il n'y a pas de serveur derrière la page.

## Pourquoi ne puis-je pas convertir toute ma ville de 81 tuiles ?

La zone jouable d'une carte CS2 mesure environ 14,3 km de côté, moins que les 81 tuiles de CS1 (17,3 km). L'outil web vous laisse choisir le carré qui tient ; vous pouvez convertir un autre carré dans une autre ville.

## Certaines routes sont plus étroites ou plus larges que dans CS1. Pourquoi ?

Les types de route sont recréés avec Road Builder, qui a des largeurs de voie fixes et des trottoirs de 7 m au plus. Une route CS1 avec des trottoirs de 9 m devient quelques mètres plus étroite. Chaque changement figure sous **Types RB** et dans le rapport.

## La page dit qu'elle ne peut pas ouvrir mon fichier.

Ouvrez le `.cs1x` écrit par l'exporteur, pas une sauvegarde `.crp`. Si elle indique un format trop récent, rechargez la page pour avoir le dernier outil ; s'il est trop ancien, mettez l'exporteur à jour et exportez à nouveau.

## Une partie de mon export a échoué.

Le reste se convertit normalement. Exportez à nouveau ; si l'échec persiste, le message de l'exporteur se trouve dans l'outil web sous Aperçu → Vérification du fichier.

## L'importeur ne liste pas mon paquet.

**Packages on this PC** liste les dossiers sous `%LOCALAPPDATA%\cs1-to-cs2\` qui contiennent un `cs1conv-plan.json`. Décompressez le paquet à cet endroit, ou tapez son dossier dans **Package folder**.

## L'importeur dit que des types de route manquent.

Les routes Road Builder doivent être dans ModsData\RoadBuilder **avant** le lancement de CS2. "Road Builder configs aren't installed" : cliquez sur **Install Road Builder configs**, puis relancez CS2. "Installed but not loaded yet" : relancez CS2. Cliquez ensuite à nouveau sur **Check**. Les routes d'un type manquant sont ignorées.

## Ma ville n'a ni les collines, ni les rivières, ni la mer de CS1.

L'importeur n'applique pas encore le terrain ni l'eau : les routes suivent le sol de la carte CS2. Le paquet contient déjà le terrain converti (dossier `terrain`), que l'importeur utilisera dans une version ultérieure.

## Où sont mes connexions de voies et flèches de voie TM:PE ?

Elles sont dans le paquet mais pas appliquées dans cette version : seuls les panneaux de priorité le sont (avec le mod Traffic). Le rapport d'import liste le reste.

## Pourquoi tous mes bâtiments n'ont-ils pas été placés ?

Les bâtiments des zones ne sont pas placés : CS2 construit les siens sur les zones converties. Les piliers, jonctions de tuyaux et poteaux sont reconstruits par CS2. Quelques types de bâtiment CS1 n'ont pas d'équivalent CS2 (bibliothèques, stations de taxi, arrêts de téléphérique), et les bâtiments sur des routes hors du carré sont laissés de côté. L'onglet Bâtiments montre chaque type.

## Pourquoi un bâtiment s'est-il retrouvé ailleurs ?

Les bâtiments CS2 ont souvent besoin de plus de place que leurs équivalents CS1. Quand l'un d'eux ne tient pas à sa place CS1, l'importeur essaie les bâtiments CS2 suivants les mieux classés, puis un terrain libre le long d'une route importée à proximité (jusqu'à 400 m). Les très grands bâtiments peuvent aller n'importe où dans le carré. Dans l'onglet Bâtiments, vous pouvez changer cela par type, ou ne pas placer le bâtiment.

## Ma ligne de métro ou de train n'a pas de véhicules.

Les lignes CS2 reçoivent leurs véhicules d'un dépôt de leur type, et les lignes de métro et de train de CS1 n'en ont pas besoin. Quand la ville n'en a pas, le paquet ajoute un dépôt par défaut au premier arrêt de la ligne. Reliez à la main la voie d'un dépôt ferroviaire à la ligne : dans CS2, les dépôts n'envoient des trains que sur des voies reliées.

## Ai-je besoin du mod Traffic ?

Seulement pour les panneaux de priorité, flèches de voie et connexions de voies de TM:PE. Limites de vitesse, voies de bus, feux, arrêts toutes directions, interdictions de tourner et passages piétons fonctionnent sans lui.

## Que deviennent les feux programmés et les réglages de Node Controller ?

CS2 gère lui-même les phases des feux et construit lui-même les carrefours. Les feux programmés deviennent des feux ordinaires ; les réglages de Node Controller figurent dans le rapport.

## Bâtiments européens ou nord-américains ?

Choisissez le thème des zones sous Aperçu → Options. Il détermine les zones résidentielles et commerciales de CS2 utilisées, et les variantes d'arbres préférées.

## L'exporteur modifie-t-il ma sauvegarde CS1 ?

Non. Il ne fait que lire la ville chargée et écrire le fichier `.cs1x`.

## Pourquoi le paquet est-il si gros ?

L'essentiel, c'est le terrain : deux cartes de hauteur de 4096 × 4096, la taille qu'utilise CS2, 64 Mo décompressées et environ 22 Mo dans le zip. Tout ce dont le mod CS2 a besoin est dans le paquet : rien d'autre ne doit tourner sur votre ordinateur.

## Ma grande ville est-elle trop grosse pour l'outil web ?

La carte et la création du paquet sont faites pour les grandes villes : une ville de test de 60 000 segments reste fluide, et le paquet est créé en arrière-plan.

## Puis-je utiliser l'outil web en français ?

Oui : il suit la langue de votre navigateur, et le menu de langue en haut de la page passe de l'anglais au français. Le rapport (`report.json`) reste en anglais.

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

**Paquets sur ce PC** liste les derniers paquets utilisés, puis les dossiers sous `%LOCALAPPDATA%\cs1-to-cs2\` qui contiennent un `cs1conv-plan.json`. Cliquez sur **Parcourir…** et choisissez le zip téléchargé (l'importeur le décompresse sous `packages\`) ou un `cs1conv-plan.json`, ou collez son chemin dans **Dossier du paquet**. **Ouvrir le dossier des paquets** ouvre ce dossier dans l'Explorateur.

## L'importeur dit que des types de route manquent.

Les routes Road Builder doivent être dans ModsData\RoadBuilder **avant** le lancement de CS2. "Configurations Road Builder pas installées" : cliquez sur **Installer les configurations Road Builder**, puis relancez CS2. "Installées mais pas encore chargées" : relancez CS2. "Road Builder n'est pas actif" : activez Road Builder dans votre playset et relancez. Cliquez ensuite à nouveau sur **Vérifier**. Les routes d'un type manquant sont ignorées.

## Ma ville n'a ni les collines, ni les rivières, ni la mer de CS1.

Cliquez sur **Préparer la carte** dans une nouvelle ville avant d'importer : il remplace le terrain et les environs de la carte par ceux de votre ville et règle le niveau de la mer et les sources d'eau. Laissez ensuite le jeu tourner quelques minutes pour que CS2 remplisse les rivières et les lacs. Un paquet créé sans données d'eau donne une carte sèche.

## Où sont mes connexions de voies et flèches de voie TM:PE ?

**Connexions de voies (mod Traffic)** les recrée aux carrefours, avec les règles de demi-tour ; il est activé par défaut et nécessite le mod Traffic. Là où une route se prolonge dans une autre sans carrefour, CS2 garde sa propre continuité de voies, et les divisions de voies n'y sont pas reproductibles. Le résumé de l'import indique combien de références de voies CS1 ont été retrouvées.

## Comment annuler un import ?

Cliquez sur **Retirer l'import** : il supprime les routes, bâtiments, arbres et objets, quartiers et zones, lignes de transport et connexions extérieures créés par les imports dans cette ville, même après enregistrement et rechargement. Le zonage et les règles de circulation partent avec les routes. La carte de base retirée par **Préparer la carte** ne revient pas ; commencez une nouvelle ville pour cela.

## L'import dit que le terrain de cette ville n'est pas celui du paquet.

Commencez une nouvelle ville et cliquez sur **Préparer la carte** avant d'importer. Pour construire quand même sur le sol de la carte, activez **Garder le terrain de cette carte** : les routes suivent alors ce sol, et seules les tranchées de 4 m ou plus gardent leur profondeur CS1.

## Pourquoi tous mes bâtiments n'ont-ils pas été placés ?

Les bâtiments des zones ne sont pas placés : CS2 construit les siens sur les zones converties. Les piliers, jonctions de tuyaux et poteaux sont reconstruits par CS2. Quelques types de bâtiment CS1 n'ont pas d'équivalent CS2 (bibliothèques, stations de taxi, arrêts de téléphérique), et les bâtiments sur des routes hors du carré sont laissés de côté. L'onglet Bâtiments montre chaque type.

## Pourquoi un bâtiment s'est-il retrouvé ailleurs ?

Les bâtiments CS2 ont souvent besoin de plus de place que leurs équivalents CS1. Quand l'un d'eux ne tient pas à sa place CS1, l'importeur essaie les bâtiments CS2 suivants les mieux classés, puis un terrain libre le long d'une route importée à proximité (jusqu'à 400 m). Les très grands bâtiments peuvent aller n'importe où dans le carré. Dans l'onglet Bâtiments, vous pouvez changer cela par type, ou ne pas placer le bâtiment.

## Ma ligne de métro ou de train n'a pas de véhicules.

Les lignes CS2 reçoivent leurs véhicules d'un dépôt de leur type, et les lignes de métro et de train de CS1 n'en ont pas besoin. Les dépôts ferroviaires ne sont jamais placés dans votre ville : par défaut, l'importeur place un dépôt compact au bord du carré, là où la voie de la ligne en sort, et le relie. Une ligne dont la voie reste dans le carré n'en reçoit pas ; le rapport d'import la nomme, et vous placez un dépôt vous-même. Sous Aperçu → Options, vous pouvez choisir "Je le place moi-même" pour le métro ou le train. Les lignes de bus et de tram reçoivent un dépôt par défaut près de leur premier arrêt.

## Ai-je besoin du mod Traffic ?

Seulement pour les panneaux de priorité, connexions de voies, flèches de voie et demi-tours de TM:PE. Limites de vitesse, voies de bus, feux, arrêts toutes directions, interdictions de tourner et passages piétons fonctionnent sans lui.

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

<!-- Généré depuis web/src/core/docs.fr.ts par `node web/scripts/gen-docs.mjs` (FAQ). Modifiez la source, pas ce fichier. -->

# FAQ et dépannage

## Ma ville est-elle envoyée quelque part ?

Non. L'outil web tourne entièrement dans votre navigateur ; le fichier ne quitte jamais votre ordinateur. Il n'y a pas de serveur derrière la page.

## Pourquoi ne puis-je pas convertir toute ma ville de 81 tuiles ?

La zone jouable d'une carte CS2 mesure environ 14,3 km de côté, moins que les 81 tuiles de CS1 (17,3 km). L'outil web vous laisse choisir le carré qui tient ; vous pouvez convertir un autre carré dans une autre ville.

## Pourquoi certaines routes sont-elles des routes CS2 vanilla et d'autres des routes Road Builder ?

Quand la route que cs1conv générerait a les mêmes voies qu'une route CS2 vanilla (mêmes voies de circulation et mêmes sens de chaque côté, chacune à 0,5 m près en largeur, stationnement et pistes cyclables des mêmes côtés, même terre-plein, largeur totale à 1 m près et même vitesse), la route vanilla est utilisée : moins de configurations Road Builder à installer, et des routes identiques à celles du jeu. Les arbres, les voies de bus et les autres changements venant d'IMT ou de TM:PE gardent en général une route sur Road Builder. **Routes** donne la raison pour chaque type. Pour imposer Road Builder, choisissez **Road Builder** pour ce type, ou désactivez **Préférer les routes CS2 vanilla**.

## Pourquoi le terrain est-il remodelé le long des routes ?

CS1 aplanit le sol sous ses routes, mais la carte de hauteur exportée est trop grossière (cellules de 16 m) pour le garder. Le paquet grave donc le profil de hauteur CS1 de chaque rue dans le terrain CS2, avec des talus qui rejoignent le sol naturel, et les routes sont exactement à leur hauteur CS1 au lieu de suivre des bosses. Les autoroutes gardent leur hauteur CS1 avec des murs de soutènement, et les chemins suivent le terrain. Réglable par type de route dans l'onglet **Routes** ("Hauteur").

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

**Connexions de voies (mod Traffic)** les recrée aux carrefours, avec les règles de demi-tour ; il est activé par défaut et nécessite le mod Traffic. Là où une route se prolonge dans une autre sans carrefour, CS2 garde sa propre continuité de voies, et les divisions de voies n'y sont pas reproductibles. Le résumé de l'import indique combien de références de voies CS1 ont été retrouvées. Elles survivent à la sauvegarde et au rechargement de la ville : la dernière étape de l'import, la **vérification des connexions de voies**, réécrit les carrefours dont une étape suivante a modifié les routes (par exemple une baie d'arrêt de bus), et sa ligne dans le résumé en donne le nombre.

## Comment annuler un import ?

Cliquez sur **Retirer l'import** : il supprime les routes, bâtiments, arbres et objets, quartiers et zones, lignes de transport et connexions extérieures créés par les imports dans cette ville, même après enregistrement et rechargement. Le zonage et les règles de circulation partent avec les routes. La carte de base retirée par **Préparer la carte** ne revient pas ; commencez une nouvelle ville pour cela.

## L'import dit « Ce paquet est pour une ville où l'on roule à gauche ; cette ville roule à droite » (ou l'inverse).

La ville CS1 et cette ville CS2 ne roulent pas du même côté de la route. Commencez une nouvelle ville et cliquez sur **Préparer la carte** : elle règle le côté de circulation de la ville CS1 avant de construire la moindre route. Pour importer quand même, activez **Importer dans une ville qui roule de l'autre côté** ; routes, sens uniques et connexions de voies sont alors inversés.

## L'import dit que le terrain de cette ville n'est pas celui du paquet.

Commencez une nouvelle ville et cliquez sur **Préparer la carte** avant d'importer. Pour construire quand même sur le sol de la carte, activez **Garder le terrain de cette carte** : les routes suivent alors ce sol, et seules les tranchées de 4 m ou plus gardent leur profondeur CS1.

## Pourquoi tous mes bâtiments n'ont-ils pas été placés ?

Les bâtiments des zones ne sont pas placés : CS2 construit les siens sur les zones converties. Les piliers, jonctions de tuyaux et poteaux sont reconstruits par CS2. Quelques types de bâtiment CS1 n'ont pas d'équivalent CS2 (bibliothèques, stations de taxi, arrêts de téléphérique), et les bâtiments sur des routes hors du carré sont laissés de côté. L'onglet Bâtiments montre chaque type.

## Un bâtiment manque, ou s'est retrouvé ailleurs (mon hôpital a bougé). Pourquoi ?

Les bâtiments CS2 ont souvent besoin de plus de place que leurs équivalents CS1. Quand l'un d'eux ne tient pas à sa place CS1, l'importeur le fait glisser un peu ou essaie une alternative CS2 plus petite. S'il ne tient toujours pas, il n'est **pas déplacé** ailleurs : le rapport d'import le liste comme "à placer vous-même" avec sa position CS1, et vous le placez à la main. Un bâtiment ne va ailleurs que si vous avez réglé son type, dans l'onglet Bâtiments, pour chercher un terrain libre à proximité (jusqu'à 400 m) ou n'importe où dans le carré, ou, pour les bâtiments d'électricité, d'eau et d'égouts, si vous avez cliqué sur **Les laisser se déplacer à proximité** dans Aperçu. Une exception : quand aucun des dépôts de bus (ou de tram) de la ville ne tient à sa place CS1, l'un d'eux est placé à proximité, car ses lignes n'ont pas de véhicules sans dépôt ; le rapport d'import le signale.

## Ma ligne de métro ou de train n'a pas de véhicules.

Les lignes CS2 reçoivent leurs véhicules d'un dépôt de leur type, et les lignes de métro et de train de CS1 n'en ont pas besoin. Les dépôts ferroviaires ne sont jamais placés dans votre ville : par défaut, l'importeur place un dépôt compact au bord du carré, là où la voie de la ligne en sort, et le relie. Une ligne dont la voie reste dans le carré n'en reçoit pas ; le rapport d'import la nomme, et vous placez un dépôt vous-même. Sous Aperçu → Options, vous pouvez choisir "Je le place moi-même" pour le métro ou le train. Les lignes de bus et de tram reçoivent toujours un dépôt : si votre ville CS1 n'en avait aucun de ce type, un dépôt par défaut est ajouté près du premier arrêt ; si ses dépôts de ce type ne tiennent pas à leur place CS1, l'un d'eux est placé à proximité. Les dépôts et les liaisons des quais viennent avec **Relier les gares de train et de métro et ajouter des dépôts ferroviaires au bord de la zone** (désactivé par défaut ; activez-le pour l'essayer) : les trains peuvent alors sortir de leur dépôt et desservent leurs gares. Si un train ou un métro reste dans son dépôt, sa voie rejoindrait la ligne sous un angle trop vif ou en traversant une route : reliez-la vous-même par une courbe douce ou un pont. Les lignes, gares et arrêts sont tous là.

## Ai-je besoin du mod Traffic ?

Seulement pour les panneaux de priorité, connexions de voies, flèches de voie et demi-tours de TM:PE. Limites de vitesse, voies de bus, feux, arrêts toutes directions, interdictions de tourner et passages piétons fonctionnent sans lui.

## Que deviennent les feux programmés et les réglages de Node Controller ?

CS2 gère lui-même les phases des feux et construit lui-même les carrefours. Les feux programmés deviennent des feux ordinaires ; les décalages latéraux de Node Controller sont convertis (les routes se terminent à côté du centre du nœud et les voies s'alignent comme dans CS1), et les décalages longitudinaux aux carrefours aussi (les carrefours allongés gardent leur longueur, avec des séparations et des insertions douces) ; les autres réglages de Node Controller figurent dans le rapport.

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

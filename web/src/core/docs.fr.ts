// Documentation utilisateur en français : même structure que DOCS_EN (docs.ts), vérifiée par test/i18n.test.ts.
// Les libellés des jeux et des mods (options, boutons) restent tels qu'ils s'affichent, en anglais.

import type { Docs } from './docs.ts';

export const DOCS_FR: Docs = {
  STATUS_TEXT: {
    yes: 'converti',
    approx: 'converti, approché',
    report: 'seulement dans le rapport',
    no: 'non converti',
    pending: 'dans le paquet ; côté CS2 en cours',
  },

  PRIVACY: [
    'cs1conv tourne entièrement dans votre navigateur. Votre fichier .cs1x est lu sur votre ordinateur et n\'est jamais envoyé ; il n\'y a ni serveur, ni compte, ni statistiques, ni cookies.',
    'La page garde votre découpe, vos choix de correspondance, vos options et votre langue dans le stockage local du navigateur pour les retrouver la fois suivante. Effacer les données du site les supprime.',
  ],

  REQUIREMENTS: [
    {
      game: 'Cities: Skylines (CS1)',
      items: [
        'La ville à convertir, et le mod **CS1 → CS2 Exporter** (aucun autre mod n\'est nécessaire).',
        'Fonctionne avec les villes qui utilisent 81 Tiles, TM:PE, Node Controller (Renewal), Intersection Marking Tool et les routes Vanilla+ ou du Workshop ; leurs données sont exportées quand le mod est activé.',
      ],
    },
    {
      game: 'Cities: Skylines II (CS2)',
      items: [
        'CS2 1.6 ou plus récent, avec le mod **Road Builder** (les types de route convertis sont des routes Road Builder).',
        'Le mod **CS1 → CS2 Importer**, qui construit le paquet dans votre ville.',
        'Le mod **Traffic** pour les panneaux de priorité, flèches de voie et connexions de voies de TM:PE (facultatif).',
      ],
    },
  ],

  GUIDE: [
    {
      title: 'Installez l\'exporteur dans Cities: Skylines',
      body: [
        'Ajoutez le mod **CS1 → CS2 Exporter** et activez-le dans Content Manager → Mods. En attendant sa publication, compilez-le depuis les sources (voir le README du projet) ; une compilation Release le place dans votre dossier Mods local.',
        'L\'exporteur ne fait que lire votre ville. Il ne l\'enregistre ni ne la modifie jamais.',
      ],
    },
    {
      title: 'Exportez votre ville',
      body: [
        'Chargez la ville, puis ouvrez Options → Mod Settings → **CS1 → CS2 Exporter** → **Export city**.',
        'Il écrit `%LOCALAPPDATA%\\Colossal Order\\Cities_Skylines\\Cs1Exporter\\<ville>-<date>.cs1x` en quelques secondes et affiche un résumé.',
      ],
    },
    {
      title: 'Ouvrez l\'export dans l\'outil web',
      body: [
        'Déposez le `.cs1x` sur la page de cs1conv, ou cliquez sur **Choisir un fichier .cs1x…**. Le fichier est lu dans votre navigateur ; rien n\'est envoyé.',
        'Si une partie de l\'export a échoué (par exemple les données IMT), la page indique laquelle ; tout le reste se convertit quand même.',
      ],
    },
    {
      title: 'Choisissez ce qui est converti',
      body: [
        'Les cartes de CS2 sont plus petites qu\'une carte CS1 de 81 tuiles : vous convertissez un carré de 14,3 km au plus. Faites glisser le carré pour le déplacer et ses coins pour le redimensionner, ou cliquez sur **Auto** pour la zone qui a le plus de routes.',
        'Les routes qui coupent le bord du carré sont laissées de côté (en jaune sur la carte). Placez le bord en pleine campagne si possible. Les autoroutes et voies ferrées qui quittent le carré deviennent des connexions extérieures de CS2.',
        'Au clavier : cliquez sur la carte, puis utilisez les flèches pour déplacer le carré, + et − pour le redimensionner.',
      ],
    },
    {
      title: 'Vérifiez la conversion',
      body: [
        '**Aperçu** montre ce qui est converti par catégorie avec les chiffres de votre ville, un aperçu du terrain, les options (thème des zones, parties à convertir) et les étapes d\'installation.',
        '**Routes** liste chaque type de route avec son équivalent CS2 ; vous pouvez en choisir un autre. **Types RB** montre les routes Road Builder générées et les changements de largeur.',
        '**Bâtiments** liste les bâtiments CS1 avec le bâtiment CS2 choisi pour chaque type, le score de correspondance et la raison ; choisissez-en un autre ou ignorez un type. Pour chaque type, vous pouvez aussi dire ce qui se passe s\'il ne tient pas à sa place : chercher un terrain libre à proximité (jusqu\'à 400 m), n\'importe où dans le carré, ou ne pas le placer. Les très grands bâtiments (universités, centrales, terminaux de fret) cherchent partout par défaut.',
        '**Rapport** liste tout ce qui est approché ou laissé de côté.',
      ],
    },
    {
      title: 'Téléchargez le paquet',
      body: [
        'Cliquez sur **Télécharger le paquet (.zip)**. La création prend quelques secondes : le terrain, ce sont deux cartes de hauteur de 4096 × 4096.',
        'Décompressez-le dans son propre dossier sous `%LOCALAPPDATA%\\cs1-to-cs2\\` (collez ce chemin dans la barre d\'adresse de l\'Explorateur), par exemple `%LOCALAPPDATA%\\cs1-to-cs2\\ma-ville\\`. L\'importeur le liste alors dans **Packages on this PC**. Un autre dossier convient aussi ; tapez-le alors dans **Package folder**.',
        'Le dossier contient `cs1conv-plan.json`, `report.json`, les dossiers `roadbuilder` et `terrain` et un `README.txt`.',
      ],
    },
    {
      title: 'Installez les mods et les types de route dans CS2',
      body: [
        'Activez **Road Builder** et le **CS1 → CS2 Importer**. **Traffic** est facultatif : sans lui, les panneaux de priorité sont ignorés.',
        'Road Builder ne lit ses types de route qu\'au lancement du jeu : installez-les donc d\'abord. Lancez CS2, ouvrez Options → **CS1 → CS2 importer**, choisissez le paquet (comme à l\'étape 9) et cliquez sur **Install Road Builder configs**. Confirmez, attendez "Copied N Road Builder configs", puis **relancez le jeu**. Ou, CS2 fermé, copiez les fichiers du dossier `roadbuilder` du paquet dans `%USERPROFILE%\\AppData\\LocalLow\\Colossal Order\\Cities Skylines II\\ModsData\\RoadBuilder\\`.',
      ],
    },
    {
      title: 'Commencez une nouvelle ville',
      body: [
        'Lancez une nouvelle partie (pas l\'éditeur). Toutes les cartes conviennent ; Sunshine Peninsula a le moins de routes préconstruites.',
        'L\'importeur n\'applique pas encore le terrain, l\'eau et les connexions extérieures : les routes suivent le sol de la carte, et seules les tranchées de 4 m ou plus gardent leur profondeur CS1. Le paquet les contient déjà (dossier `terrain`) pour le jour où il le fera.',
        'Enregistrez la ville maintenant : l\'import ne peut pas être annulé.',
      ],
    },
    {
      title: 'Vérifiez le paquet',
      body: [
        'Ouvrez Options → **CS1 → CS2 importer** (onglet **Import**). Dans le groupe **Package**, choisissez le paquet dans **Packages on this PC** : la liste montre les paquets sous `%LOCALAPPDATA%\\cs1-to-cs2\\`, du plus récent au plus ancien, et vérifie celui que vous choisissez. Ou collez le dossier du paquet dans **Package folder** et cliquez sur **Check**.',
        'La ligne d\'état indique le contenu du paquet, par exemple "Package: Skibidi.02, 2035 roads to build. All 37 road types are available." S\'il manque des types de route, installez les configurations Road Builder et relancez le jeu. S\'il y a déjà des routes dans la zone, laissez **Clear the area first** activé.',
      ],
    },
    {
      title: 'Importez dans la ville',
      body: [
        'Dans le groupe **Import into this city**, toutes les étapes sont activées par défaut : **Clear the area first**, **Zoning**, **Traffic lights, stops, turn bans, crosswalks**, **Priority signs (Traffic mod)**, **Buildings, trees, districts** et **Transit lines**. Désactivez ce que vous ne voulez pas ; les routes sont toujours construites en premier.',
        'Cliquez sur **Import into this city** et confirmez. **Status** montre l\'avancement ("Importing… batch 3 of 6", puis chaque étape) et se termine par un résumé par étape, par exemple "Done in 4.33 s: 2035 of 2035 roads built." et "Transit: 9 of 9 lines, 145 of 145 stops.". Il donne aussi le chemin du rapport complet, sous `%LOCALAPPDATA%\\cs1-to-cs2\\import-reports\\`.',
        'Un carré de 3 km prend bien moins d\'une minute : les routes environ 4 secondes, les lignes de transport le plus longtemps. Le jeu reste fluide.',
      ],
    },
    {
      title: 'Après l\'import',
      body: [
        'Enregistrez la ville. Elle a encore besoin d\'eau, d\'électricité et de services pour grandir, comme toute nouvelle ville ; les bâtiments poussent ensuite sur les zones converties.',
        'Lisez l\'onglet **Rapport** de l\'outil web (ou `report.json`) et le résumé de l\'import pour ce qui demande une intervention :',
        'les connexions de voies, flèches de voie et demi-tours de TM:PE ne sont pas appliqués dans cette version (seuls les panneaux de priorité le sont) ; le rapport d\'import les liste. Environ 7 % des cellules de zone ne trouvent pas de bloc de zone CS2, car les blocs de CS2 sont moins profonds. Les lignes de métro et de train ont besoin de leurs gares, placées par l\'étape des bâtiments. Les dépôts ferroviaires ajoutés pour elles doivent être reliés à la ligne à la main. Quelques routes à faible dénivelé sont réunies en carrefour par CS2.',
      ],
    },
  ],

  FAQ: [
    { q: 'Ma ville est-elle envoyée quelque part ?', a: ['Non. L\'outil web tourne entièrement dans votre navigateur ; le fichier ne quitte jamais votre ordinateur. Il n\'y a pas de serveur derrière la page.'] },
    { q: 'Pourquoi ne puis-je pas convertir toute ma ville de 81 tuiles ?', a: ['La zone jouable d\'une carte CS2 mesure environ 14,3 km de côté, moins que les 81 tuiles de CS1 (17,3 km). L\'outil web vous laisse choisir le carré qui tient ; vous pouvez convertir un autre carré dans une autre ville.'] },
    { q: 'Certaines routes sont plus étroites ou plus larges que dans CS1. Pourquoi ?', a: ['Les types de route sont recréés avec Road Builder, qui a des largeurs de voie fixes et des trottoirs de 7 m au plus. Une route CS1 avec des trottoirs de 9 m devient quelques mètres plus étroite. Chaque changement figure sous **Types RB** et dans le rapport.'] },
    { q: 'La page dit qu\'elle ne peut pas ouvrir mon fichier.', a: ['Ouvrez le `.cs1x` écrit par l\'exporteur, pas une sauvegarde `.crp`. Si elle indique un format trop récent, rechargez la page pour avoir le dernier outil ; s\'il est trop ancien, mettez l\'exporteur à jour et exportez à nouveau.'] },
    { q: 'Une partie de mon export a échoué.', a: ['Le reste se convertit normalement. Exportez à nouveau ; si l\'échec persiste, le message de l\'exporteur se trouve dans l\'outil web sous Aperçu → Vérification du fichier.'] },
    { q: 'L\'importeur ne liste pas mon paquet.', a: ['**Packages on this PC** liste les dossiers sous `%LOCALAPPDATA%\\cs1-to-cs2\\` qui contiennent un `cs1conv-plan.json`. Décompressez le paquet à cet endroit, ou tapez son dossier dans **Package folder**.'] },
    { q: 'L\'importeur dit que des types de route manquent.', a: ['Les routes Road Builder doivent être dans ModsData\\RoadBuilder **avant** le lancement de CS2. "Road Builder configs aren\'t installed" : cliquez sur **Install Road Builder configs**, puis relancez CS2. "Installed but not loaded yet" : relancez CS2. Cliquez ensuite à nouveau sur **Check**. Les routes d\'un type manquant sont ignorées.'] },
    { q: 'Ma ville n\'a ni les collines, ni les rivières, ni la mer de CS1.', a: ['L\'importeur n\'applique pas encore le terrain ni l\'eau : les routes suivent le sol de la carte CS2. Le paquet contient déjà le terrain converti (dossier `terrain`), que l\'importeur utilisera dans une version ultérieure.'] },
    { q: 'Où sont mes connexions de voies et flèches de voie TM:PE ?', a: ['Elles sont dans le paquet mais pas appliquées dans cette version : seuls les panneaux de priorité le sont (avec le mod Traffic). Le rapport d\'import liste le reste.'] },
    { q: 'Pourquoi tous mes bâtiments n\'ont-ils pas été placés ?', a: ['Les bâtiments des zones ne sont pas placés : CS2 construit les siens sur les zones converties. Les piliers, jonctions de tuyaux et poteaux sont reconstruits par CS2. Quelques types de bâtiment CS1 n\'ont pas d\'équivalent CS2 (bibliothèques, stations de taxi, arrêts de téléphérique), et les bâtiments sur des routes hors du carré sont laissés de côté. L\'onglet Bâtiments montre chaque type.'] },
    { q: 'Pourquoi un bâtiment s\'est-il retrouvé ailleurs ?', a: ['Les bâtiments CS2 ont souvent besoin de plus de place que leurs équivalents CS1. Quand l\'un d\'eux ne tient pas à sa place CS1, l\'importeur essaie les bâtiments CS2 suivants les mieux classés, puis un terrain libre le long d\'une route importée à proximité (jusqu\'à 400 m). Les très grands bâtiments peuvent aller n\'importe où dans le carré. Dans l\'onglet Bâtiments, vous pouvez changer cela par type, ou ne pas placer le bâtiment.'] },
    { q: 'Ma ligne de métro ou de train n\'a pas de véhicules.', a: ['Les lignes CS2 reçoivent leurs véhicules d\'un dépôt de leur type, et les lignes de métro et de train de CS1 n\'en ont pas besoin. Quand la ville n\'en a pas, le paquet ajoute un dépôt par défaut au premier arrêt de la ligne. Reliez à la main la voie d\'un dépôt ferroviaire à la ligne : dans CS2, les dépôts n\'envoient des trains que sur des voies reliées.'] },
    { q: 'Ai-je besoin du mod Traffic ?', a: ['Seulement pour les panneaux de priorité, flèches de voie et connexions de voies de TM:PE. Limites de vitesse, voies de bus, feux, arrêts toutes directions, interdictions de tourner et passages piétons fonctionnent sans lui.'] },
    { q: 'Que deviennent les feux programmés et les réglages de Node Controller ?', a: ['CS2 gère lui-même les phases des feux et construit lui-même les carrefours. Les feux programmés deviennent des feux ordinaires ; les réglages de Node Controller figurent dans le rapport.'] },
    { q: 'Bâtiments européens ou nord-américains ?', a: ['Choisissez le thème des zones sous Aperçu → Options. Il détermine les zones résidentielles et commerciales de CS2 utilisées, et les variantes d\'arbres préférées.'] },
    { q: 'L\'exporteur modifie-t-il ma sauvegarde CS1 ?', a: ['Non. Il ne fait que lire la ville chargée et écrire le fichier `.cs1x`.'] },
    { q: 'Pourquoi le paquet est-il si gros ?', a: ['L\'essentiel, c\'est le terrain : deux cartes de hauteur de 4096 × 4096, la taille qu\'utilise CS2, 64 Mo décompressées et environ 22 Mo dans le zip. Tout ce dont le mod CS2 a besoin est dans le paquet : rien d\'autre ne doit tourner sur votre ordinateur.'] },
    { q: 'Ma grande ville est-elle trop grosse pour l\'outil web ?', a: ['La carte et la création du paquet sont faites pour les grandes villes : une ville de test de 60 000 segments reste fluide, et le paquet est créé en arrière-plan.'] },
    { q: 'Puis-je utiliser l\'outil web en français ?', a: ['Oui : il suit la langue de votre navigateur, et le menu de langue en haut de la page passe de l\'anglais au français. Le rapport (`report.json`) reste en anglais.'] },
  ],

  MATRIX: [
    { area: 'Routes', cs1: 'Routes, autoroutes, sens uniques, routes Vanilla+ et du Workshop', cs2: 'Routes Road Builder générées', status: 'approx', notes: 'Voies, terre-pleins, stationnement, voies de bus et de tram, arbres et vitesse sont conservés ; les largeurs peuvent changer (trottoirs Road Builder de 7 m au plus).' },
    { area: 'Routes', cs1: 'Routes surélevées, ponts, rampes, tunnels', cs2: 'La même route Road Builder à la hauteur de CS1', status: 'yes', notes: 'Road Builder crée lui-même les formes surélevée, pont et tunnel.' },
    { area: 'Routes', cs1: 'Bandes cyclables peintes sur la chaussée', cs2: 'Trottoir avec piste cyclable', status: 'approx', notes: 'Au bord de la route seulement ; CS2 n\'a pas de bandes cyclables peintes sur la chaussée.' },
    { area: 'Rail et chemins', cs1: 'Voies de train, de métro et de tram', cs2: 'Voies CS2 doubles, à double sens ou à sens unique', status: 'yes', notes: '' },
    { area: 'Rail et chemins', cs1: 'Chemins piétons et pistes cyclables', cs2: 'Chemins pavés, pistes cyclables', status: 'yes', notes: '' },
    { area: 'Rail et chemins', cs1: 'Monorail, téléphérique', cs2: '–', status: 'no', notes: 'CS2 n\'a ni l\'un ni l\'autre.' },
    { area: 'IMT', cs1: 'Peinture rouge sur toute une voie / îlot pavé ou herbe sur toute une voie', cs2: 'Voie de bus / terre-plein dans la route Road Builder', status: 'yes', notes: 'Les remplissages partiels et la peinture grise sont signalés.' },
    { area: 'IMT', cs1: 'Remplissages pavés, herbe et gravier aux carrefours', cs2: 'Surfaces CS2 (pavés, herbe, sable)', status: 'yes', notes: 'Placées avec l\'étape des bâtiments.' },
    { area: 'IMT', cs1: 'Marquages peints aux carrefours', cs2: '–', status: 'report', notes: 'Pas encore d\'équivalent CS2 (décalcomanies prévues).' },
    { area: 'Terrain et eau', cs1: 'Carte de hauteur (et le terrain autour du carré)', cs2: 'Terrain CS2 aux mêmes hauteurs, plus la carte du monde autour', status: 'pending', notes: 'Dans le paquet (dossier terrain) ; l\'importeur ne l\'applique pas encore : les routes suivent le sol de la carte.' },
    { area: 'Terrain et eau', cs1: 'Niveau de la mer, rivières et lacs (sources d\'eau naturelles)', cs2: 'Niveau de la mer et sources d\'eau CS2', status: 'pending', notes: 'Dans le paquet ; pas encore appliqué par l\'importeur. CS2 simule à nouveau l\'eau ; des sources supplémentaires remplissent les rivières et lacs au-dessus de la mer.' },
    { area: 'Terrain et eau', cs1: 'Autoroutes et voies ferrées qui quittent le carré', cs2: 'Connexions extérieures CS2', status: 'pending', notes: 'Dans le paquet ; pas encore appliquées par l\'importeur.' },
    { area: 'Zonage', cs1: 'Zones résidentielles, commerciales, industrielles et de bureaux', cs2: 'Zones CS2 (thème européen ou nord-américain)', status: 'approx', notes: 'Les spécialisations de quartier deviennent des zones génériques.' },
    { area: 'Circulation (TM:PE)', cs1: 'Limites de vitesse, voies réservées aux bus', cs2: 'Variantes Road Builder avec cette vitesse / voie de bus', status: 'yes', notes: 'CS2 a une seule vitesse par route.' },
    { area: 'Circulation (TM:PE)', cs1: 'Feux, arrêts toutes directions, interdictions de tourner issues des flèches, passages piétons', cs2: 'Réglages de carrefour de CS2', status: 'yes', notes: '' },
    { area: 'Circulation (TM:PE)', cs1: 'Panneaux de priorité', cs2: 'Mod Traffic', status: 'yes', notes: 'Nécessite le mod Traffic dans CS2.' },
    { area: 'Circulation (TM:PE)', cs1: 'Flèches de voie, connexions de voies, demi-tours', cs2: 'Mod Traffic', status: 'pending', notes: 'Dans le paquet ; pas appliquées dans cette version (listées dans le rapport d\'import).' },
    { area: 'Circulation (TM:PE)', cs1: 'Feux programmés, virage au rouge, carrefours encombrés, règles de stationnement', cs2: '–', status: 'report', notes: 'Les feux programmés deviennent des feux ordinaires.' },
    { area: 'Node Controller', cs1: 'Types de nœud, décalages, marquages masqués', cs2: '–', status: 'report', notes: 'CS2 construit lui-même la géométrie des carrefours.' },
    { area: 'Transports', cs1: 'Lignes de bus et de tram', cs2: 'Lignes CS2 avec couleur, nom, numéro et arrêts', status: 'approx', notes: 'Trolleybus, bus touristiques et interurbains deviennent des lignes de bus.' },
    { area: 'Transports', cs1: 'Lignes de train, métro, bateau et avion', cs2: 'Lignes CS2 aux gares placées', status: 'approx', notes: 'Ont besoin de leurs gares, placées avec les bâtiments ; un dépôt par défaut est ajouté quand la ville n\'en a pas.' },
    { area: 'Bâtiments', cs1: 'Bâtiments de service, gares, dépôts, parcs, monuments', cs2: 'Bâtiment CS2 le plus proche (même service, puis capacité et taille)', status: 'approx', notes: 'Vérifiez ou changez chaque type dans l\'onglet Bâtiments, y compris ce qui se passe s\'il ne tient pas.' },
    { area: 'Bâtiments', cs1: 'Bâtiments des zones', cs2: 'Construits par CS2 sur les zones converties', status: 'yes', notes: 'Pas placés un par un.' },
    { area: 'Bâtiments', cs1: 'Bibliothèques, stations de taxi, arrêts de téléphérique', cs2: '–', status: 'no', notes: 'Pas d\'équivalent CS2.' },
    { area: 'Quartiers', cs1: 'Noms et contours des quartiers ; politiques', cs2: 'Quartiers et politiques CS2', status: 'approx', notes: 'Les politiques qui ont un équivalent CS2 (interdiction des poids lourds, recyclage…) ; les spécialisations de zone sont signalées.' },
    { area: 'Quartiers', cs1: 'Parcs, zones industrielles, campus, aéroports', cs2: '–', status: 'report', notes: 'Leurs bâtiments sont placés comme bâtiments isolés.' },
    { area: 'Autres', cs1: 'Tuyaux, lignes électriques', cs2: 'Posés par CS2 le long des routes', status: 'no', notes: 'CS2 pose l\'eau et l\'électricité avec les routes.' },
    { area: 'Autres', cs1: 'Arbres et rochers isolés', cs2: 'Arbres CS2 (même espèce, ou même genre et hauteur la plus proche) et rochers', status: 'yes', notes: 'Les arbres sur une route sont déplacés juste après son bord ; ceux dans le terrain d\'un bâtiment sont laissés de côté.' },
    { area: 'Autres', cs1: 'Autres décorations isolées (bancs, lampadaires, ornements)', cs2: '–', status: 'no', notes: 'Pas d\'équivalent CS2 raisonnable ; comptées dans le rapport.' },
  ],

  MD: {
    guideTitle: 'Guide d\'utilisation',
    guideIntro: 'D\'une ville Cities: Skylines à une ville Cities: Skylines II, étape par étape.',
    need: 'Ce qu\'il vous faut',
    privacy: 'Confidentialité',
    seeAlso: 'Voir aussi : [FAQ et dépannage](faq.md) · [Ce qui est converti](what-converts.md) · [English](../user-guide.md)',
    faqTitle: 'FAQ et dépannage',
    matrixTitle: 'Ce qui est converti',
    matrixIntro: 'Pour chaque ville, l\'outil web montre aussi ceci par catégorie avec vos chiffres (Aperçu → Ce qui est converti) et liste chaque approximation dans `report.json`.',
    matrixHead: '| CS1 | CS2 | État | Remarques |',
    generated: 'Généré depuis web/src/core/docs.fr.ts par `node web/scripts/gen-docs.mjs` ({src}). Modifiez la source, pas ce fichier.',
  },
};

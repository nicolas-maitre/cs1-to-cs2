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
        'CS2 1.6 ou plus récent, avec le mod **Road Builder** (les types de route qui correspondent à une route CS2 vanilla l\'utilisent ; les autres deviennent des routes Road Builder).',
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
        '**Routes** liste chaque type de route avec son équivalent CS2 : une route CS2 vanilla quand la route générée a les mêmes voies ("Small Road vanilla (correspondance)"), sinon une route Road Builder, avec la raison dans les deux cas ; vous pouvez en choisir un autre. **Préférer les routes CS2 vanilla** (dans les options, activé par défaut) fait cette correspondance ; désactivez-le pour que toutes les routes soient des routes Road Builder. **Types RB** montre les routes Road Builder générées et les changements de largeur.',
        '**Bâtiments** liste les bâtiments CS1 avec le bâtiment CS2 choisi pour chaque type, le score de correspondance et la raison ; choisissez-en un autre ou ignorez un type. Pour chaque type, vous pouvez aussi dire ce qui se passe s\'il ne tient pas à sa place. Par défaut, il n\'est pas déplacé : il glisse un peu ou prend une alternative plus petite, et s\'il ne tient toujours pas, le rapport d\'import le liste avec sa position CS1 pour que vous le placiez vous-même ("Ne pas le déplacer (me le lister pour que je le place)"). Vous pouvez à la place laisser un type chercher un terrain libre à proximité (jusqu\'à 400 m) ou n\'importe où dans le carré.',
        '**Rapport** liste tout ce qui est approché ou laissé de côté.',
      ],
    },
    {
      title: 'Téléchargez le paquet',
      body: [
        'Cliquez sur **Télécharger le paquet (.zip)**. La création prend quelques secondes : le terrain, ce sont deux cartes de hauteur de 4096 × 4096.',
        'Inutile de le décompresser : l\'importeur prend le zip et le décompresse lui-même. Il contient `cs1conv-plan.json`, `report.json`, les dossiers `roadbuilder` et `terrain` et un `README.txt`.',
      ],
    },
    {
      title: 'Installez les mods et les types de route dans CS2',
      body: [
        'Activez **Road Builder** et le **CS1 → CS2 Importer**. **Traffic** est facultatif : il pose les panneaux de priorité, les connexions de voies, les flèches de voie et les règles de demi-tour. La ligne en bas de la page de l\'importeur indique si Road Builder et Traffic sont actifs. Les libellés cités ici sont ceux du jeu en français ; si votre jeu est en anglais, voyez le guide anglais.',
        'Lancez CS2 et ouvrez Options → **CS1 → CS2 importer** (onglet **Import**). Cliquez sur **Parcourir…** et choisissez le zip téléchargé : l\'importeur le décompresse sous `%LOCALAPPDATA%\\cs1-to-cs2\\packages\\`, affiche "<zip> décompressé dans <dossier>." et le vérifie. **Ouvrir le dossier des paquets** montre ce dossier dans l\'Explorateur.',
        'Road Builder ne lit ses types de route qu\'au lancement du jeu : installez-les donc maintenant. Cliquez sur **Installer les configurations Road Builder**, confirmez, attendez "N configurations Road Builder copiées", puis **relancez le jeu**. (Ou, CS2 fermé, copiez les fichiers du dossier `roadbuilder` du paquet dans `%USERPROFILE%\\AppData\\LocalLow\\Colossal Order\\Cities Skylines II\\ModsData\\RoadBuilder\\` ; les configurations d\'autres villes converties peuvent y rester.)',
      ],
    },
    {
      title: 'Commencez une nouvelle ville',
      body: [
        'Lancez une nouvelle partie (pas l\'éditeur). Toutes les cartes conviennent, puisque l\'importeur remplace leur terrain ; Sunshine Peninsula a le moins de routes préconstruites à retirer.',
      ],
    },
    {
      title: 'Vérifiez le paquet',
      body: [
        'Ouvrez à nouveau Options → **CS1 → CS2 importer**. Dans le groupe **Paquet**, choisissez le paquet dans **Paquets sur ce PC** (les derniers utilisés en premier), ou utilisez **Parcourir…**, ou collez son dossier, son `cs1conv-plan.json` ou son zip dans **Dossier du paquet** et cliquez sur **Vérifier**.',
        'La ligne d\'état indique le contenu du paquet, par exemple "Paquet : Skibidi.02, 2035 routes à construire. Les 37 types de route sont disponibles." et "Carte : terrain avec ses alentours, niveau de la mer 182 m, 5 sources d\'eau, 20 connexions extérieures.". Ses dernières lignes disent si le terrain de cette ville est déjà celui du paquet, et si elle roule du même côté que la ville CS1 (dans une nouvelle ville, **Préparer la carte** règle le côté). S\'il manque des types de route, installez les configurations Road Builder et relancez le jeu.',
      ],
    },
    {
      title: 'Préparez la carte',
      body: [
        'Dans le groupe **Nouvelle ville**, cliquez sur **Préparer la carte** et confirmez. Il retire les routes, voies et bâtiments propres à la carte, remplace son terrain et ses environs par ceux de votre ville, règle le niveau de la mer et les sources d\'eau de CS1 (un paquet sans données d\'eau donne une carte sèche), et fait rouler la circulation du même côté que dans la ville CS1. **État** suit les étapes ("Préparation de la carte… application du terrain", "… mise en place de l\'eau") et se termine par "Carte préparée.", la vérification du terrain ("à 0 m près du paquet sur 95 % des 181 points de contrôle"), le côté ("La circulation roule à gauche, comme dans la ville CS1.") et "Ensuite : cliquez sur « Importer dans cette ville ».". Comptez 10 à 20 secondes.',
        'C\'est irréversible : utilisez-le dans une nouvelle ville. Sans ville chargée (menu principal, éditeur), il s\'arrête avec "Carte non préparée : Commencez d\'abord une nouvelle ville.". Un paquet exporté sans le terrain garde le terrain de la carte ("Terrain ignoré : le paquet n\'a pas de terrain").',
      ],
    },
    {
      title: 'Importez dans la ville',
      body: [
        'Dans le groupe **Importer dans cette ville**, choisissez les étapes : **Vider la zone d\'abord**, **Zonage**, **Feux, stops, interdictions de tourner, passages piétons**, **Panneaux de priorité (mod Traffic)**, **Connexions de voies (mod Traffic)**, **Bâtiments, arbres, quartiers**, **Lignes de transport** et, en dernier, **Connexions extérieures** sont activées par défaut ; les routes sont toujours construites en premier. **Garder le terrain de cette carte** est désactivé : l\'import refuse alors une ville dont le terrain n\'est pas celui du paquet ; activez-le pour importer quand même sur le sol de la carte (seules les tranchées de 4 m ou plus gardent leur profondeur).',
        'Cliquez sur **Importer dans cette ville** et confirmez. **État** montre l\'avancement ("Import… lot 3 sur 6", puis chaque étape) et se termine par un résumé par étape, par exemple "Terminé en 4.33 s : 2035 routes construites sur 2035", "Transports : 9 lignes sur 9, 145 arrêts sur 145." et "Connexions extérieures : 20 sur 20 (13 routières, 7 ferroviaires).". Une étape désactivée indique "désactivé dans les options". Le résumé donne aussi le chemin du rapport complet, sous `%LOCALAPPDATA%\\cs1-to-cs2\\import-reports\\`.',
        'Un carré de 3 km prend moins d\'une minute : les routes environ 4 secondes, les lignes de transport le plus longtemps. Le jeu reste fluide, et **Annuler** (groupe **État**) s\'arrête après l\'étape en cours ; ce qui est construit reste.',
      ],
    },
    {
      title: 'Après l\'import',
      body: [
        'Laissez le jeu tourner quelques minutes : CS2 remplit les rivières et les lacs à partir des sources d\'eau. Enregistrez ensuite la ville. Elle a encore besoin d\'eau, d\'électricité et de services pour grandir, comme toute nouvelle ville ; les bâtiments poussent ensuite sur les zones converties.',
        'Pour revenir en arrière, cliquez sur **Retirer l\'import** et confirmez : il supprime les routes, bâtiments, arbres et objets, quartiers et zones, lignes de transport et connexions extérieures créés par les imports, même après enregistrement et rechargement. Le zonage et les règles de circulation partent avec les routes ; la carte de base retirée par **Préparer la carte** ne revient pas.',
        'Lisez l\'onglet **Rapport** de l\'outil web (ou `report.json`) et le résumé de l\'import pour ce qui demande une intervention :',
        'les connexions de voies, flèches de voie et demi-tours s\'appliquent aux carrefours ; les divisions de voies là où une route se prolonge dans une autre sans carrefour restent telles que CS2 les construit (environ 8 % des connexions dans nos tests). Dans une ville qui roule à gauche, **Préparer la carte** règle le côté de circulation d\'après la ville CS1 ; les routes, les sens uniques et les connexions de voies suivent. Environ 7 % des cellules de zone ne trouvent pas de bloc de zone CS2, car les blocs de CS2 sont moins profonds. Les lignes de métro et de train ont besoin de leurs gares, placées par l\'étape des bâtiments ; une ligne de métro ou de train dont la voie reste dans le carré ne reçoit pas de dépôt : placez-en un vous-même (le rapport d\'import nomme la ligne). Quand deux routes CS1 quittent un carrefour avec un angle très faible, CS2 fusionne leurs premiers mètres en une seule route et les sépare un peu plus loin, connectées comme dans CS1. Quelques morceaux de CS1 qui se chevauchent et que CS2 ne peut pas placer (par exemple une route à quelques mètres d\'une route parallèle) sont laissés de côté et listés dans le rapport.',
      ],
    },
  ],

  FAQ: [
    { q: 'Ma ville est-elle envoyée quelque part ?', a: ['Non. L\'outil web tourne entièrement dans votre navigateur ; le fichier ne quitte jamais votre ordinateur. Il n\'y a pas de serveur derrière la page.'] },
    { q: 'Pourquoi ne puis-je pas convertir toute ma ville de 81 tuiles ?', a: ['La zone jouable d\'une carte CS2 mesure environ 14,3 km de côté, moins que les 81 tuiles de CS1 (17,3 km). L\'outil web vous laisse choisir le carré qui tient ; vous pouvez convertir un autre carré dans une autre ville.'] },
    { q: 'Pourquoi certaines routes sont-elles des routes CS2 vanilla et d\'autres des routes Road Builder ?', a: ['Quand la route que cs1conv générerait a les mêmes voies qu\'une route CS2 vanilla (mêmes voies de circulation et mêmes sens de chaque côté, chacune à 0,5 m près en largeur, stationnement et pistes cyclables des mêmes côtés, même terre-plein, largeur totale à 1 m près et même vitesse), la route vanilla est utilisée : moins de configurations Road Builder à installer, et des routes identiques à celles du jeu. Les arbres, les voies de bus et les autres changements venant d\'IMT ou de TM:PE gardent en général une route sur Road Builder. **Routes** donne la raison pour chaque type. Pour imposer Road Builder, choisissez **Road Builder** pour ce type, ou désactivez **Préférer les routes CS2 vanilla**.'] },
    { q: 'Certaines routes sont plus étroites ou plus larges que dans CS1. Pourquoi ?', a: ['Les types de route sont recréés avec Road Builder, qui a des largeurs de voie fixes et des trottoirs de 7 m au plus. Une route CS1 avec des trottoirs de 9 m devient quelques mètres plus étroite. Chaque changement figure sous **Types RB** et dans le rapport.'] },
    { q: 'La page dit qu\'elle ne peut pas ouvrir mon fichier.', a: ['Ouvrez le `.cs1x` écrit par l\'exporteur, pas une sauvegarde `.crp`. Si elle indique un format trop récent, rechargez la page pour avoir le dernier outil ; s\'il est trop ancien, mettez l\'exporteur à jour et exportez à nouveau.'] },
    { q: 'Une partie de mon export a échoué.', a: ['Le reste se convertit normalement. Exportez à nouveau ; si l\'échec persiste, le message de l\'exporteur se trouve dans l\'outil web sous Aperçu → Vérification du fichier.'] },
    { q: 'L\'importeur ne liste pas mon paquet.', a: ['**Paquets sur ce PC** liste les derniers paquets utilisés, puis les dossiers sous `%LOCALAPPDATA%\\cs1-to-cs2\\` qui contiennent un `cs1conv-plan.json`. Cliquez sur **Parcourir…** et choisissez le zip téléchargé (l\'importeur le décompresse sous `packages\\`) ou un `cs1conv-plan.json`, ou collez son chemin dans **Dossier du paquet**. **Ouvrir le dossier des paquets** ouvre ce dossier dans l\'Explorateur.'] },
    { q: 'L\'importeur dit que des types de route manquent.', a: ['Les routes Road Builder doivent être dans ModsData\\RoadBuilder **avant** le lancement de CS2. "Configurations Road Builder pas installées" : cliquez sur **Installer les configurations Road Builder**, puis relancez CS2. "Installées mais pas encore chargées" : relancez CS2. "Road Builder n\'est pas actif" : activez Road Builder dans votre playset et relancez. Cliquez ensuite à nouveau sur **Vérifier**. Les routes d\'un type manquant sont ignorées.'] },
    { q: 'Ma ville n\'a ni les collines, ni les rivières, ni la mer de CS1.', a: ['Cliquez sur **Préparer la carte** dans une nouvelle ville avant d\'importer : il remplace le terrain et les environs de la carte par ceux de votre ville et règle le niveau de la mer et les sources d\'eau. Laissez ensuite le jeu tourner quelques minutes pour que CS2 remplisse les rivières et les lacs. Un paquet créé sans données d\'eau donne une carte sèche.'] },
    { q: 'Où sont mes connexions de voies et flèches de voie TM:PE ?', a: ['**Connexions de voies (mod Traffic)** les recrée aux carrefours, avec les règles de demi-tour ; il est activé par défaut et nécessite le mod Traffic. Là où une route se prolonge dans une autre sans carrefour, CS2 garde sa propre continuité de voies, et les divisions de voies n\'y sont pas reproductibles. Le résumé de l\'import indique combien de références de voies CS1 ont été retrouvées.'] },
    { q: 'Comment annuler un import ?', a: ['Cliquez sur **Retirer l\'import** : il supprime les routes, bâtiments, arbres et objets, quartiers et zones, lignes de transport et connexions extérieures créés par les imports dans cette ville, même après enregistrement et rechargement. Le zonage et les règles de circulation partent avec les routes. La carte de base retirée par **Préparer la carte** ne revient pas ; commencez une nouvelle ville pour cela.'] },
    { q: 'L\'import dit que le terrain de cette ville n\'est pas celui du paquet.', a: ['Commencez une nouvelle ville et cliquez sur **Préparer la carte** avant d\'importer. Pour construire quand même sur le sol de la carte, activez **Garder le terrain de cette carte** : les routes suivent alors ce sol, et seules les tranchées de 4 m ou plus gardent leur profondeur CS1.'] },
    { q: 'Pourquoi tous mes bâtiments n\'ont-ils pas été placés ?', a: ['Les bâtiments des zones ne sont pas placés : CS2 construit les siens sur les zones converties. Les piliers, jonctions de tuyaux et poteaux sont reconstruits par CS2. Quelques types de bâtiment CS1 n\'ont pas d\'équivalent CS2 (bibliothèques, stations de taxi, arrêts de téléphérique), et les bâtiments sur des routes hors du carré sont laissés de côté. L\'onglet Bâtiments montre chaque type.'] },
    { q: 'Un bâtiment manque, ou s\'est retrouvé ailleurs (mon hôpital a bougé). Pourquoi ?', a: ['Les bâtiments CS2 ont souvent besoin de plus de place que leurs équivalents CS1. Quand l\'un d\'eux ne tient pas à sa place CS1, l\'importeur le fait glisser un peu ou essaie une alternative CS2 plus petite. S\'il ne tient toujours pas, il n\'est **pas déplacé** ailleurs : le rapport d\'import le liste comme "à placer vous-même" avec sa position CS1, et vous le placez à la main. Un bâtiment ne va ailleurs que si vous avez réglé son type, dans l\'onglet Bâtiments, pour chercher un terrain libre à proximité (jusqu\'à 400 m) ou n\'importe où dans le carré.'] },
    { q: 'Ma ligne de métro ou de train n\'a pas de véhicules.', a: ['Les lignes CS2 reçoivent leurs véhicules d\'un dépôt de leur type, et les lignes de métro et de train de CS1 n\'en ont pas besoin. Les dépôts ferroviaires ne sont jamais placés dans votre ville : par défaut, l\'importeur place un dépôt compact au bord du carré, là où la voie de la ligne en sort, et le relie. Une ligne dont la voie reste dans le carré n\'en reçoit pas ; le rapport d\'import la nomme, et vous placez un dépôt vous-même. Sous Aperçu → Options, vous pouvez choisir "Je le place moi-même" pour le métro ou le train. Les lignes de bus et de tram reçoivent un dépôt par défaut près de leur premier arrêt.'] },
    { q: 'Ai-je besoin du mod Traffic ?', a: ['Seulement pour les panneaux de priorité, connexions de voies, flèches de voie et demi-tours de TM:PE. Limites de vitesse, voies de bus, feux, arrêts toutes directions, interdictions de tourner et passages piétons fonctionnent sans lui.'] },
    { q: 'Que deviennent les feux programmés et les réglages de Node Controller ?', a: ['CS2 gère lui-même les phases des feux et construit lui-même les carrefours. Les feux programmés deviennent des feux ordinaires ; les réglages de Node Controller figurent dans le rapport.'] },
    { q: 'Bâtiments européens ou nord-américains ?', a: ['Choisissez le thème des zones sous Aperçu → Options. Il détermine les zones résidentielles et commerciales de CS2 utilisées, et les variantes d\'arbres préférées.'] },
    { q: 'L\'exporteur modifie-t-il ma sauvegarde CS1 ?', a: ['Non. Il ne fait que lire la ville chargée et écrire le fichier `.cs1x`.'] },
    { q: 'Pourquoi le paquet est-il si gros ?', a: ['L\'essentiel, c\'est le terrain : deux cartes de hauteur de 4096 × 4096, la taille qu\'utilise CS2, 64 Mo décompressées et environ 22 Mo dans le zip. Tout ce dont le mod CS2 a besoin est dans le paquet : rien d\'autre ne doit tourner sur votre ordinateur.'] },
    { q: 'Ma grande ville est-elle trop grosse pour l\'outil web ?', a: ['La carte et la création du paquet sont faites pour les grandes villes : une ville de test de 60 000 segments reste fluide, et le paquet est créé en arrière-plan.'] },
    { q: 'Puis-je utiliser l\'outil web en français ?', a: ['Oui : il suit la langue de votre navigateur, et le menu de langue en haut de la page passe de l\'anglais au français. Le rapport (`report.json`) reste en anglais.'] },
  ],

  MATRIX: [
    { area: 'Routes', cs1: 'Routes, autoroutes, sens uniques, routes Vanilla+ et du Workshop', cs2: 'Une route CS2 vanilla équivalente, sinon une route Road Builder générée', status: 'approx', notes: 'Voies, terre-pleins, stationnement, voies de bus et de tram, arbres et vitesse sont conservés ; les largeurs peuvent changer (trottoirs Road Builder de 7 m au plus). Une route vanilla n\'est utilisée que si les voies correspondent (chacune à 0,5 m près, largeur totale à 1 m près).' },
    { area: 'Routes', cs1: 'Routes surélevées, ponts, rampes, tunnels', cs2: 'La même route Road Builder à la hauteur de CS1', status: 'yes', notes: 'Road Builder crée lui-même les formes surélevée, pont et tunnel.' },
    { area: 'Routes', cs1: 'Bandes cyclables peintes sur la chaussée', cs2: 'Trottoir avec piste cyclable', status: 'approx', notes: 'Au bord de la route seulement ; CS2 n\'a pas de bandes cyclables peintes sur la chaussée.' },
    { area: 'Rail et chemins', cs1: 'Voies de train, de métro et de tram', cs2: 'Voies CS2 doubles, à double sens ou à sens unique', status: 'yes', notes: '' },
    { area: 'Rail et chemins', cs1: 'Chemins piétons et pistes cyclables', cs2: 'Chemins pavés, pistes cyclables', status: 'yes', notes: '' },
    { area: 'Rail et chemins', cs1: 'Monorail, téléphérique', cs2: '–', status: 'no', notes: 'CS2 n\'a ni l\'un ni l\'autre.' },
    { area: 'IMT', cs1: 'Peinture rouge sur toute une voie / îlot pavé ou herbe sur toute une voie', cs2: 'Voie de bus / terre-plein dans la route Road Builder', status: 'yes', notes: 'Les remplissages partiels et la peinture grise sont signalés.' },
    { area: 'IMT', cs1: 'Remplissages pavés, herbe et gravier aux carrefours', cs2: 'Surfaces CS2 (pavés, herbe, sable)', status: 'yes', notes: 'Placées avec l\'étape des bâtiments.' },
    { area: 'IMT', cs1: 'Marquages peints aux carrefours', cs2: '–', status: 'report', notes: 'Pas encore d\'équivalent CS2 (décalcomanies prévues).' },
    { area: 'Terrain et eau', cs1: 'Carte de hauteur (et le terrain autour du carré)', cs2: 'Terrain CS2 aux mêmes hauteurs, plus la carte du monde autour', status: 'yes', notes: 'Appliqué par Prepare map de l\'importeur, dans une nouvelle ville, avant l\'import.' },
    { area: 'Terrain et eau', cs1: 'Niveau de la mer, rivières et lacs (sources d\'eau naturelles)', cs2: 'Niveau de la mer et sources d\'eau CS2', status: 'approx', notes: 'Réglés par Prepare map. CS2 simule à nouveau l\'eau ; des sources supplémentaires remplissent les rivières et lacs au-dessus de la mer. Pompes et rejets viennent avec leurs bâtiments.' },
    { area: 'Terrain et eau', cs1: 'Autoroutes et voies ferrées qui quittent le carré', cs2: 'Connexions extérieures CS2', status: 'yes', notes: 'Dernière étape de l\'importeur (Outside connections), pour que voitures et trains puissent entrer dans la ville et en sortir.' },
    { area: 'Zonage', cs1: 'Zones résidentielles, commerciales, industrielles et de bureaux', cs2: 'Zones CS2 (thème européen ou nord-américain)', status: 'approx', notes: 'Les spécialisations de quartier deviennent des zones génériques.' },
    { area: 'Circulation (TM:PE)', cs1: 'Limites de vitesse, voies réservées aux bus', cs2: 'Variantes Road Builder avec cette vitesse / voie de bus', status: 'yes', notes: 'CS2 a une seule vitesse par route.' },
    { area: 'Circulation (TM:PE)', cs1: 'Feux, arrêts toutes directions, interdictions de tourner issues des flèches, passages piétons', cs2: 'Réglages de carrefour de CS2', status: 'yes', notes: '' },
    { area: 'Circulation (TM:PE)', cs1: 'Panneaux de priorité', cs2: 'Mod Traffic', status: 'yes', notes: 'Nécessite le mod Traffic dans CS2.' },
    { area: 'Circulation (TM:PE)', cs1: 'Flèches de voie, connexions de voies, demi-tours', cs2: 'Connexions de voies et demi-tours du mod Traffic', status: 'approx', notes: 'Appliqués exactement aux carrefours (Connexions de voies (mod Traffic), activé par défaut), que la ville roule à droite ou à gauche ; les divisions de voies là où une route se prolonge sans carrefour restent telles que CS2 les construit.' },
    { area: 'Circulation (TM:PE)', cs1: 'Feux programmés, virage au rouge, carrefours encombrés, règles de stationnement', cs2: '–', status: 'report', notes: 'Les feux programmés deviennent des feux ordinaires.' },
    { area: 'Node Controller', cs1: 'Types de nœud, décalages, marquages masqués', cs2: '–', status: 'report', notes: 'CS2 construit lui-même la géométrie des carrefours.' },
    { area: 'Transports', cs1: 'Lignes de bus et de tram', cs2: 'Lignes CS2 avec couleur, nom, numéro et arrêts', status: 'approx', notes: 'Trolleybus, bus touristiques et interurbains deviennent des lignes de bus.' },
    { area: 'Transports', cs1: 'Lignes de train, métro, bateau et avion', cs2: 'Lignes CS2 aux gares placées', status: 'approx', notes: 'Ont besoin de leurs gares, placées avec les bâtiments. Les dépôts de métro et de train vont au bord de la découpe, là où la voie en sort (ou vous les placez) ; jamais dans la ville.' },
    { area: 'Bâtiments', cs1: 'Bâtiments de service, gares, dépôts, parcs, monuments', cs2: 'Bâtiment CS2 le plus proche (même service, puis capacité et taille)', status: 'approx', notes: 'Vérifiez ou changez chaque type dans l\'onglet Bâtiments. Un bâtiment qui ne tient pas à sa place n\'est pas déplacé par défaut : il vous est listé pour que vous le placiez.' },
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

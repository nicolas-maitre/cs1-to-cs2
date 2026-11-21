<!-- Généré depuis web/src/core/docs.fr.ts par `node web/scripts/gen-docs.mjs` (MATRIX). Modifiez la source, pas ce fichier. -->

# Ce qui est converti

Pour chaque ville, l'outil web montre aussi ceci par catégorie avec vos chiffres (Aperçu → Ce qui est converti) et liste chaque approximation dans `report.json`.


## Routes

| CS1 | CS2 | État | Remarques |
| --- | --- | --- | --- |
| Routes, autoroutes, sens uniques, routes Vanilla+ et du Workshop | Une route CS2 vanilla équivalente, sinon une route Road Builder générée | converti, approché | Voies, terre-pleins, stationnement, voies de bus et de tram, arbres et vitesse sont conservés ; les largeurs peuvent changer (trottoirs Road Builder de 7 m au plus). Une route vanilla n'est utilisée que si les voies correspondent (chacune à 0,5 m près, largeur totale à 1 m près). |
| Routes surélevées, ponts, rampes, tunnels | La même route Road Builder à la hauteur de CS1 | converti | Road Builder crée lui-même les formes surélevée, pont et tunnel. |
| Bandes cyclables peintes sur la chaussée | Trottoir avec piste cyclable | converti, approché | Au bord de la route seulement ; CS2 n'a pas de bandes cyclables peintes sur la chaussée. |

## Rail et chemins

| CS1 | CS2 | État | Remarques |
| --- | --- | --- | --- |
| Voies de train, de métro et de tram | Voies CS2 doubles, à double sens ou à sens unique | converti |   |
| Chemins piétons et pistes cyclables | Chemins pavés, pistes cyclables | converti |   |
| Monorail, téléphérique | – | non converti | CS2 n'a ni l'un ni l'autre. |

## IMT

| CS1 | CS2 | État | Remarques |
| --- | --- | --- | --- |
| Peinture rouge sur toute une voie / îlot pavé ou herbe sur toute une voie | Voie de bus / terre-plein dans la route Road Builder | converti | Les remplissages partiels et la peinture grise sont signalés. |
| Remplissages pavés, herbe et gravier aux carrefours | Surfaces CS2 (pavés, herbe, sable) | converti | Placées avec l'étape des bâtiments. |
| Marquages peints aux carrefours | – | seulement dans le rapport | Pas encore d'équivalent CS2 (décalcomanies prévues). |

## Terrain et eau

| CS1 | CS2 | État | Remarques |
| --- | --- | --- | --- |
| Carte de hauteur (et le terrain autour du carré) | Terrain CS2 aux mêmes hauteurs, plus la carte du monde autour | converti | Appliqué par Prepare map de l'importeur, dans une nouvelle ville, avant l'import. |
| Sol modelé autour des routes (CS1 l'aplanit sous ses routes) | Profils des routes gravés dans la carte de hauteur CS2, avec talus | converti | Par type de route dans l'onglet Routes : le terrain suit la route (rues, voies ferrées), hauteur CS1 avec murs de soutènement (autoroutes) ou la route suit le terrain (chemins). |
| Niveau de la mer, rivières et lacs (sources d'eau naturelles) | Niveau de la mer et sources d'eau CS2 | converti, approché | Réglés par Prepare map. CS2 simule à nouveau l'eau ; des sources supplémentaires remplissent les rivières et lacs au-dessus de la mer. Pompes et rejets viennent avec leurs bâtiments. |
| Autoroutes et voies ferrées qui quittent le carré | Connexions extérieures CS2 | converti | Dernière étape de l'importeur (Outside connections), pour que voitures et trains puissent entrer dans la ville et en sortir. |

## Zonage

| CS1 | CS2 | État | Remarques |
| --- | --- | --- | --- |
| Zones résidentielles, commerciales, industrielles et de bureaux | Zones CS2 (thème européen ou nord-américain) | converti, approché | Les spécialisations de quartier deviennent des zones génériques. |

## Circulation (TM:PE)

| CS1 | CS2 | État | Remarques |
| --- | --- | --- | --- |
| Limites de vitesse, voies réservées aux bus | Variantes Road Builder avec cette vitesse / voie de bus | converti | CS2 a une seule vitesse par route. |
| Feux, arrêts toutes directions, interdictions de tourner issues des flèches, passages piétons | Réglages de carrefour de CS2 | converti |   |
| Panneaux de priorité | Mod Traffic | converti | Nécessite le mod Traffic dans CS2. |
| Flèches de voie, connexions de voies, demi-tours | Connexions de voies et demi-tours du mod Traffic | converti, approché | Appliqués exactement aux carrefours (Connexions de voies (mod Traffic), activé par défaut), que la ville roule à droite ou à gauche ; les divisions de voies là où une route se prolonge sans carrefour restent telles que CS2 les construit. |
| Feux programmés, virage au rouge, carrefours encombrés, règles de stationnement | – | seulement dans le rapport | Les feux programmés deviennent des feux ordinaires. |

## Node Controller

| CS1 | CS2 | État | Remarques |
| --- | --- | --- | --- |
| Types de nœud, décalages, marquages masqués | – | seulement dans le rapport | CS2 construit lui-même la géométrie des carrefours. |

## Transports

| CS1 | CS2 | État | Remarques |
| --- | --- | --- | --- |
| Lignes de bus et de tram | Lignes CS2 avec couleur, nom, numéro et arrêts | converti, approché | Trolleybus, bus touristiques et interurbains deviennent des lignes de bus. |
| Lignes de train, métro, bateau et avion | Lignes CS2 aux gares placées | converti, approché | Ont besoin de leurs gares, placées avec les bâtiments. Les dépôts de métro et de train vont au bord de la découpe, là où la voie en sort (ou vous les placez) ; jamais dans la ville. |

## Bâtiments

| CS1 | CS2 | État | Remarques |
| --- | --- | --- | --- |
| Bâtiments de service, gares, dépôts, parcs, monuments | Bâtiment CS2 le plus proche (même service, puis capacité et taille) | converti, approché | Vérifiez ou changez chaque type dans l'onglet Bâtiments. Un bâtiment qui ne tient pas à sa place n'est pas déplacé par défaut : il vous est listé pour que vous le placiez. |
| Bâtiments des zones | Construits par CS2 sur les zones converties | converti | Pas placés un par un. |
| Bibliothèques, stations de taxi, arrêts de téléphérique | – | non converti | Pas d'équivalent CS2. |

## Quartiers

| CS1 | CS2 | État | Remarques |
| --- | --- | --- | --- |
| Noms et contours des quartiers ; politiques | Quartiers et politiques CS2 | converti, approché | Les politiques qui ont un équivalent CS2 (interdiction des poids lourds, recyclage…) ; les spécialisations de zone sont signalées. |
| Parcs, zones industrielles, campus, aéroports | – | seulement dans le rapport | Leurs bâtiments sont placés comme bâtiments isolés. |

## Autres

| CS1 | CS2 | État | Remarques |
| --- | --- | --- | --- |
| Tuyaux, lignes électriques | Posés par CS2 le long des routes | non converti | CS2 pose l'eau et l'électricité avec les routes. |
| Arbres et rochers isolés | Arbres CS2 (même espèce, ou même genre et hauteur la plus proche) et rochers | converti | Les arbres sur une route sont déplacés juste après son bord ; ceux dans le terrain d'un bâtiment sont laissés de côté. |
| Autres décorations isolées (bancs, lampadaires, ornements) | – | non converti | Pas d'équivalent CS2 raisonnable ; comptées dans le rapport. |

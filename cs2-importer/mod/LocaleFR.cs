// French texts of the Options page (fr-FR), the same keys as LocaleEN.cs. Terms follow the web tool's French
// (web/src/core/i18n/fr.ts): paquet, zonage, quartiers, connexions extérieures, configurations Road Builder…
using System.Collections.Generic;

namespace Cs2ImporterMod
{
    public class LocaleFR : ModLocale
    {
        public LocaleFR(Setting s) : base(s) { }
        protected override bool French { get { return true; } }

        protected override Dictionary<string, string> Entries(Setting s)
        {
            return new Dictionary<string, string>
            {
                { s.GetSettingsLocaleID(), "CS1 → CS2 importer" },
                { s.GetOptionTabLocaleID(Setting.Section), "Import" },
                { s.GetOptionGroupLocaleID(Setting.PackageGroup), "Paquet" },
                { s.GetOptionGroupLocaleID(Setting.MapGroup), "Nouvelle ville" },
                { s.GetOptionGroupLocaleID(Setting.ImportGroup), "Importer dans cette ville" },
                { s.GetOptionGroupLocaleID(Setting.ResultGroup), "État" },

                { s.GetOptionLabelLocaleID(nameof(Setting.PackageFolder)), "Dossier du paquet" },
                { s.GetOptionDescLocaleID(nameof(Setting.PackageFolder)), "Le paquet d'import de l'outil web cs1conv : son dossier, son cs1conv-plan.json ou le .zip téléchargé (décompressé dans %LOCALAPPDATA%\\cs1-to-cs2\\packages). Collez un chemin, avec ou sans guillemets, ou utilisez Parcourir…." },
                { s.GetOptionLabelLocaleID(nameof(Setting.FoundPackage)), "Paquets sur ce PC" },
                { s.GetOptionDescLocaleID(nameof(Setting.FoundPackage)), "Les derniers paquets utilisés, puis ceux trouvés dans %LOCALAPPDATA%\\cs1-to-cs2. En choisir un remplit le dossier et le vérifie." },
                { s.GetOptionLabelLocaleID(nameof(Setting.Browse)), "Parcourir…" },
                { s.GetOptionDescLocaleID(nameof(Setting.Browse)), "Ouvre la fenêtre de choix de fichier de Windows : choisissez le cs1conv-plan.json d'un paquet ou le .zip téléchargé depuis l'outil web. Le jeu attend tant que la fenêtre est ouverte." },
                { s.GetOptionLabelLocaleID(nameof(Setting.OpenPackagesFolder)), "Ouvrir le dossier des paquets" },
                { s.GetOptionDescLocaleID(nameof(Setting.OpenPackagesFolder)), "Ouvre %LOCALAPPDATA%\\cs1-to-cs2\\packages dans l'Explorateur. Les paquets décompressés là apparaissent dans « Paquets sur ce PC »." },
                { s.GetOptionLabelLocaleID(nameof(Setting.Check)), "Vérifier" },
                { s.GetOptionDescLocaleID(nameof(Setting.Check)), "Lit le paquet et vérifie que chaque type de route qu'il utilise est disponible dans ce jeu, y compris ses routes Road Builder, et si cette ville a été préparée pour lui." },
                { s.GetOptionLabelLocaleID(nameof(Setting.InstallRoadBuilderConfigs)), "Installer les configurations Road Builder" },
                { s.GetOptionDescLocaleID(nameof(Setting.InstallRoadBuilderConfigs)), "Copie les routes Road Builder générées du paquet (*-cs1conv.json) dans ModsData\\RoadBuilder. Road Builder ne les lit qu'au lancement : relancez le jeu ensuite." },
                { s.GetOptionWarningLocaleID(nameof(Setting.InstallRoadBuilderConfigs)), "Copier les configurations Road Builder du paquet ? Les configurations existantes de même nom sont remplacées ; il faudra relancer le jeu." },

                { s.GetOptionLabelLocaleID(nameof(Setting.PrepareMap)), "Préparer la carte" },
                { s.GetOptionDescLocaleID(nameof(Setting.PrepareMap)), "Pour une nouvelle ville, avant l'import : supprime les routes, voies ferrées et bâtiments de la carte, puis remplace son terrain et ses alentours par ceux du paquet et règle le niveau de la mer et les sources d'eau de CS1." },
                { s.GetOptionWarningLocaleID(nameof(Setting.PrepareMap)), "Supprimer toutes les routes, voies ferrées et bâtiments de cette carte et remplacer son terrain et son eau par ceux du paquet ? C'est irréversible." },

                { s.GetOptionLabelLocaleID(nameof(Setting.ClearArea)), "Vider la zone d'abord" },
                { s.GetOptionDescLocaleID(nameof(Setting.ClearArea)), "Rase les routes déjà présentes dans la zone du paquet (plus 50 m) avant de construire. Les routes appartenant à des bâtiments restent. Les routes importées ne peuvent pas croiser des routes existantes." },
                { s.GetOptionLabelLocaleID(nameof(Setting.KeepMapTerrain)), "Garder le terrain de cette carte" },
                { s.GetOptionDescLocaleID(nameof(Setting.KeepMapTerrain)), "Importe dans une ville qui n'a pas été préparée avec le terrain du paquet. Les routes suivent alors le sol de cette carte ; seules les tranchées de 4 m ou plus gardent leur profondeur. Désactivé : l'import refuse une ville dont le terrain n'est pas celui du paquet." },
                { s.GetOptionLabelLocaleID(nameof(Setting.AllowOtherDrivingSide)), "Importer dans une ville qui roule de l'autre côté" },
                { s.GetOptionDescLocaleID(nameof(Setting.AllowOtherDrivingSide)), "Importe un paquet dans une ville où l'on roule de l'autre côté de la route que dans la ville CS1. Routes, sens uniques et connexions de voies sont alors inversés. Désactivé : l'import refuse une telle ville ; commencez une nouvelle ville et cliquez sur Préparer la carte, qui règle le bon côté." },
                { s.GetOptionLabelLocaleID(nameof(Setting.ApplyZoning)), "Zonage" },
                { s.GetOptionDescLocaleID(nameof(Setting.ApplyZoning)), "Après les routes, peint les zones de CS1 sur les nouveaux blocs de zonage." },
                { s.GetOptionLabelLocaleID(nameof(Setting.ApplyTrafficSettings)), "Feux, stops, interdictions de tourner, passages piétons" },
                { s.GetOptionDescLocaleID(nameof(Setting.ApplyTrafficSettings)), "Après les routes, applique les réglages des carrefours de CS1 avec les améliorations de route de CS2." },
                { s.GetOptionLabelLocaleID(nameof(Setting.ApplyTrafficMod)), "Panneaux de priorité (mod Traffic)" },
                { s.GetOptionDescLocaleID(nameof(Setting.ApplyTrafficMod)), "Après les routes, pose les panneaux de priorité de TM:PE avec le mod Traffic. Ignoré si le mod Traffic n'est pas installé." },
                { s.GetOptionLabelLocaleID(nameof(Setting.ApplyLaneConnections)), "Connexions de voies (mod Traffic)" },
                { s.GetOptionDescLocaleID(nameof(Setting.ApplyLaneConnections)), "Après les panneaux de priorité, recrée exactement aux carrefours les connexions de voies, les flèches de voie et les règles de demi-tour de TM:PE avec le mod Traffic. Là où une route se prolonge simplement dans une autre, CS2 garde sa propre continuité des voies. Ignoré si le mod Traffic n'est pas installé." },
                { s.GetOptionLabelLocaleID(nameof(Setting.ApplyBuildings)), "Bâtiments, arbres, quartiers" },
                { s.GetOptionDescLocaleID(nameof(Setting.ApplyBuildings)), "Après les routes et les zones, place les bâtiments de service, parcs et monuments de CS1, les arbres et objets, et les quartiers avec leurs politiques." },
                { s.GetOptionLabelLocaleID(nameof(Setting.ApplyTransit)), "Lignes de transport" },
                { s.GetOptionDescLocaleID(nameof(Setting.ApplyTransit)), "Après les bâtiments, crée les arrêts de bus et de tram de CS1 et les lignes avec leurs noms, numéros et couleurs. Les lignes de métro et de train ont besoin des gares de l'étape des bâtiments." },
                { s.GetOptionLabelLocaleID(nameof(Setting.ApplyConnections)), "Connexions extérieures" },
                { s.GetOptionDescLocaleID(nameof(Setting.ApplyConnections)), "En dernier, transforme les extrémités des autoroutes et voies ferrées de CS1 au bord de la zone du paquet en connexions extérieures de CS2, pour que la circulation et les trains puissent entrer et sortir." },
                { s.GetOptionLabelLocaleID(nameof(Setting.Import)), "Importer dans cette ville" },
                { s.GetOptionDescLocaleID(nameof(Setting.Import)), "Construit les routes du paquet dans la ville chargée, puis exécute les étapes activées ci-dessus, dans cet ordre. « Retirer l'import » peut supprimer ce qu'il crée." },
                { s.GetOptionWarningLocaleID(nameof(Setting.Import)), "Construire le paquet dans cette ville maintenant ?" },
                { s.GetOptionLabelLocaleID(nameof(Setting.RemoveImported)), "Retirer l'import" },
                { s.GetOptionDescLocaleID(nameof(Setting.RemoveImported)), "Supprime tout ce que les imports ont créé dans cette ville : routes, bâtiments, arbres et objets, quartiers et zones, lignes de transport et connexions extérieures (le zonage et les règles de circulation partent avec les routes). Fonctionne aussi après une sauvegarde et un rechargement. La carte d'origine supprimée par Préparer la carte ne revient pas." },
                { s.GetOptionWarningLocaleID(nameof(Setting.RemoveImported)), "Supprimer tout ce que les imports ont créé dans cette ville ?" },

                { s.GetOptionLabelLocaleID(nameof(Setting.Cancel)), "Annuler" },
                { s.GetOptionDescLocaleID(nameof(Setting.Cancel)), "Arrête Préparer la carte, l'import ou Retirer l'import après l'étape ou le lot de routes en cours. Ce qui est fait reste ; « Retirer l'import » supprime la partie importée." },
            };
        }
    }
}

// Status and error texts in English and French (the Options labels themselves are in LocaleEN.cs / LocaleFR.cs, where the
// web tool's guide test reads them). The language follows the game's: French for any fr-* locale, English otherwise.
// French terms follow the web tool's (web/src/core/i18n/fr.ts): paquet, zonage, quartiers, connexions extérieures…
using System;
using System.Collections.Generic;
using System.Globalization;
using Game.SceneFlow;

namespace Cs2ImporterMod
{
    public static class T
    {
        /// <summary>Forced language for scripts and tests ("en" / "fr"); null follows the game.</summary>
        public static string Override;

        public static bool Fr
        {
            get
            {
                if (Override != null) return Override == "fr";
                try
                {
                    var id = GameManager.instance?.localizationManager?.activeLocaleId;
                    return id != null && id.StartsWith("fr", StringComparison.OrdinalIgnoreCase);
                }
                catch { return false; }
            }
        }

        public static string S(string key, params object[] args) { return In(Fr, key, args); }

        /// <summary>A text in a given language (the locale sources build both).</summary>
        public static string In(bool fr, string key, params object[] args)
        {
            string[] t;
            if (!Table.TryGetValue(key, out t)) return key;
            var f = fr && t.Length > 1 ? t[1] : t[0];
            return args.Length == 0 ? f : string.Format(CultureInfo.InvariantCulture, f, args);
        }

        /// <summary>A step's display name in the status line.</summary>
        public static string Step(string key) { return Table.ContainsKey("step." + key) ? S("step." + key) : key; }

        static readonly Dictionary<string, string[]> Table = new Dictionary<string, string[]>
        {
            { "status.initial", new[] { "Pick a package, then press Check.", "Choisissez un paquet, puis cliquez sur Vérifier." } },
            { "about", new[] { "CS1 → CS2 Importer {0}. Road Builder: {1}. Traffic: {2}.", "CS1 → CS2 Importer {0}. Road Builder : {1}. Traffic : {2}." } },
            { "about.running", new[] { "running", "actif" } },
            { "about.missing", new[] { "not running", "inactif" } },

            // packages
            { "err.noPackage", new[] { "Choose a package first: Browse…, Packages on this PC, or paste its path into Package folder.", "Choisissez d'abord un paquet : Parcourir…, Paquets sur ce PC, ou collez son chemin dans Dossier du paquet." } },
            { "err.noPlan", new[] { "No cs1conv-plan.json in {0}. Choose the folder the web tool's zip was unpacked to, or the zip itself.", "Pas de cs1conv-plan.json dans {0}. Choisissez le dossier où le zip de l'outil web a été décompressé, ou le zip lui-même." } },
            { "err.notPackage", new[] { "This isn't a CS1 → CS2 import package ({0}). Choose the folder or the zip the web tool downloaded.", "Ce n'est pas un paquet d'import CS1 → CS2 ({0}). Choisissez le dossier ou le zip téléchargé depuis l'outil web." } },
            { "err.newerFormat", new[] { "This package uses format version {0}, from a newer web tool; this mod reads version {1}. Update the CS1 → CS2 Importer mod.", "Ce paquet est au format {0}, celui d'un outil web plus récent ; ce mod lit le format {1}. Mettez à jour le mod CS1 → CS2 Importer." } },
            { "err.olderFormat", new[] { "This package uses format version {0}, which this mod doesn't read (it reads version {1}). Make the package again with the current web tool.", "Ce paquet est au format {0}, que ce mod ne lit pas (il lit le format {1}). Recréez le paquet avec l'outil web actuel." } },
            { "err.damaged", new[] { "The package's plan can't be read ({0}). Download or unpack the package again.", "Le plan du paquet est illisible ({0}). Téléchargez ou décompressez le paquet à nouveau." } },
            { "err.zip", new[] { "The zip couldn't be unpacked: {0}", "Impossible de décompresser le zip : {0}" } },
            { "err.zipNoPlan", new[] { "The zip has no cs1conv-plan.json: it isn't an import package from the web tool.", "Le zip ne contient pas de cs1conv-plan.json : ce n'est pas un paquet d'import de l'outil web." } },
            { "err.noCity", new[] { "Load or start a city first.", "Chargez ou commencez d'abord une ville." } },
            { "err.noNewCity", new[] { "Start a new city first.", "Commencez d'abord une nouvelle ville." } },
            { "err.busy", new[] { "An import is already running.", "Un import est déjà en cours." } },
            { "err.rbNotLoaded", new[] { "Road Builder isn't running: subscribe to Road Builder on Paradox Mods, enable it in your playset, then restart Cities: Skylines II. The package's own road types need it.", "Road Builder n'est pas actif : abonnez-vous à Road Builder sur Paradox Mods, activez-le dans votre playset, puis relancez Cities: Skylines II. Les types de route du paquet en ont besoin." } },
            { "err.rbFolderMissing", new[] { "Road Builder's configs folder doesn't exist yet ({0}): press \"Install Road Builder configs\" (it creates it), then restart Cities: Skylines II.", "Le dossier des configurations Road Builder n'existe pas encore ({0}) : cliquez sur « Installer les configurations Road Builder » (il le crée), puis relancez Cities: Skylines II." } },
            { "err.sideLeft", new[] { "This package is for a left-hand city; this city drives on the right. Start a new city and press \"Prepare map\". To import anyway, turn on \"Import into a city driving on the other side\".", "Ce paquet est pour une ville où l'on roule à gauche ; cette ville roule à droite. Commencez une nouvelle ville et cliquez sur « Préparer la carte ». Pour importer quand même, activez « Importer dans une ville qui roule de l'autre côté »." } },
            { "err.sideRight", new[] { "This package is for a right-hand city; this city drives on the left. Start a new city and press \"Prepare map\". To import anyway, turn on \"Import into a city driving on the other side\".", "Ce paquet est pour une ville où l'on roule à droite ; cette ville roule à gauche. Commencez une nouvelle ville et cliquez sur « Préparer la carte ». Pour importer quand même, activez « Importer dans une ville qui roule de l'autre côté »." } },
            { "err.notPrepared", new[] { "This city's terrain isn't the package's (it differs by {0} m at the check points). Start a new city and press \"Prepare map\" first. To import onto this map's own terrain anyway, turn on \"Keep this map's terrain\".", "Le terrain de cette ville n'est pas celui du paquet (écart de {0} m aux points de contrôle). Commencez une nouvelle ville et cliquez d'abord sur « Préparer la carte ». Pour importer quand même sur le terrain de cette carte, activez « Garder le terrain de cette carte »." } },
            { "pick.title", new[] { "Choose a CS1 → CS2 import package (cs1conv-plan.json or .zip)", "Choisissez un paquet d'import CS1 → CS2 (cs1conv-plan.json ou .zip)" } },
            { "pick.filter", new[] { "Import package", "Paquet d'import" } },
            { "pick.all", new[] { "All files", "Tous les fichiers" } },
            { "pick.none", new[] { "No package chosen.", "Aucun paquet choisi." } },
            { "pick.unavailable", new[] { "The file dialog couldn't open ({0}): paste the package's path into \"Package folder\" instead.", "La fenêtre de choix de fichier n'a pas pu s'ouvrir ({0}) : collez plutôt le chemin du paquet dans « Dossier du paquet »." } },
            { "pick.unpacked", new[] { "Unpacked {0} to {1}.", "{0} décompressé dans {1}." } },
            { "pick.choose", new[] { "(choose a package found on this PC)", "(choisissez un paquet trouvé sur ce PC)" } },
            { "pick.recent", new[] { "Recent: {0}", "Récent : {0}" } },

            // check
            { "check.failed", new[] { "Check failed: {0}", "Échec de la vérification : {0}" } },
            { "check.package", new[] { "Package: {0}, {1} roads to build", "Paquet : {0}, {1} routes à construire" } },
            { "check.skipped", new[] { " ({0} skipped)", " ({0} ignorées)" } },
            { "check.allTypes", new[] { "All {0} road types are available.", "Les {0} types de route sont disponibles." } },
            { "check.missing", new[] { "{0} road types are missing.", "{0} types de route manquent." } },
            { "check.rbMissingFiles", new[] { "{0} Road Builder configs aren't installed: press \"Install Road Builder configs\", then restart Cities: Skylines II (Road Builder only reads its configs at startup).", "{0} configurations Road Builder ne sont pas installées : cliquez sur « Installer les configurations Road Builder », puis relancez Cities: Skylines II (Road Builder ne lit ses configurations qu'au lancement)." } },
            { "check.rbNotLoaded", new[] { "{0} Road Builder configs are installed but not loaded yet: restart Cities: Skylines II.", "{0} configurations Road Builder sont installées mais pas encore chargées : relancez Cities: Skylines II." } },
            { "check.otherMissing", new[] { "Other missing types are listed in the log.", "Les autres types manquants sont listés dans le journal." } },
            { "check.missingSkipped", new[] { "Roads of a missing type are skipped.", "Les routes d'un type manquant sont ignorées." } },
            { "check.existing", new[] { "{0} roads already in the area: keep \"Clear the area first\" on.", "{0} routes déjà dans la zone : laissez « Vider la zone d'abord » activé." } },
            { "check.outside", new[] { "The crop doesn't fit the CS2 map.", "La découpe ne tient pas sur la carte CS2." } },
            { "check.mapNone", new[] { "Map: no terrain in the package (the map's own terrain stays)", "Carte : pas de terrain dans le paquet (le terrain de la carte reste)" } },
            { "check.mapTerrain", new[] { "Map: terrain", "Carte : terrain" } },
            { "check.mapWorld", new[] { " with its surroundings", " avec ses alentours" } },
            { "check.mapWater", new[] { ", sea level {0} m, {1} water sources", ", niveau de la mer {0} m, {1} sources d'eau" } },
            { "check.mapDry", new[] { ", no water (a dry map)", ", pas d'eau (carte sèche)" } },
            { "check.mapConnections", new[] { ", {0} outside connections.", ", {0} connexions extérieures." } },
            { "check.mapPrepare", new[] { " In a new city, press \"Prepare map\" before importing.", " Dans une nouvelle ville, cliquez sur « Préparer la carte » avant d'importer." } },
            { "check.cityPrepared", new[] { "This city's terrain is the package's.", "Le terrain de cette ville est celui du paquet." } },
            { "check.cityNotPrepared", new[] { "This city's terrain differs from the package's by {0} m: press \"Prepare map\" in a new city first, or turn on \"Keep this map's terrain\".", "Le terrain de cette ville diffère de celui du paquet de {0} m : cliquez d'abord sur « Préparer la carte » dans une nouvelle ville, ou activez « Garder le terrain de cette carte »." } },

            // Road Builder configs
            { "rb.copied", new[] { "Copied {0} Road Builder configs. Restart Cities: Skylines II so Road Builder loads them, then import.", "{0} configurations Road Builder copiées. Relancez Cities: Skylines II pour que Road Builder les charge, puis importez." } },
            { "rb.noFolder", new[] { "The package has no roadbuilder folder.", "Le paquet n'a pas de dossier roadbuilder." } },
            { "rb.failed", new[] { "Installing the Road Builder configs failed: {0}", "Échec de l'installation des configurations Road Builder : {0}" } },

            // prepare map
            { "prep.running", new[] { "Preparing the map…", "Préparation de la carte…" } },
            { "prep.progress", new[] { "Preparing the map… {0}", "Préparation de la carte… {0}" } },
            { "prep.step.clear", new[] { "removing the base map's roads and buildings", "suppression des routes et bâtiments de la carte" } },
            { "prep.step.terrain", new[] { "applying the terrain", "application du terrain" } },
            { "prep.step.water", new[] { "setting up the water", "mise en place de l'eau" } },
            { "prep.step.settle", new[] { "letting the terrain and water settle", "stabilisation du terrain et de l'eau" } },
            { "prep.notStarted", new[] { "Map not prepared: {0}", "Carte non préparée : {0}" } },
            { "prep.failed", new[] { "Preparing the map failed: {0}", "Échec de la préparation de la carte : {0}" } },
            { "prep.cancelled", new[] { "Cancelled: the map was prepared up to \"{0}\". Start a new city to begin again.", "Annulé : la carte a été préparée jusqu'à « {0} ». Commencez une nouvelle ville pour recommencer." } },
            { "prep.noReport", new[] { "Map prepared, without a report.", "Carte préparée, sans rapport." } },
            { "prep.done", new[] { "Map prepared.", "Carte préparée." } },
            { "prep.removed", new[] { "Removed {0} roads and tracks and {1} buildings of the base map.", "{0} routes et voies ferrées et {1} bâtiments de la carte supprimés." } },
            { "prep.terrainSkipped", new[] { "Terrain skipped: the package has no terrain (export the city with its terrain).", "Terrain ignoré : le paquet n'a pas de terrain (exportez la ville avec son terrain)." } },
            { "prep.terrain", new[] { "Terrain applied{0}: within {1} m of the package at 95 % of {2} check points.", "Terrain appliqué{0} : à {1} m près du paquet sur 95 % des {2} points de contrôle." } },
            { "prep.lht", new[] { "Traffic drives on the left, as in the CS1 city.", "La circulation roule à gauche, comme dans la ville CS1." } },
            { "prep.rht", new[] { "Traffic drives on the right, as in the CS1 city.", "La circulation roule à droite, comme dans la ville CS1." } },
            { "check.sideLeft", new[] { "The CS1 city drove on the left, this city drives on the right: in a new city, \"Prepare map\" sets the side; otherwise roads and lane connections are mirrored.", "La ville CS1 roulait à gauche, cette ville roule à droite : dans une nouvelle ville, « Préparer la carte » règle le côté ; sinon les routes et les connexions de voies sont en miroir." } },
            { "check.sideRight", new[] { "The CS1 city drove on the right, this city drives on the left: in a new city, \"Prepare map\" sets the side; otherwise roads and lane connections are mirrored.", "La ville CS1 roulait à droite, cette ville roule à gauche : dans une nouvelle ville, « Préparer la carte » règle le côté ; sinon les routes et les connexions de voies sont en miroir." } },
            { "prep.waterSkipped", new[] { "Water skipped: the package has no water or terrain.", "Eau ignorée : le paquet n'a ni eau ni terrain." } },
            { "prep.water", new[] { "Water: sea level {0} m, {1} CS1 water sources. Unpause for a while to let rivers and lakes fill.", "Eau : niveau de la mer {0} m, {1} sources d'eau CS1. Laissez tourner le jeu un moment pour que rivières et lacs se remplissent." } },
            { "prep.dry", new[] { "Water: none in the package, the map is dry.", "Eau : aucune dans le paquet, la carte est sèche." } },
            { "prep.next", new[] { "Next: press \"Import into this city\".", "Ensuite : cliquez sur « Importer dans cette ville »." } },
            { "report", new[] { "Full report: {0}", "Rapport complet : {0}" } },

            // import
            { "imp.running", new[] { "Importing…", "Import…" } },
            { "imp.progress", new[] { "Importing… {0}", "Import… {0}" } },
            { "imp.batch", new[] { "batch {0} of {1}: {2} of {3} segments placed", "lot {0} sur {1} : {2} segments posés sur {3}" } },
            { "imp.verify", new[] { "checking what was built", "vérification de ce qui a été construit" } },
            { "imp.notStarted", new[] { "Import not started: {0}", "Import non lancé : {0}" } },
            { "imp.failed", new[] { "Import failed: {0}", "Échec de l'import : {0}" } },
            { "imp.cancelled", new[] { "Cancelled during \"{0}\". What was built so far stays; \"Remove imported\" deletes it.", "Annulé pendant « {0} ». Ce qui a été construit reste ; « Retirer l'import » le supprime." } },
            { "imp.noReport", new[] { "Import finished without a report.", "Import terminé sans rapport." } },
            { "imp.done", new[] { "Done in {0} s: {1} of {2} roads built", "Terminé en {0} s : {1} routes construites sur {2}" } },
            { "imp.failedN", new[] { ", {0} failed", ", {0} en échec" } },
            { "imp.problems", new[] { "Problems: {0}.", "Problèmes : {0}." } },
            { "imp.tunnelsDeepened", new[] { "Tunnels: {0} of {1} CS1 tunnel segments were too shallow for a CS2 tunnel and were built deeper (listed in the report).", "Tunnels : {0} des {1} segments de tunnel CS1 étaient trop peu profonds pour un tunnel CS2 et ont été construits plus bas (liste dans le rapport)." } },
            { "imp.planSkipped", new[] { "{0} roads in the plan were skipped.", "{0} routes du plan ont été ignorées." } },
            { "imp.ghosts", new[] { "Warning: {0} unfinished (ghost) entities remain.", "Attention : {0} éléments inachevés (fantômes) restent." } },
            { "imp.recorded", new[] { "Remove imported can delete the {0} items this import created.", "« Retirer l'import » peut supprimer les {0} éléments créés par cet import." } },
            { "step.roads", new[] { "roads", "routes" } },
            { "step.traffic", new[] { "traffic settings", "règles de circulation" } },
            { "step.trafficmod", new[] { "Traffic mod", "mod Traffic" } },
            { "step.lanes", new[] { "lane connections", "connexions de voies" } },
            { "step.zoning", new[] { "zoning", "zonage" } },
            { "step.buildings", new[] { "buildings", "bâtiments" } },
            { "step.objects", new[] { "trees and props", "arbres et objets" } },
            { "step.districts", new[] { "districts", "quartiers" } },
            { "step.surfaces", new[] { "surfaces", "surfaces" } },
            { "step.transit", new[] { "transit", "transports" } },
            { "step.connections", new[] { "outside connections", "connexions extérieures" } },
            { "step.powerhook", new[] { "Power plants onto the grid", "Raccordement des centrales" } },
            { "line.moreInReport", new[] { "… and {0} more, listed in the report.", "… et {0} autres, liste dans le rapport." } },
            { "step.settle", new[] { "road check", "vérification des routes" } },
            { "line.settle", new[] { "Roads: {0} updated to the crosswalks and turn rules CS2 keeps there (so they don't change when the city is saved and loaded).", "Routes : {0} mises à jour selon les passages piétons et interdictions de tourner que CS2 garde à cet endroit (pour qu'elles ne changent pas à la sauvegarde et au rechargement)." } },
            { "step.lanesrepair", new[] { "lane connections check", "vérification des connexions de voies" } },
            { "line.zoning", new[] { "Zoning: {0} of {1} cells ({2} %), {3} painted.", "Zonage : {0} cellules sur {1} ({2} %), {3} peintes." } },
            { "line.traffic", new[] { "Traffic: {0} lights added, {1} removed, {2} all-way stops, {3} turn bans, {4} crosswalk changes.", "Circulation : {0} feux ajoutés, {1} retirés, {2} stops dans toutes les directions, {3} interdictions de tourner, {4} passages piétons modifiés." } },
            { "line.trafficmod", new[] { "Traffic mod: {0} priority signs on {1} roads.", "Mod Traffic : {0} panneaux de priorité sur {1} routes." } },
            { "line.lanes", new[] { "Lane connections: {0} connections at {1} of {2} junctions; {3} of {4} CS1 lane references matched{5}.", "Connexions de voies : {0} connexions sur {1} carrefours sur {2} ; {3} références de voie CS1 sur {4} trouvées{5}." } },
            { "line.lanesLeftHand", new[] { "not applied yet in cities that drive on the left", "pas encore appliquées dans les villes qui roulent à gauche" } },
            { "line.lanesArmCheck", new[] { " {0} junctions were left as CS2 builds them: their roads didn't line up with CS1's, so a connection could have gone to the wrong road (listed in the report).", " {0} carrefours ont été laissés tels que CS2 les construit : leurs routes ne correspondaient pas à celles de CS1, une connexion aurait pu aller sur la mauvaise route (liste dans le rapport)." } },
            { "line.lanesRepair", new[] { "Lane connections check: {0} of {1} junctions written again because later steps changed their roads (so they survive saving and reloading).", "Vérification des connexions de voies : {0} carrefours sur {1} réécrits, car des étapes suivantes avaient modifié leurs routes (pour qu'ils survivent à la sauvegarde et au rechargement)." } },
            { "line.lanesMirrored", new[] { " (the CS1 city drove on the other side of the road)", " (la ville CS1 roulait de l'autre côté de la route)" } },
            { "line.connections", new[] { "Outside connections: {0} of {1} ({2} road, {3} train{5}){4}.", "Connexions extérieures : {0} sur {1} ({2} routières, {3} ferroviaires{5}){4}." } },
            { "line.connectionsUtilities", new[] { "{0} electricity, {1} water", "{0} électriques, {1} d'eau" } },
            { "line.connectionsUtilitiesOnly", new[] { "Outside connections: {0} (no road or train connections in the package).", "Connexions extérieures : {0} (aucune connexion routière ou ferroviaire dans le paquet)." } },
            { "line.connectionsFailed", new[] { ", {0} without an imported end node", ", {0} sans nœud d'extrémité importé" } },
            { "line.transit", new[] { "Transit: {0} of {1} lines{2}.", "Transports : {0} lignes sur {1}{2}." } },
            { "line.transitStops", new[] { ", {0} of {1} stops", ", {0} arrêts sur {1}" } },
            { "line.placeYourself", new[] { " {0} to place yourself, listed in the report.", " {0} à placer vous-même, liste dans le rapport." } },
            { "line.stepWarning", new[] { "Warning: {0}", "Attention : {0}" } },
            { "line.generic", new[] { "{0}: {1} of {2}{3}{4}.", "{0} : {1} sur {2}{3}{4}." } },
            { "line.already", new[] { " ({0} already there)", " ({0} déjà présents)" } },
            { "line.failedN", new[] { ", {0} failed", ", {0} en échec" } },
            { "line.skipped", new[] { "{0} skipped: {1}", "{0} ignoré : {1}" } },
            { "line.failed", new[] { "{0} failed: {1}", "{0} en échec : {1}" } },
            { "line.noResult", new[] { "{0}: no result.", "{0} : pas de résultat." } },
            { "line.done", new[] { "{0}: done.", "{0} : fait." } },
            { "line.off", new[] { "turned off in the Options ({0})", "désactivé dans les options ({0})" } },
            { "label.ApplyBuildings", new[] { "Buildings, trees, districts", "Bâtiments, arbres, quartiers" } },
            { "label.ApplyTransit", new[] { "Transit lines", "Lignes de transport" } },

            // cancel
            { "cancel.none", new[] { "Nothing to cancel.", "Rien à annuler." } },
            { "cancel.requested", new[] { "Cancelling after the current step…", "Annulation après l'étape en cours…" } },

            // remove imported
            { "undo.running", new[] { "Removing what the imports created…", "Suppression de ce que les imports ont créé…" } },
            { "undo.progress", new[] { "Removing… {0}", "Suppression… {0}" } },
            { "undo.nothing", new[] { "Nothing to remove: no import into this city is recorded.", "Rien à retirer : aucun import dans cette ville n'est enregistré." } },
            { "undo.notStarted", new[] { "Nothing removed: {0}", "Rien n'a été retiré : {0}" } },
            { "undo.failed", new[] { "Removing failed: {0}", "Échec de la suppression : {0}" } },
            { "undo.cancelled", new[] { "Cancelled: some imported items were removed, the rest stays. Press \"Remove imported\" again to finish.", "Annulé : une partie des éléments importés a été supprimée, le reste demeure. Cliquez à nouveau sur « Retirer l'import » pour terminer." } },
            { "undo.done", new[] { "Removed {0} roads, {1} buildings, {2} trees and props, {3} districts and areas, {4} transit lines.", "{0} routes, {1} bâtiments, {2} arbres et objets, {3} quartiers et zones, {4} lignes de transport supprimés." } },
            { "undo.gone", new[] { " {0} items were already gone.", " {0} éléments avaient déjà disparu." } },
            { "undo.kind.edge", new[] { "roads", "routes" } },
            { "undo.kind.building", new[] { "buildings", "bâtiments" } },
            { "undo.kind.object", new[] { "trees and props", "arbres et objets" } },
            { "undo.kind.area", new[] { "districts and areas", "quartiers et zones" } },
            { "undo.kind.route", new[] { "transit lines", "lignes de transport" } },
            { "undo.kind.node", new[] { "junctions", "carrefours" } },
        };
    }
}

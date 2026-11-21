// The CS1 city's driving side against the CS2 city's (DECISIONS-importer.md #63, DECISIONS.md #138). A left-hand plan
// imported into a right-hand city builds every road and lane connection on the wrong side; only Prepare map sets the
// side (#90), so scripts that create a city another way produced right-hand CacaoCity test cities.
namespace Cs2Importer.Core
{
    public static class DrivingSide
    {
        /// <summary>
        /// Null when the import may go ahead: the plan doesn't say (exports before exporter 1.2.1), the city's side is
        /// unknown, or they agree. Otherwise the refusal, in the words the mod and the exec report show.
        /// </summary>
        public static string Mismatch(bool? planLeftHand, bool? cityLeftHand)
        {
            if (!planLeftHand.HasValue || !cityLeftHand.HasValue || planLeftHand.Value == cityLeftHand.Value) return null;
            return planLeftHand.Value
                ? "This package is for a left-hand city; this city drives on the right. Start a new city and press Prepare map."
                : "This package is for a right-hand city; this city drives on the left. Start a new city and press Prepare map.";
        }
    }
}

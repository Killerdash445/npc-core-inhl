using System.Collections.Generic;
using UnityEngine;

namespace NPC.Core.World
{
    /// <summary>
    /// The game's item zones (ItemDetector: doorways, airlocks, elevators, sell pens) forget an item only
    /// on OnTriggerExit, which Unity never sends for a collider switched off inside. Such an item stays
    /// listed, and the zone later acts on it wherever it is. docs/invariants.md#an-item-zone-lists-only-items-in-it
    /// </summary>
    public static class NpcItemZones
    {
        private static readonly List<Grabbable> Stale = [];

        /// <summary>
        /// Takes `item` off every zone's list, as its exit would have: call it before switching the
        /// item's colliders or object off. Returns how many zones listed it.
        /// </summary>
        public static int Leave(Grabbable item)
        {
            if (item == null) return 0;

            int left = 0;
            foreach (ItemDetector zone in Object.FindObjectsOfType<ItemDetector>(true))
            {
                if (zone != null && GameInternals.ItemDetectorAccess.Forget(zone, item)) left++;
            }
            if (left > 0 && NpcLog.Level >= 1)
            {
                NpcLog.Log.LogInfo($"[ai] '{item.gameObject.name}' left {left} item zone(s) it lay in");
            }
            return left;
        }

        /// <summary>
        /// Drops the switched-off items a zone still lists: stored, worn, sold. One switched on again
        /// inside the zone is listed anew by its OnTriggerEnter.
        /// </summary>
        internal static void Prune(ItemDetector? zone, string where)
        {
            if (zone == null) return;

            Stale.Clear();
            foreach (Grabbable item in zone.Items)
            {
                if (item != null && !item.gameObject.activeSelf) Stale.Add(item);
            }
            foreach (Grabbable item in Stale)
            {
                if (!GameInternals.ItemDetectorAccess.Forget(zone, item)) continue;

                if (NpcLog.Level >= 1)
                {
                    NpcLog.Log.LogInfo($"[ai] Doorway {where} no longer re-files '{item.gameObject.name}': it is switched off, not in the doorway");
                }
            }
            Stale.Clear();
        }
    }
}

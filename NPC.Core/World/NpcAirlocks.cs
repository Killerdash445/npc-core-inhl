using System.Collections.Generic;
using NPC.Core.Agents;
using UnityEngine;
using UnityEngine.Events;

namespace NPC.Core.World
{
    /// <summary>
    /// The airlocks' cycles as NPCs feel them: a listener on every airlock's OnExit and OnEnter, the game's
    /// own events (FuelStation and the task sequences listen the same way). The cycle moves whoever stands
    /// in the chamber with the player. docs/invariants.md#an-airlock-is-crossed-by-its-cycle
    /// </summary>
    internal static class NpcAirlocks
    {
        /// <summary>
        /// Airlocks sit in the scene, switched off with their station, so one sweep finds them all; later
        /// sweeps only catch one built afterwards.
        /// </summary>
        private const float RescanSeconds = 30f;

        private static readonly Dictionary<Airlock, (UnityAction Exit, UnityAction Enter)> Listening = [];
        private static float _rescanAt;

        /// <summary>
        /// From the shared tick: listens to every airlock not yet heard, switched off ones included.
        /// </summary>
        internal static void Tick()
        {
            if (Listening.Count > 0 && Time.time < _rescanAt) return;
            if (!SceneScan.MayRescan(Listening.Count == 0)) return;

            _rescanAt = Time.time + RescanSeconds;
            foreach (Airlock airlock in Object.FindObjectsOfType<Airlock>(true))
            {
                if (airlock == null || Listening.ContainsKey(airlock)) continue;

                UnityAction exit = () => Cycled(airlock, toSpace: true);
                UnityAction enter = () => Cycled(airlock, toSpace: false);
                airlock.OnExit.AddListener(exit);
                airlock.OnEnter.AddListener(enter);
                Listening[airlock] = (exit, enter);
            }
        }

        internal static void ResetScene()
        {
            foreach (KeyValuePair<Airlock, (UnityAction Exit, UnityAction Enter)> entry in Listening)
            {
                if (entry.Key == null) continue;

                entry.Key.OnExit.RemoveListener(entry.Value.Exit);
                entry.Key.OnEnter.RemoveListener(entry.Value.Enter);
            }
            Listening.Clear();
            _rescanAt = 0f;
        }

        /// <summary>
        /// Airlock.Exit or Enter has just moved the player: OnExit runs before the ship's rooms switch off,
        /// OnEnter before they switch back on.
        /// </summary>
        private static void Cycled(Airlock airlock, bool toSpace)
        {
            foreach (INpc npc in NpcRegistry.Snapshot())
            {
                if (NpcRegistry.IsGone(npc) || npc is not NpcAgent agent) continue;

                using NpcRegistry.ActingScope _ = NpcRegistry.Acting(agent);
                agent.CycledWith(airlock, toSpace);
            }
        }
    }
}

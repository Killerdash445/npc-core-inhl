using System.Collections.Generic;
using NPC.Core.Agents;
using Space;
using UnityEngine;

namespace NPC.Core.World
{
    /// <summary>
    /// Where the player went on their last walk outside, the airlock end first: a point every Spacing metres,
    /// in the ship's frame. A floating NPC flies along it where it cannot fly straight. docs/agent.md#8-floating
    /// </summary>
    internal static class NpcTrail
    {
        private const float Spacing = 0.5f;
        /// <summary>
        /// Past this many points the older half is thinned to every other point, so the airlock end stays.
        /// </summary>
        private const int Capacity = 400;

        private static readonly List<Vector3> Points = [];
        private static readonly List<Vector3> Thinned = [];
        private static bool _playerWasOutside;

        internal static int Count => Points.Count;

        /// <summary>
        /// Changes whenever indices stop meaning the same points: a clear or a thinning.
        /// </summary>
        internal static int Version { get; private set; }

        internal static Vector3 At(int index) => Points[index];

        /// <summary>
        /// The index of the point nearest `position`, or -1 on an empty trail.
        /// </summary>
        internal static int NearestIndex(Vector3 position)
        {
            int best = -1;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < Points.Count; i++)
            {
                float sqr = (Points[i] - position).sqrMagnitude;
                if (sqr >= bestSqr) continue;

                bestSqr = sqr;
                best = i;
            }
            return best;
        }

        /// <summary>
        /// From the shared tick. A new walk outside starts a new trail; coming in keeps it, so an NPC left
        /// outside can still find the airlock. Once the world moves, no point is anywhere any more.
        /// </summary>
        internal static void Tick()
        {
            Player? pilot = NpcPlayer.Pilot;
            if (pilot == null || pilot.Controller == null) return;

            if (Points.Count > 0 && NpcVessels.WorldMoving()) Clear("the world is moving");

            bool outside = NpcAgent.IsPlayerInSpace(pilot);
            if (outside && !_playerWasOutside) Clear(null);
            _playerWasOutside = outside;
            if (!outside) return;

            Vector3 here = pilot.Controller.CachedTransform.position;
            if (Points.Count > 0 && (here - Points[Points.Count - 1]).sqrMagnitude < Spacing * Spacing) return;

            if (Points.Count >= Capacity) Thin();
            Points.Add(here);
        }

        internal static void ResetScene()
        {
            Points.Clear();
            _playerWasOutside = false;
            Version++;
        }

        private static void Clear(string? why)
        {
            if (why != null && Points.Count > 0 && NpcLog.Level >= 2)
            {
                NpcLog.Core.LogInfo("[ai] Your trail outside is dropped (" + Points.Count + " points) - " + why);
            }
            Points.Clear();
            Version++;
        }

        private static void Thin()
        {
            Thinned.Clear();
            int half = Points.Count / 2;
            for (int i = 0; i < Points.Count; i++)
            {
                if (i < half && i % 2 == 1) continue;

                Thinned.Add(Points[i]);
            }
            Points.Clear();
            Points.AddRange(Thinned);
            Version++;
        }
    }
}

using NPC.Core.World;
using Space;
using Space.Data;
using UnityEngine;

namespace NPC.Core.Agents
{
    /// <summary>
    /// Zero gravity: the gravity of the side it is on, floating with inertia as the player does outside,
    /// and flying after the player without the graph - along the player's own trail where it cannot fly
    /// straight. docs/agent.md#8-floating
    /// </summary>
    public sealed partial class NpcAgent
    {
        /// <summary>
        /// The player's Data.gravity inside: what Airlock.Enter sets.
        /// </summary>
        private const float InsideGravity = 9.81f;

        // PlayerController in zero gravity (Player.prefab): speed 3, spaceAcceleration 3, spaceDrag 0.5.
        private const float FlightAcceleration = 3f;
        /// <summary>
        /// Stopping on purpose brakes harder than the player's coast, so it hovers where it stopped.
        /// </summary>
        private const float FlightBrake = 3f;
        /// <summary>
        /// Following: it sets off beyond the first distance to the player and flies on down to the second.
        /// </summary>
        private const float FlyFollowStart = 3f;
        private const float FlyFollowStop = 2.2f;
        /// <summary>
        /// A Pursue while floating arrives this near its goal.
        /// </summary>
        private const float FlightArrival = 0.5f;
        /// <summary>
        /// Near its goal it slows to this many m/s per metre left, never below FlightMinSpeed.
        /// </summary>
        private const float FlightSlowdown = 1.2f;
        private const float FlightMinSpeed = 0.4f;
        /// <summary>
        /// How often the way ahead is looked at again, and how near a trail point counts as reached.
        /// </summary>
        private const float FlightAimInterval = 0.2f;
        private const float TrailPointReached = 0.6f;
        /// <summary>
        /// Sight lines per look along the trail, and the misses in a row after a seen point that end it.
        /// </summary>
        private const int FlightAimMaxCasts = 48;
        private const int FlightAimMissRun = 6;
        /// <summary>
        /// Wanting to fly but moved less than FlightStuckProgress in FlightStuckSeconds: stuck.
        /// </summary>
        private const float FlightStuckSeconds = 4f;
        private const float FlightStuckProgress = 0.5f;
        /// <summary>
        /// Farther than this from the player outside, it is moved onto their trail, RescueBehind behind them.
        /// </summary>
        private const float FlightLeash = 40f;
        private const float RescueBehind = 3f;
        private const float RescueCooldown = 5f;
        private const int RescueMaxTries = 40;
        private const float FlightDetourMemory = 0.3f;

        private static readonly float[] FlightDetourYaw = [35f, -35f, 70f, -70f];
        private static readonly float[] FlightDetourPitch = [35f, -35f];
        // Shared like every probe buffer; sized for a cast along a cluttered wreck. docs/invariants.md#probe-buffers-must-not-truncate
        private static readonly RaycastHit[] FlightHits = new RaycastHit[64];
        private static readonly Collider[] FlightOverlaps = new Collider[32];
        private static float _flightTruncationWarnedAt = -999f;

        private float sideGravity = InsideGravity;
        private Vector3 flightVelocity;
        private Vector3 flightAim;
        private Vector3 flightAimGoal;
        /// <summary>
        /// The trail point it is flying to, or -1 when it flies straight at its goal.
        /// </summary>
        private int flightAimIndex = -1;
        private float flightAimAt;
        /// <summary>
        /// The furthest trail point flown to on this way along the trail (`flightTrailStep` its
        /// direction), for `flightTrailVersion` of the trail; -1 for none.
        /// </summary>
        private int flightTrailProgress = -1;
        private int flightTrailStep;
        private int flightTrailVersion;
        private bool flyFollowing;
        private bool flightFollowsPlayer;
        private Vector3 flightDetour;
        private float flightDetourUntil;
        private float flightStuckSince = -1f;
        private Vector3 flightStuckFrom;
        private float flightRescueAt;
        private bool flightAimLoggedTrail;
        private float flightAimLoggedAt;
        private float flightStuckLoggedAt;

        /// <summary>
        /// The gravity of its side, as the player's Data.gravity: 9.81 inside; outside, the ExitGravity of
        /// the airlock it went through. docs/agent.md#8-floating
        /// </summary>
        public float Gravity => sideGravity;

        /// <summary>
        /// Outside with no gravity, where PlayerController's Gravity is 0: it floats, and flies instead of
        /// walking. docs/agent.md#8-floating
        /// </summary>
        public bool Floating => outside && sideGravity * GravityMultiplier <= 0f;

        /// <summary>
        /// Its top speed floating: MoveSpeed. A suit does not slow flight (PlayerController.MovementSpeed).
        /// </summary>
        public float FlySpeed => MoveSpeed;

        /// <summary>
        /// The game's gravity setting; 1 outside a game scene.
        /// </summary>
        private static float GravityMultiplier
        {
            get
            {
                GameSettingsData? settingsData = SceneLoader.Instance != null ? SceneLoader.Instance.GameData?.Settings : null;
                return settingsData?.gravityMultiplier ?? 1f;
            }
        }

        /// <summary>
        /// The middle of its capsule: what a flight aims with, as the player's position is the middle of theirs.
        /// </summary>
        private Vector3 BodyCentre => transform.TransformPoint(cc.center);

        // ------------------------------------------------------------------
        // Flights a brain picks from
        // ------------------------------------------------------------------

        /// <summary>
        /// Flies after the player and hovers near them: straight when it can see them, else along their
        /// trail. Farther than FlightLeash while they are outside, it is moved onto the trail behind them.
        /// </summary>
        public Vector3 FlyFollow(Player player, out bool wantMove)
        {
            wantMove = false;
            flightFollowsPlayer = true;
            if (player == null || player.Controller == null) return Vector3.zero;

            Vector3 you = player.Controller.CachedTransform.position;
            float distance = Vector3.Distance(BodyCentre, you);
            if (distance > FlightLeash && IsPlayerInSpace(player))
            {
                RescueBehindPlayer(you, "more than " + FlightLeash.ToString("0") + " m from you");
                return Vector3.zero;
            }

            flyFollowing = distance > (flyFollowing ? FlyFollowStop : FlyFollowStart);
            if (!flyFollowing)
            {
                hasMoveTarget = false;
                return Vector3.zero;
            }
            return FlyToward(you, FlyFollowStop, out wantMove);
        }

        /// <summary>
        /// Flies until its middle is within `arrival` of `point`: straight when it can, else along the
        /// player's trail toward the point's end of it.
        /// </summary>
        public Vector3 FlyTo(Vector3 point, float arrival, out bool wantMove)
        {
            wantMove = false;
            flyFollowing = false;
            flightFollowsPlayer = false;
            if (Vector3.Distance(BodyCentre, point) <= arrival)
            {
                hasMoveTarget = false;
                return Vector3.zero;
            }
            return FlyToward(point, arrival, out wantMove);
        }

        /// <summary>
        /// Holds where it is: it brakes to a stop, at once in an airlock chamber.
        /// </summary>
        public Vector3 Hover(out bool wantMove)
        {
            wantMove = false;
            flyFollowing = false;
            flightFollowsPlayer = false;
            hasMoveTarget = false;
            return Vector3.zero;
        }

        private Vector3 FlyToward(Vector3 goal, float stopAt, out bool wantMove)
        {
            Vector3 aim = FlightAimToward(goal);
            SetMoveTarget(aim);
            Vector3 centre = BodyCentre;
            Vector3 to = aim - centre;
            wantMove = to.sqrMagnitude > 0.0004f;
            if (!wantMove) return Vector3.zero;

            float left = Vector3.Distance(centre, goal) - stopAt;
            return to.normalized * Mathf.Clamp(left * FlightSlowdown, FlightMinSpeed, FlySpeed);
        }

        // ------------------------------------------------------------------
        // The floating frame
        // ------------------------------------------------------------------

        /// <summary>
        /// The brain's step, a detour round what is in the way, recovery, inertia. Doors, hops, whiskers,
        /// stairs and the floor recoveries do not run.
        /// </summary>
        private void UpdateFlight(Player player)
        {
            Vector3 desired = brain.Steer(player, out bool wantMove);
            desired = brain.Constrain(desired, ref wantMove);
            if (wantMove) desired = SteerInFlight(desired);

            UpdateFlightRecovery(wantMove, player);
            ApplyMovement(desired, wantMove);
            UpdateAnimation(desired, wantMove, player);
            UpdateDebugVisuals();
        }

        /// <summary>
        /// PlayerController's zero-g move: the velocity eases toward the step, and eases off when there is none.
        /// </summary>
        private void ApplyFlight(Vector3 desired, bool wantMove)
        {
            verticalVelocity = 0f;
            wantJump = false;
            // A drift into a closing door fails the player's cycle (AntiCrasher -> Gate.FailClose).
            if (!wantMove && inChamber)
            {
                flightVelocity = Vector3.zero;
                return;
            }

            Vector3 target = wantMove ? Vector3.ClampMagnitude(desired, FlySpeed) : Vector3.zero;
            flightVelocity = Vector3.Lerp(flightVelocity, target, Time.deltaTime * (wantMove ? FlightAcceleration : FlightBrake));
            if (flightVelocity.sqrMagnitude < 0.0001f) return;

            // What a wall stopped is gone, so it does not keep pressing into it.
            if (cc.Move(flightVelocity * Time.deltaTime) != CollisionFlags.None) flightVelocity = cc.velocity;
        }

        /// <summary>
        /// Floating started or ended: nothing of the other way of moving carries over.
        /// </summary>
        private void OnFloatingChanged()
        {
            flightVelocity = Vector3.zero;
            verticalVelocity = 0f;
            flightAimIndex = -1;
            flightAimAt = 0f;
            flightTrailProgress = -1;
            flyFollowing = false;
            flightStuckSince = -1f;
            if (!Floating)
            {
                hasBaseFloor = false;
                return;
            }

            // It rides the ship's frame, the scene root: docs/invariants.md#an-npc-rides-its-own-floor
            if (transform.parent != null) transform.SetParent(null, true);
            CurrentOwner = null;
            DropPlan();
            hasMoveTarget = false;
            sidestepUntil = 0f;
            followStepOffUntil = 0f;
            jumpCommitUntil = 0f;
            wantJump = false;
        }

        // ------------------------------------------------------------------
        // Where to fly
        // ------------------------------------------------------------------

        /// <summary>
        /// Where to fly now on the way to `goal`: straight at it when the way is clear, else a point of the
        /// player's trail. Looked at again every FlightAimInterval, and at once on reaching a trail point.
        /// </summary>
        private Vector3 FlightAimToward(Vector3 goal)
        {
            Vector3 centre = BodyCentre;
            bool reachedTrailPoint = flightAimIndex >= 0 && (centre - flightAim).sqrMagnitude < TrailPointReached * TrailPointReached;
            if (Time.time < flightAimAt && !reachedTrailPoint && (flightAimGoal - goal).sqrMagnitude < 1f) return flightAim;

            flightAimAt = Time.time + FlightAimInterval;
            flightAimGoal = goal;
            flightAimIndex = FlightClear(centre, goal) ? -1 : TrailIndexToward(centre, goal);
            flightAim = flightAimIndex >= 0 ? NpcTrail.At(flightAimIndex) : goal;
            LogFlightAim();
            return flightAim;
        }

        /// <summary>
        /// Walking the trail from the point nearest the NPC toward the one nearest the goal: the last point
        /// it can see before a run of misses, the nearest point when it sees none, -1 without a trail.
        /// Going the same way, it starts no further back than the point it already flew to: where the
        /// trail passes near itself, the nearest point can be on the other pass and turn it round.
        /// </summary>
        private int TrailIndexToward(Vector3 centre, Vector3 goal)
        {
            if (NpcTrail.Count == 0) return -1;

            int nearest = NpcTrail.NearestIndex(centre);
            int to = NpcTrail.NearestIndex(goal);
            int from = nearest;
            int step = to >= from ? 1 : -1;
            bool onTheWay = flightTrailProgress >= 0 && flightTrailVersion == NpcTrail.Version &&
                            (to - flightTrailProgress) * flightTrailStep >= 0;
            if (onTheWay && (flightTrailProgress - from) * flightTrailStep > 0)
            {
                from = flightTrailProgress;
                step = flightTrailStep;
            }
            int seen = -1;
            int misses = 0;
            int casts = 0;
            for (int i = from; ; i += step)
            {
                if (FlightClear(centre, NpcTrail.At(i)))
                {
                    seen = i;
                    misses = 0;
                }
                else if (seen >= 0 && ++misses >= FlightAimMissRun)
                {
                    break;
                }
                if (i == to || ++casts >= FlightAimMaxCasts) break;
            }
            if (seen < 0)
            {
                // Nothing in sight: the nearest point, and the controller slides along whatever is in between.
                flightTrailProgress = -1;
                return nearest;
            }
            flightTrailProgress = seen;
            flightTrailStep = step;
            flightTrailVersion = NpcTrail.Version;
            return seen;
        }

        /// <summary>
        /// Round what is in the way: sideways first, then over or under. A detour found is kept a moment
        /// while it stays clear. With none clear it flies on, and the controller slides along the obstacle.
        /// </summary>
        private Vector3 SteerInFlight(Vector3 desired)
        {
            float speed = desired.magnitude;
            if (speed < 0.01f) return desired;

            Vector3 dir = desired / speed;
            float ahead = 0.3f + speed * 0.4f;
            if (!FlightBlocked(dir, ahead)) return desired;

            if (Time.time < flightDetourUntil && Vector3.Dot(flightDetour, dir) > 0.2f && !FlightBlocked(flightDetour, ahead))
            {
                return flightDetour * speed;
            }

            Vector3 across = Vector3.Cross(dir, Vector3.up);
            if (across.sqrMagnitude < 0.001f) across = transform.right;
            across.Normalize();
            foreach (float yaw in FlightDetourYaw)
            {
                if (TryDetour(Quaternion.AngleAxis(yaw, Vector3.up) * dir, ahead)) return flightDetour * speed;
            }
            foreach (float pitch in FlightDetourPitch)
            {
                if (TryDetour(Quaternion.AngleAxis(pitch, across) * dir, ahead)) return flightDetour * speed;
            }
            return desired;
        }

        private bool TryDetour(Vector3 dir, float ahead)
        {
            if (FlightBlocked(dir, ahead)) return false;

            flightDetour = dir;
            flightDetourUntil = Time.time + FlightDetourMemory;
            return true;
        }

        // ------------------------------------------------------------------
        // Probes: the body's own filter (PassesThrough), on what its controller collides with
        // ------------------------------------------------------------------

        private bool FlightBlocked(Vector3 dir, float distance)
        {
            FlightCapsuleAt(BodyCentre, out Vector3 bottom, out Vector3 top, out float radius);
            return AnySolidFlightHit(Physics.CapsuleCastNonAlloc(bottom, top, radius, dir, FlightHits, distance, ProbeLayers,
                QueryTriggerInteraction.Ignore));
        }

        /// <summary>
        /// A body's width fits along the line between two middles.
        /// </summary>
        private bool FlightClear(Vector3 from, Vector3 to)
        {
            Vector3 line = to - from;
            float length = line.magnitude;
            if (length < 0.05f) return true;

            return !AnySolidFlightHit(Physics.SphereCastNonAlloc(from, cc.radius * 0.8f, line / length, FlightHits, length, ProbeLayers,
                QueryTriggerInteraction.Ignore));
        }

        /// <summary>
        /// One of the first `count` FlightHits stops the body; a full buffer counts as one.
        /// </summary>
        private bool AnySolidFlightHit(int count)
        {
            if (FlightHitsTruncated(count)) return true;

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = FlightHits[i];
                // Started inside it: the controller depenetrates that itself.
                if (hit.distance >= 0.02f && !PassesThrough(hit.collider, hit.point)) return true;
            }
            return false;
        }

        /// <summary>
        /// Nothing solid where its body would be with its middle at `centre`, and not on top of the player.
        /// </summary>
        private bool FreeAt(Vector3 centre)
        {
            CharacterController? playerCc = NpcPlayer.Controller;
            if (playerCc != null && (playerCc.transform.position - centre).sqrMagnitude < 1f) return false;

            FlightCapsuleAt(centre, out Vector3 bottom, out Vector3 top, out float radius);
            int count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, FlightOverlaps, ProbeLayers,
                QueryTriggerInteraction.Ignore);
            if (count >= FlightOverlaps.Length) return false;

            for (int i = 0; i < count; i++)
            {
                if (!IsIgnorableCollider(FlightOverlaps[i])) return false;
            }
            return true;
        }

        private void FlightCapsuleAt(Vector3 centre, out Vector3 bottom, out Vector3 top, out float radius)
        {
            radius = cc.radius * 0.9f;
            float half = Mathf.Max(cc.height * 0.5f - radius, 0f);
            bottom = centre + Vector3.down * half;
            top = centre + Vector3.up * half;
        }

        private static bool FlightHitsTruncated(int count)
        {
            if (count < FlightHits.Length) return false;

            if (Time.time - _flightTruncationWarnedAt >= 30f)
            {
                _flightTruncationWarnedAt = Time.time;
                NpcLog.Log.LogWarning("[probe] flight probe filled its " + FlightHits.Length +
                                      "-hit buffer; the way is taken as blocked.");
            }
            return true;
        }

        // ------------------------------------------------------------------
        // Recovery
        // ------------------------------------------------------------------

        /// <summary>
        /// Wanting to fly and getting nowhere: following the player outside, it is moved onto their trail
        /// behind them; following the trail anywhere else, on to the trail point it was flying to.
        /// </summary>
        private void UpdateFlightRecovery(bool wantMove, Player player)
        {
            if (!wantMove)
            {
                flightStuckSince = -1f;
                return;
            }

            Vector3 here = transform.position;
            if (flightStuckSince < 0f || (here - flightStuckFrom).sqrMagnitude > FlightStuckProgress * FlightStuckProgress)
            {
                flightStuckSince = Time.time;
                flightStuckFrom = here;
                return;
            }
            if (Time.time - flightStuckSince < FlightStuckSeconds) return;

            flightStuckSince = -1f;
            string why = "stuck floating for " + FlightStuckSeconds.ToString("0") + " s";
            if (flightFollowsPlayer && player != null && player.Controller != null && IsPlayerInSpace(player))
            {
                RescueBehindPlayer(player.Controller.CachedTransform.position, why);
            }
            // Only a hop in sight: a point past a shut door is not on the way.
            else if (flightAimIndex >= 0 && FreeAt(flightAim) && FlightClear(BodyCentre, flightAim))
            {
                MoveInFlight(flightAim, why + " - moved on to point " + (flightAimIndex + 1) + " of your trail");
            }
            else if (Time.time - flightStuckLoggedAt >= 15f)
            {
                flightStuckLoggedAt = Time.time;
                NpcLog.Log.LogInfo("[ai] " + Name + " is " + why + ", with no free point of your trail to move to - " +
                                   "it keeps trying");
            }
        }

        /// <summary>
        /// Onto the player's trail, RescueBehind or more behind them, where it is free and in sight of them.
        /// A last resort, like every rescue: docs/agent.md#8-floating
        /// </summary>
        private void RescueBehindPlayer(Vector3 you, string why)
        {
            if (Time.time < flightRescueAt) return;

            flightRescueAt = Time.time + RescueCooldown;
            float along = 0f;
            int tries = 0;
            for (int i = NpcTrail.Count - 2; i >= 0 && tries < RescueMaxTries; i--)
            {
                Vector3 point = NpcTrail.At(i);
                along += Vector3.Distance(point, NpcTrail.At(i + 1));
                if (along < RescueBehind) continue;

                tries++;
                if (!FreeAt(point) || !FlightClear(point, you)) continue;

                MoveInFlight(point, why + " - moved to your trail " + along.ToString("0.0") + " m behind you");
                return;
            }
            NpcLog.Log.LogInfo("[ai] " + Name + " is " + why + ", and no point of your trail behind you is free - it keeps flying");
        }

        private void MoveInFlight(Vector3 centre, string why)
        {
            TeleportTo(centre - (BodyCentre - transform.position), true);
            flightAimAt = 0f;
            NpcLog.Log.LogWarning("[ai] " + Name + " was " + why);
        }

        /// <summary>
        /// Level 1, when it switches between flying straight and following the trail; at most every 5 s.
        /// </summary>
        private void LogFlightAim()
        {
            bool onTrail = flightAimIndex >= 0;
            if (onTrail == flightAimLoggedTrail || NpcLog.Level < 1 || Time.time < flightAimLoggedAt) return;

            flightAimLoggedTrail = onTrail;
            flightAimLoggedAt = Time.time + 5f;
            NpcLog.Log.LogInfo(onTrail
                ? "[ai] " + Name + " cannot fly straight there - following your trail (point " + (flightAimIndex + 1) +
                  " of " + NpcTrail.Count + ")"
                : "[ai] " + Name + " flies straight at its goal again");
        }
    }
}

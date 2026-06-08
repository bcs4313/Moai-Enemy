using MoaiEnemy.src.MoaiNormal;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using GameNetcodeStuff;

namespace MoaiEnemy.src.MoaiPirate
{
    // pirate ship is a vehicle that the MoaiPirateAI controls
    // it is not an enemyAI, for simplicity's sake, instead it has a NavAgent
    // that moves to a target destination on command

    // random travel phases:
    // 0 - landed, the ship sits still on the ground. The moai pirate is doing business on the ground
    // 1 - rising, the ship is rising into the sky. A ship must have a moai pirate to rise
    // 2 - traveling, the ship is using the NavMeshAgent to find a destination, ignoring elevation of the dest
    // 3 - lowering, the ship is lowering to the ground, eventually landing.
    // in the lowering phase, the ship will attempt to "fit" its own hitbox to the destination, via random sampling
    // 4 - aggressive, the ship is pursuing a high-value target

    internal class MoaiPirateShip : NetworkBehaviour
    {
        public NavMeshAgent agent;
        public String phase = "landed";

        private MoaiPirateAI captain = null;
        public GameObject shipModel; // where the ship is actually located. The navigation source (navagent), still lives on the navmesh while the ship is elevated.

        // moai attachment points
        public Transform MainDeck;
        public Transform CrowsNest;
        public Transform PoopDeck;
        public Transform Bow;
        public Transform WheelPoint;  // the moai must be here in the traveling phase

        public float yLevel = 0f;
        public float targetYLevel = 0f;
        public static float yEaseRate = 0.75f;

        public static float baseLandChance = 0.25f;
        public static float landChance = 0.25f;

        // ── Aggressive state ──────────────────────────────────────────────
        public enum AggressiveAction { Cannon, Grapple, Lower }

        // The current scored target (one of these will be non-null)
        public PlayerControllerB aggroPlayer = null;
        public EnemyAI aggroEnemy = null;
        public GrabbableObject aggroScrap = null;
        public VehicleController aggroCruiser = null;
        public AggressiveAction aggroAction;

        // Grapple state — set true during CruiserGrappleRoutine hold phase
        public bool isGrappling = false;

        private float rescoreTimer = 0f;
        private const float RESCORE_INTERVAL = 3f;
        private const float ARRIVAL_DIST = 6f;       // XZ distance to consider "arrived" at target
        private const float AGGRO_SIGHT_DIST = 40f;  // distance to keep tracking target before giving up
        private const float MIN_ENEMY_SCORE = 20f;

        // stubs fire state
        private bool actionExecuted = false;

        void Start()
        {
            yLevel = transform.position.y;
            targetYLevel = transform.position.y;
        }

        public void Update()
        {
            if (captain == null) { return; }
            if (!RoundManager.Instance.IsHost) { return; }

            switch (phase)
            {
                case "landed":
                    break;
                case "rising":
                    if (Math.Abs(yLevel - targetYLevel) < 1)
                    {
                        InitPhaseTraveling(Vector3.zero);
                    }
                    break;
                case "lowering":
                    if (Math.Abs(yLevel - targetYLevel) < 1)
                    {
                        InitPhaseLanded();
                    }
                    break;
                case "traveling":
                    Vector3 adjustedPos = new Vector3(transform.position.x, 0, transform.position.z);
                    Vector3 adjustedDest = new Vector3(agent.destination.x, 0, agent.destination.z);
                    if (Vector3.Distance(adjustedPos, adjustedDest) < 3)
                    {
                        if (UnityEngine.Random.Range(0f, 1f) < landChance)
                        {
                            InitPhaseLowering();
                            landChance = baseLandChance;
                        }
                        else
                        {
                            InitPhaseTraveling(Vector3.zero);
                            landChance += 0.1f;
                        }
                    }
                    break;
                case "aggressive":
                    UpdateAggressive();
                    break;
            }

            // Ease yLevel
            yLevel = Mathf.Lerp(yLevel, targetYLevel, yEaseRate * Time.deltaTime);

            // Apply to ship model
            shipModel.transform.position = new Vector3(transform.position.x, yLevel, transform.position.z);
        }

        // ─────────────────────────────────────────────────────────────────
        //  AGGRESSIVE UPDATE  (called every frame while phase == "aggressive")
        // ─────────────────────────────────────────────────────────────────
        private void UpdateAggressive()
        {
            // During cruiser grapple hold, the coroutine drives the ship — hands off
            if (isGrappling) return;

            // Re-score on timer
            rescoreTimer -= Time.deltaTime;
            if (rescoreTimer <= 0f)
            {
                rescoreTimer = RESCORE_INTERVAL;
                ScoreAndPickTarget();
            }

            // No target — give up, go back to patrolling
            if (!HasValidTarget())
            {
                Debug.Log("Moai Pirate Ship: Lost target, resuming patrol.");
                ExitAggressive();
                return;
            }

            // Navigate toward target
            Vector3 targetPos = GetTargetPosition();
            agent.SetDestination(targetPos);

            // Check XZ arrival
            Vector3 shipXZ = new Vector3(transform.position.x, 0, transform.position.z);
            Vector3 targetXZ = new Vector3(targetPos.x, 0, targetPos.z);
            bool arrived = Vector3.Distance(shipXZ, targetXZ) < ARRIVAL_DIST;

            if (arrived && !actionExecuted)
            {
                ExecuteAggroAction();
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  SCORING
        // ─────────────────────────────────────────────────────────────────
        private void ScoreAndPickTarget()
        {
            float bestScore = float.MinValue;
            PlayerControllerB bestPlayer = null;
            EnemyAI bestEnemy = null;
            GrabbableObject bestScrap = null;
            AggressiveAction bestAction = AggressiveAction.Cannon;

            // ── Players ───────────────────────────────────────────────────
            foreach (PlayerControllerB player in FindObjectsOfType<PlayerControllerB>())
            {
                if (player == null || player.isPlayerDead || !player.isPlayerControlled) continue;

                float dist = Vector3.Distance(transform.position, player.transform.position);
                if (dist > AGGRO_SIGHT_DIST) continue;

                int heldValue = 0;
                if (player.ItemSlots != null)
                {
                    foreach (GrabbableObject item in player.ItemSlots)
                    {
                        if (item != null && item.itemProperties.isScrap)
                            heldValue += item.scrapValue;
                    }
                }

                float score = 32f - dist + heldValue;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestPlayer = player;
                    bestEnemy = null;
                    bestScrap = null;
                    // 50/50: cannon or lower
                    bestAction = UnityEngine.Random.value < 0.5f ? AggressiveAction.Cannon : AggressiveAction.Lower;
                }
            }

            // ── Enemies ───────────────────────────────────────────────────
            foreach (EnemyAI enemy in FindObjectsOfType<EnemyAI>())
            {
                if (enemy == null || enemy.isEnemyDead) continue;
                if (enemy is MOAIAICORE) continue;  // ignore own kind
                if (enemy.enemyHP <= 0) continue;   // invincible / already dead

                float dist = Vector3.Distance(transform.position, enemy.transform.position);
                if (dist > AGGRO_SIGHT_DIST) continue;

                float score = (9f * enemy.enemyHP) - dist;
                if (score < MIN_ENEMY_SCORE) continue;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestPlayer = null;
                    bestEnemy = enemy;
                    bestScrap = null;
                    // 33% each: cannon, grapple, lower
                    float roll = UnityEngine.Random.value;
                    bestAction = roll < 0.333f ? AggressiveAction.Cannon
                               : roll < 0.666f ? AggressiveAction.Grapple
                               : AggressiveAction.Lower;
                }
            }

            // ── Scrap ─────────────────────────────────────────────────────
            foreach (GrabbableObject item in FindObjectsOfType<GrabbableObject>())
            {
                if (item == null || !item.itemProperties.isScrap) continue;
                if (item.isHeld || item.isHeldByEnemy) continue;  // skip held items

                float dist = Vector3.Distance(transform.position, item.transform.position);
                if (dist > AGGRO_SIGHT_DIST) continue;

                float score = item.scrapValue - dist;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestPlayer = null;
                    bestEnemy = null;
                    bestScrap = item;
                    bestAction = AggressiveAction.Grapple;  // scrap always grappled
                }
            }

            // ── Cruiser ───────────────────────────────────────────────────
            VehicleController bestCruiser = null;
            foreach (VehicleController vehicle in FindObjectsOfType<VehicleController>())
            {
                if (vehicle == null || vehicle.carDestroyed) continue;

                float dist = Vector3.Distance(transform.position, vehicle.transform.position);
                if (dist > AGGRO_SIGHT_DIST) continue;

                // Cruiser always scores very high — pirate ships love stealing cars
                float score = 180f - dist;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestPlayer = null;
                    bestEnemy = null;
                    bestScrap = null;
                    bestCruiser = vehicle;
                    bestAction = AggressiveAction.Grapple;  // cruiser always grappled
                }
            }

            // Apply result
            aggroPlayer = bestPlayer;
            aggroEnemy = bestEnemy;
            aggroScrap = bestScrap;
            aggroCruiser = bestCruiser;
            aggroAction = bestAction;
            actionExecuted = false;  // new target, reset action flag

            if (aggroPlayer != null)
                Debug.Log($"Moai Pirate Ship: Targeting player {aggroPlayer.playerUsername}, action={aggroAction}, score={bestScore}");
            else if (aggroEnemy != null)
                Debug.Log($"Moai Pirate Ship: Targeting enemy {aggroEnemy.enemyType.enemyName}, action={aggroAction}, score={bestScore}");
            else if (aggroScrap != null)
                Debug.Log($"Moai Pirate Ship: Targeting scrap {aggroScrap.itemProperties.itemName}, score={bestScore}");
            else if (aggroCruiser != null)
                Debug.Log($"Moai Pirate Ship: Targeting cruiser, action=Grapple, score={bestScore}");
            else
                Debug.Log("Moai Pirate Ship: No valid target found during rescore.");
        }

        private bool HasValidTarget()
        {
            if (aggroPlayer != null)
            {
                if (aggroPlayer.isPlayerDead || !aggroPlayer.isPlayerControlled) return false;
                float dist = Vector3.Distance(transform.position, aggroPlayer.transform.position);
                return dist <= AGGRO_SIGHT_DIST;
            }
            if (aggroEnemy != null)
            {
                if (aggroEnemy == null || aggroEnemy.isEnemyDead) return false;
                float dist = Vector3.Distance(transform.position, aggroEnemy.transform.position);
                return dist <= AGGRO_SIGHT_DIST;
            }
            if (aggroScrap != null)
            {
                if (aggroScrap == null) return false;
                float dist = Vector3.Distance(transform.position, aggroScrap.transform.position);
                return dist <= AGGRO_SIGHT_DIST;
            }
            if (aggroCruiser != null)
            {
                if (aggroCruiser.carDestroyed) return false;
                float dist = Vector3.Distance(transform.position, aggroCruiser.transform.position);
                return dist <= AGGRO_SIGHT_DIST;
            }
            return false;
        }

        private Vector3 GetTargetPosition()
        {
            if (aggroPlayer != null) return aggroPlayer.transform.position;
            if (aggroEnemy != null) return aggroEnemy.transform.position;
            if (aggroScrap != null) return aggroScrap.transform.position;
            if (aggroCruiser != null) return aggroCruiser.transform.position;
            return transform.position;
        }

        // ─────────────────────────────────────────────────────────────────
        //  ACTION EXECUTION  (stubs)
        // ─────────────────────────────────────────────────────────────────
        private void ExecuteAggroAction()
        {
            actionExecuted = true;

            switch (aggroAction)
            {
                case AggressiveAction.Cannon:
                    FireCannon();
                    break;
                case AggressiveAction.Grapple:
                    FireGrapple();
                    break;
                case AggressiveAction.Lower:
                    InitPhaseLowering();
                    // MoaiPirateAI handles the rest once landed
                    break;
            }

            // Cannon and non-cruiser grapple return to patrolling immediately after firing.
            // Cruiser grapple is handled entirely by CruiserGrappleRoutine — don't exit here.
            // Lower will transition via the landed phase detection in MoaiPirateAI.
            if (aggroAction == AggressiveAction.Cannon)
            {
                ExitAggressive();
            }
            else if (aggroAction == AggressiveAction.Grapple && aggroCruiser == null)
            {
                // Non-cruiser grapple (enemy/scrap) — exit immediately
                ExitAggressive();
            }
            // aggroAction == Lower or cruiser grapple: do NOT call ExitAggressive here
        }

        // STUB — replace with real projectile later
        private void FireCannon()
        {
            Vector3 targetPos = GetTargetPosition();
            Debug.Log($"Moai Pirate Ship: [STUB] Firing cannon at {targetPos}");

            // TODO: Instantiate cannon ball projectile from Bow transform, aimed at targetPos
            // For now, deal direct damage if target is a player
            if (aggroPlayer != null)
            {
                aggroPlayer.DamagePlayer(30, true, true, CauseOfDeath.Blast);
                Debug.Log("Moai Pirate Ship: [STUB] Cannon hit player for 30 damage.");
            }
            else if (aggroEnemy != null)
            {
                aggroEnemy.HitEnemy(2, null, true);
                Debug.Log("Moai Pirate Ship: [STUB] Cannon hit enemy for 2 hits.");
            }
        }

        // STUB — replace with real grapple animation/projectile later
        private void FireGrapple()
        {
            Debug.Log("Moai Pirate Ship: [STUB] Firing grappling hook.");

            if (aggroCruiser != null)
            {
                // Cruiser grapple: coroutine handles the full sequence
                StartCoroutine(CruiserGrappleRoutine(aggroCruiser));
                return;  // do NOT call ExitAggressive here — coroutine does it
            }

            // TODO: Animate grapple hook from ship toward target, then poof
            if (aggroEnemy != null)
            {
                Debug.Log($"Moai Pirate Ship: [STUB] Grappling enemy {aggroEnemy.enemyType.enemyName} — poofing.");
                aggroEnemy.gameObject.SetActive(false);
                Destroy(aggroEnemy.gameObject, 0.1f);
                aggroEnemy = null;
            }
            else if (aggroScrap != null)
            {
                Debug.Log($"Moai Pirate Ship: [STUB] Grappling scrap {aggroScrap.itemProperties.itemName} — poofing.");
                aggroScrap.gameObject.SetActive(false);
                Destroy(aggroScrap.gameObject, 0.1f);
                aggroScrap = null;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  CRUISER GRAPPLE ROUTINE
        //  Sequence: rise → hold (ship flees players while dragging cruiser)
        //            → release → retract → exit aggressive
        // ─────────────────────────────────────────────────────────────────
        private IEnumerator CruiserGrappleRoutine(VehicleController cruiser)
        {
            isGrappling = true;
            Debug.Log("Moai Pirate Ship: Cruiser grapple — starting sequence.");

            // 1. Rise into the air, pulling the cruiser up via physics force each frame
            InitPhaseRising();
            Debug.Log("Moai Pirate Ship: Cruiser grapple — rising.");

            // Wait until rise is complete (yLevel within 1 of targetYLevel)
            while (Mathf.Abs(yLevel - targetYLevel) >= 1f)
            {
                if (cruiser == null || cruiser.carDestroyed)
                {
                    Debug.Log("Moai Pirate Ship: Cruiser destroyed during rise — aborting grapple.");
                    ExitAggressive();
                    yield break;
                }

                // Pull cruiser upward during rise
                Rigidbody rb = cruiser.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    Vector3 toShip = (shipModel.transform.position - cruiser.transform.position);
                    rb.AddForce(toShip.normalized * 18f, ForceMode.Force);
                }

                yield return null;
            }

            // 2. Hold phase — random 5–40s, ship actively flees players while dragging cruiser
            float holdDuration = UnityEngine.Random.Range(5f, 40f);
            float holdTimer = 0f;
            Debug.Log($"Moai Pirate Ship: Cruiser grapple — hold phase for {holdDuration:F1}s, fleeing players.");

            while (holdTimer < holdDuration)
            {
                if (cruiser == null || cruiser.carDestroyed)
                {
                    Debug.Log("Moai Pirate Ship: Cruiser destroyed during hold — aborting grapple.");
                    ExitAggressive();
                    yield break;
                }

                holdTimer += Time.deltaTime;

                // Find nearest player (XZ only)
                PlayerControllerB nearest = null;
                float nearestDist = float.MaxValue;
                foreach (PlayerControllerB player in FindObjectsOfType<PlayerControllerB>())
                {
                    if (player == null || player.isPlayerDead || !player.isPlayerControlled) continue;
                    float d = Vector3.Distance(transform.position, player.transform.position);
                    if (d < nearestDist)
                    {
                        nearestDist = d;
                        nearest = player;
                    }
                }

                // Navigate ship directly away from nearest player
                if (nearest != null)
                {
                    Vector3 shipXZ = new Vector3(transform.position.x, 0f, transform.position.z);
                    Vector3 playerXZ = new Vector3(nearest.transform.position.x, 0f, nearest.transform.position.z);
                    Vector3 fleeDir = (shipXZ - playerXZ).normalized;
                    Vector3 fleeDest = transform.position + fleeDir * 30f;
                    agent.SetDestination(fleeDest);
                }

                // Keep dragging cruiser toward ship each frame during hold
                Rigidbody crb = cruiser.GetComponent<Rigidbody>();
                if (crb != null)
                {
                    Vector3 toCruiserTarget = (shipModel.transform.position - cruiser.transform.position);
                    crb.AddForce(toCruiserTarget.normalized * 12f, ForceMode.Force);
                }

                yield return null;
            }

            // 3. Release — stop applying forces, let cruiser fall
            Debug.Log("Moai Pirate Ship: Cruiser grapple — releasing cruiser.");
            // (forces stop naturally since we're no longer in the loop)

            // Brief pause so the cruiser can separate cleanly
            yield return new WaitForSeconds(0.5f);

            // 4. Retract chain (visual only for now — TODO: animate chain retraction)
            Debug.Log("Moai Pirate Ship: Cruiser grapple — retracting chain.");
            yield return new WaitForSeconds(1.5f);

            // 5. Done — back to patrol
            Debug.Log("Moai Pirate Ship: Cruiser grapple — sequence complete.");
            ExitAggressive();
        }

        private void ExitAggressive()
        {
            aggroPlayer = null;
            aggroEnemy = null;
            aggroScrap = null;
            aggroCruiser = null;
            isGrappling = false;
            actionExecuted = false;
            rescoreTimer = 0f;
            InitPhaseTraveling(Vector3.zero);
        }

        // ─────────────────────────────────────────────────────────────────
        //  PHASE INITS
        // ─────────────────────────────────────────────────────────────────
        public void InitPhaseLanded()
        {
            phase = "landed";
        }

        public static float lowestHeight = 5f;
        public static float highestHeight = 25f;
        public void InitPhaseRising()
        {
            phase = "rising";
            targetYLevel = transform.position.y + UnityEngine.Random.Range(lowestHeight, highestHeight);
            Debug.Log("Moai Pirate Ship: rising to height of: " + targetYLevel);
        }

        public void InitPhaseLowering()
        {
            phase = "lowering";
            Physics.Raycast(shipModel.transform.position, Vector3.down, out RaycastHit hitInfo, 500f,
                LayerMask.GetMask("Default", "Room", "Terrain", "Colliders"));

            if (hitInfo.collider != null)
            {
                targetYLevel = hitInfo.point.y;
            }
            else
            {
                Debug.Log("Moai Pirate Ship: Failed to find a raycast point to land on. Navigating elsewhere...");
                InitPhaseTraveling(Vector3.zero);
            }
        }

        public GameObject FindDestination()
        {
            RoundManager m = RoundManager.Instance;
            GameObject[] outNodes = m.outsideAINodes;
            return outNodes[UnityEngine.Random.Range(0, outNodes.Length)];
        }

        public void InitPhaseTraveling(Vector3 destination)
        {
            phase = "traveling";
            agent.enabled = true;
            if (destination == Vector3.zero)
            {
                destination = FindDestination().transform.position;
            }
            agent.SetDestination(destination);
        }

        public void InitPhaseAggressive()
        {
            phase = "aggressive";
            rescoreTimer = 0f;   // score immediately on first update
            actionExecuted = false;
            aggroPlayer = null;
            aggroEnemy = null;
            aggroScrap = null;
            aggroCruiser = null;
            isGrappling = false;
        }

        [ClientRpc]
        public void SetCaptainClientRpc(ulong uid)
        {
            foreach (MoaiPirateAI ai in FindObjectsOfType<MoaiPirateAI>())
            {
                if (ai.NetworkObjectId == uid)
                {
                    captain = ai;
                }
            }
        }
    }
}
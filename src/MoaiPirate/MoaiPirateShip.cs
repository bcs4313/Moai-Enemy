using MoaiEnemy.src.MoaiNormal;
using System;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using GameNetcodeStuff;
using System.Collections;
using UnityEngine.TextCore.Text;

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

        public static float cannonFireCooldownMin = 0.3f;
        public static float cannonFireCooldownMax = 9f;

        // The current scored target (one of these will be non-null)
        public PlayerControllerB aggroPlayer = null;
        public EnemyAI aggroEnemy = null;
        public GrabbableObject aggroScrap = null;
        public VehicleController aggroCruiser = null;   // cruiser target — always Grapple action
        public AggressiveAction aggroAction;

        private float rescoreTimer = 0f;
        private const float RESCORE_INTERVAL = 3f;
        private static float ARRIVAL_DIST = 12f;       // XZ distance to consider "arrived" at target
        private static float LAND_DIST = 6f;           // XZ distance to consider "landable" at target
        private const float MIN_ENEMY_SCORE = 20f;

        // stubs fire state
        private bool actionExecuted = false;

        // Audio Sources
        public AudioSource shipHornThreaten;  // more menacing horn
        public static float shipHornThreatenCooldownMin = 0.25f;
        public static float shipHornThreatenCooldownMax = 12f;
        public AudioSource shipHornStealing; // steal sound indicator
        public AudioSource landingBell;      // indicates the ship is landing
        public AudioSource shipTakingOffSound;  // heavy creaking sfx

        void Start()
        {
            yLevel = transform.position.y;
            if (transform.localScale.y > 2.1f) { transform.localScale = new Vector3(2, 2, 2); } // can't be scaled to be larger, very buggy otherwise
            targetYLevel = transform.position.y;
            grappleChain.gameObject.SetActive(false);
        }

        float baseSpeed = 3.2f;
        float speedWhenGrappling = 6.2f;
        public static float cruiserGrappleAscensionSpeed = 2f;
        public void Update()
        {
            if (captain == null) { return; }
            if (!RoundManager.Instance.IsHost) { return; }

            if (transform.localScale.y > 2.1f) { transform.localScale = new Vector3(2, 2, 2); } // can't be scaled to be larger, very buggy otherwise
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
                        if (UnityEngine.Random.Range(0f, 1f) < landChance && !isGrappling)
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
                case "aggressive":  // started by InitPhaseAggressive() from MoaiPirateAI, if it sees someone
                    UpdateAggressive();
                    break;
            }

            // Ease yLevel
            yLevel = Mathf.Lerp(yLevel, targetYLevel, yEaseRate * Time.deltaTime);

            // Apply to ship model
            shipModel.transform.position = new Vector3(transform.position.x, transform.position.y + yLevel, transform.position.z);

            // agent speeds
            if(isGrappling || aggroCruiser)
            {
                agent.speed = speedWhenGrappling;
            }
            else
            {
                agent.speed = baseSpeed;
            }

            if(grapplingCruiser)
            {
                targetYLevel += cruiserGrappleAscensionSpeed * Time.deltaTime;
            }
        }

        public void LateUpdate()
        {
            if (grabbedGO && grapplingCruiser)
            {
                // Cruiser hold phase: chain endpoint bounces around below ship
                chainSwingTimer -= Time.deltaTime;
                if (chainSwingTimer <= 0f)
                {
                    chainSwingTimer = chainSwingInterval;
                    // Pick new random offset below the ship in XZ
                    Vector2 randCircle = UnityEngine.Random.insideUnitCircle * chainSwingRadius;
                    float targetY = shipModel.transform.position.y - chainHangDepth;
                    // Clamp: never below the navagent's Y (ground level)
                    targetY = Mathf.Max(targetY, transform.position.y);
                    chainSimTarget = new Vector3(
                        shipModel.transform.position.x + randCircle.x,
                        targetY,
                        shipModel.transform.position.z + randCircle.y
                    );
                }

                chainSimCurrent = Vector3.Lerp(chainSimCurrent, chainSimTarget, chainSwingEase * Time.deltaTime);
                grappleChain.endPointTransform.position = chainSimCurrent;

                // Keep cruiser pinned to chain tip
                grabbedGO.transform.position = chainSimCurrent;
            }
            else if (grabbedGO)
            {
                // Normal grapple (scrap/enemy) — snap as before
                grabbedGO.transform.position = grappleChain.endPointTransform.position;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  AGGRESSIVE UPDATE  (called every frame while phase == "aggressive")
        // ─────────────────────────────────────────────────────────────────
        private void UpdateAggressive()
        {
            // While the cruiser grapple coroutine is running, ship movement is managed
            // inside CruiserGrappleRoutine — don't interfere here
            if (isGrappling) return;

            // Re-score on timer
            rescoreTimer -= Time.deltaTime;
            if (rescoreTimer <= 0f)
            {
                rescoreTimer = RESCORE_INTERVAL;
                ScoreAndPickTarget();
            }

            // No target — give up, go back to patrolling iff not grappling
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


        public static float fireCannonOverLoweringChance = 0.80f;  // 80% = avg of 5 shots before lowering
        // ─────────────────────────────────────────────────────────────────
        //  SCORING
        // ─────────────────────────────────────────────────────────────────
        private void ScoreAndPickTarget()
        {
            float bestScore = float.MinValue;
            PlayerControllerB bestPlayer = null;
            EnemyAI bestEnemy = null;
            GrabbableObject bestScrap = null;
            VehicleController bestCruiser = null;
            AggressiveAction bestAction = AggressiveAction.Cannon;

            // ── Players ───────────────────────────────────────────────────
            foreach (PlayerControllerB player in FindObjectsOfType<PlayerControllerB>())
            {
                if (player == null || player.isPlayerDead || !player.isPlayerControlled) continue;

                float dist = Vector3.Distance(transform.position, player.transform.position);
                if (dist > MoaiPirateAI.shipSightRange * 1.33f) continue;

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
                    bestCruiser = null;
                    // 85/15: cannon or lower
                    bestAction = (UnityEngine.Random.value < fireCannonOverLoweringChance && dist <= LAND_DIST) ? AggressiveAction.Cannon : AggressiveAction.Lower;
                }
            }

            // ── Enemies ───────────────────────────────────────────────────
            foreach (EnemyAI enemy in FindObjectsOfType<EnemyAI>())
            {
                if (enemy == null || enemy.isEnemyDead) continue;
                if (enemy is MOAIAICORE) continue;  // ignore own kind
                if (enemy.enemyHP <= 0) continue;   // invincible / already dead

                float dist = Vector3.Distance(transform.position, enemy.transform.position);
                if (dist > MoaiPirateAI.shipSightRange * 1.33f) continue;

                float score = (9f * enemy.enemyHP) - dist;
                if (score < MIN_ENEMY_SCORE) continue;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestPlayer = null;
                    bestEnemy = enemy;
                    bestScrap = null;
                    bestCruiser = null;
                    float roll = UnityEngine.Random.value;

                    if (dist <= LAND_DIST)
                    {
                        bestAction = roll < 0.666f ? AggressiveAction.Cannon
                                   : roll < 0.333f ? AggressiveAction.Grapple
                                   : AggressiveAction.Lower;
                    }
                    else // 75% cannon and 25% grapple
                    {
                        bestAction = roll < 0.75f ? AggressiveAction.Cannon : AggressiveAction.Grapple;
                    }
                }
            }

            // ── Scrap ─────────────────────────────────────────────────────
            foreach (GrabbableObject item in FindObjectsOfType<GrabbableObject>())
            {
                if (item == null || !item.itemProperties.isScrap) continue;
                if (item.isHeld || item.isHeldByEnemy) continue;  // skip held items

                float dist = Vector3.Distance(transform.position, item.transform.position);
                if (dist > MoaiPirateAI.shipSightRange * 1.33f) continue;

                float score = item.scrapValue - dist;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestPlayer = null;
                    bestEnemy = null;
                    bestScrap = item;
                    bestCruiser = null;
                    bestAction = AggressiveAction.Grapple;  // scrap always grappled
                }
            }

            // ── Cruiser ───────────────────────────────────────────────────
            // Cruiser is a very high-priority target — score = 180 - dist, always Grapple
            foreach (VehicleController cruiser in FindObjectsOfType<VehicleController>())
            {
                if (cruiser == null) continue;
                if (cruiser.carDestroyed) continue;

                float dist = Vector3.Distance(transform.position, cruiser.transform.position);
                if (dist > MoaiPirateAI.shipSightRange * 1.33f) continue;

                float score = 180f - dist;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestPlayer = null;
                    bestEnemy = null;
                    bestScrap = null;
                    bestCruiser = cruiser;
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
                Debug.Log($"Moai Pirate Ship: Targeting cruiser at {aggroCruiser.transform.position}, action=Grapple, score={bestScore}");
            else
                Debug.Log("Moai Pirate Ship: No valid target found during rescore.");
        }

        private bool HasValidTarget()
        {
            if (aggroPlayer != null)
            {
                if (aggroPlayer.isPlayerDead || !aggroPlayer.isPlayerControlled) return false;
                float dist = Vector3.Distance(transform.position, aggroPlayer.transform.position);
                return dist <= MoaiPirateAI.shipSightRange * 1.33f;
            }
            if (aggroEnemy != null)
            {
                if (aggroEnemy.isEnemyDead || aggroEnemy == null) return false;
                float dist = Vector3.Distance(transform.position, aggroEnemy.transform.position);
                return dist <= MoaiPirateAI.shipSightRange * 1.33f;
            }
            if (aggroScrap != null)
            {
                if (aggroScrap == null) return false;
                float dist = Vector3.Distance(transform.position, aggroScrap.transform.position);
                return dist <= MoaiPirateAI.shipSightRange * 1.33f;
            }
            if (aggroCruiser != null)
            {
                if (aggroCruiser == null || aggroCruiser.carDestroyed) return false;
                float dist = Vector3.Distance(transform.position, aggroCruiser.transform.position);
                return dist <= MoaiPirateAI.shipSightRange * 1.33f;
            }
            return false;
        }

        private Vector3 GetTargetPosition()
        {
            Vector3 pos = Vector3.zero;
            if (aggroPlayer != null)
            {
                pos = aggroPlayer.transform.position;
            }
            if (aggroEnemy != null)
            {
                pos = aggroEnemy.transform.position;
            }
            if (aggroScrap != null)
            {
                pos = aggroScrap.transform.position;
            }
            if (aggroCruiser != null)
            {
                Vector3 cruiserXZ = aggroCruiser.transform.position;


                // upward alternative vector, goes underground and hits terrain surface
                Vector3 samplePoint = GetNearestNode(cruiserXZ).transform.position;

                // using a system that acquires the navmesh mask from an AI node
                GameObject node = GetNearestNode(aggroCruiser.transform.position);
                if (NavMesh.SamplePosition(node.transform.position, out NavMeshHit nodeHit, 5f, NavMesh.AllAreas))
                {
                    Debug.Log("Moai Pirate Ship: Used filter with AI Nodes for tracking cruiser");
                    NavMeshQueryFilter filter2 = new NavMeshQueryFilter
                    {
                        agentTypeID = agent.agentTypeID,
                        areaMask = nodeHit.mask  // mask of the surface the node sits on = environment navmesh
                    };

                    if (NavMesh.SamplePosition(aggroCruiser.transform.position, out NavMeshHit hit2, 15f, filter2))
                        return hit2.position;
                }

                if (NavMesh.SamplePosition(samplePoint, out NavMeshHit navHit, 5f, NavMesh.AllAreas))
                {
                    Debug.Log("Moai Pirate Ship: Used AI Nodes for tracking cruiser");
                    return navHit.position;
                }

                Debug.Log("Moai Pirate Ship: Using fallback for tracking cruiser");
                return aggroCruiser.transform.position; // fallback
            }

            if(pos == Vector3.zero)
            {
                Debug.LogError("Moai Pirate Ship Error: no target available to set destination. This error should not be thrown!");
            }

            NavMeshQueryFilter filter = new NavMeshQueryFilter
            {
                agentTypeID = agent.agentTypeID,
                areaMask = NavMesh.AllAreas
            };

            bool result = NavMesh.SamplePosition(pos, out NavMeshHit hit, 15f, filter);

            if(result)
            {
                return hit.position;
            }
            else
            {
                return pos;
            }
        }

        public GameObject GetNearestNode(Vector3 pos)
        {
            GameObject bestGO = null;
            float bestDist = 999999f;
            foreach(GameObject GO in RoundManager.Instance.outsideAINodes)
            {
                var dist = Vector3.Distance(GO.transform.position, pos);
                if (Vector3.Distance(GO.transform.position, pos) < bestDist)
                {
                    bestDist = dist;
                    bestGO = GO;
                }
            }
            return bestGO;
        }

        // ─────────────────────────────────────────────────────────────────
        //  ACTION EXECUTION
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
                    if (shipHornStealing) { shipHornStealing.Play(); }
                    FireGrapple();
                    break;
                case AggressiveAction.Lower:
                    if (!isGrappling)
                    {
                        InitPhaseLowering();
                        Debug.Log("Aggressive Action: Lowering Ship...");
                    }
                    else
                    {
                        Debug.Log("Aggressive Action: Refusing to lower ship as the ship is grappling something.");
                    }
                    // MoaiPirateAI handles the rest once landed
                    break;
            }

            // Cannon returns to patrolling immediately after firing.
            // Grapple and Lower manage their own exit (GrappleRoutine / CruiserGrappleRoutine call ExitAggressive at the end).
            if (aggroAction == AggressiveAction.Cannon)
            {
                ExitAggressive();
            }
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

        // ─────────────────────────────────────────────────────────────────
        //  GRAPPLE
        // ─────────────────────────────────────────────────────────────────
        public AudioSource grappleFireSound;
        public AudioSource grappleHitSound;
        public AudioSource grappleRetractSound;
        public ShipCableProceduralSimple grappleChain;
        public static float grappleHoldTime = 2.4f;  // 2.4 seconds of holding
        public static float grappleTravelSpeed = 8f;
        public bool isGrappling = false;
        public GameObject grabbedGO;

        private void FireGrapple()
        {
            // Cruiser gets its own dedicated routine — ship physically flees while holding
            if (aggroCruiser != null)
            {
                StartCoroutine(CruiserGrappleRoutine(aggroCruiser));
                return;
            }

            Transform target = null;
            if (aggroEnemy != null) target = aggroEnemy.transform;
            else if (aggroScrap != null) target = aggroScrap.transform;

            if (target == null)
            {
                Debug.Log("Moai Pirate Ship: Grapple — no valid target.");
                ExitAggressive();
                return;
            }

            StartCoroutine(GrappleRoutine(target));
        }

        // ─────────────────────────────────────────────────────────────────
        //  GRAPPLE ROUTINE  (enemy / scrap)
        // ─────────────────────────────────────────────────────────────────
        private IEnumerator GrappleRoutine(Transform target)
        {
            if (isGrappling) { yield break; }

            isGrappling = true;
            actionExecuted = true;

            grappleChain.gameObject.SetActive(true);
            grappleChain.endPointTransform.position = grappleChain.transform.position;
            if (grappleFireSound) grappleFireSound.Play();

            // extend toward target
            while (target != null && Vector3.Distance(grappleChain.endPointTransform.position, target.position) > 0.4f)
            {
                grappleChain.endPointTransform.position = Vector3.MoveTowards(
                    grappleChain.endPointTransform.position, target.position,
                    grappleTravelSpeed * Time.deltaTime);
                yield return null;
            }

            // latch
            if (grappleHitSound) grappleHitSound.Play();

            // disable components that override transforms so we can drag the object
            if (target != null && target.gameObject)
            {
                grabbedGO = target.gameObject;
                var GO = target.gameObject;
                if (GO.GetComponent<EnemyAI>()) { GO.GetComponent<EnemyAI>().enabled = false; }
                if (GO.GetComponent<NavMeshAgent>()) { GO.GetComponent<NavMeshAgent>().enabled = false; }
                if (GO.GetComponent<GrabbableObject>()) 
                {
                    if (VoiceThereBeTreasure) { VoiceThereBeTreasure.Play(); }
                    GO.GetComponent<GrabbableObject>().enabled = false; 
                }
            }

            yield return new WaitForSeconds(grappleHoldTime);

            // retract
            if (grappleRetractSound) grappleRetractSound.Play();
            Vector3 retractTarget = grappleChain.transform.position;
            while (Vector3.Distance(grappleChain.endPointTransform.position, retractTarget) > 0.3f)
            {
                retractTarget = grappleChain.transform.position;
                grappleChain.endPointTransform.position = Vector3.MoveTowards(
                    grappleChain.endPointTransform.position, retractTarget,
                    grappleTravelSpeed * 1.5f * Time.deltaTime);
                yield return null;
            }

            // poof — aggroScrap/aggroEnemy still valid because ExitAggressive hasn't run yet
            if (aggroEnemy != null)
            {
                aggroEnemy.gameObject.SetActive(false);
                Destroy(aggroEnemy.gameObject, 0.1f);
                aggroEnemy = null;
            }
            else if (aggroScrap != null)
            {
                aggroScrap.gameObject.SetActive(false);
                Destroy(aggroScrap.gameObject, 0.1f);
                aggroScrap = null;
            }

            grappleChain.gameObject.SetActive(false);
            isGrappling = false;
            grabbedGO = null;
            ExitAggressive();  // now safe to exit
        }

        // ─────────────────────────────────────────────────────────────────
        //  CRUISER GRAPPLE ROUTINE
        //  The ship rises, latches the chain onto the cruiser, then actively
        //  FLEES away from the nearest player while dragging the cruiser
        //  with it via physics forces. After the hold time, chain retracts
        //  and the ship exits aggressive.
        // ─────────────────────────────────────────────────────────────────
        public AudioSource VoiceThereBeTreasure; // lol
        public static float cruiserGrappleHoldTimeMin = 5f;
        public static float cruiserGrappleHoldTimeMax = 30f;
        public static float cruiserFleeSpeed = 8f;       // how far ahead of itself the ship runs while fleeing
        public static float cruiserFleeSampleRadius = 25f; // NavMesh sample radius when picking flee point
        public static float cruiserDragForce = 60f;      // force applied to cruiser rigidbody each frame
        bool grapplingCruiser = false;
        private IEnumerator CruiserGrappleRoutine(VehicleController cruiser)
        {
            if (isGrappling) { yield break; }

            isGrappling = true;
            actionExecuted = true;

            // ── 1. Rise first so the chain angle makes sense ──────────────
            InitPhaseRising();

            // Wait until the ship has actually risen (yLevel close to targetYLevel)
            while (Mathf.Abs(yLevel - targetYLevel) > 1f)
            {
                yield return null;
            }

            // Lock agent in place while we grapple — we'll drive it manually
            agent.ResetPath();

            // ── 2. Fire chain toward cruiser ──────────────────────────────
            if(VoiceThereBeTreasure) { VoiceThereBeTreasure.Play(); }
            grappleChain.gameObject.SetActive(true);
            grappleChain.endPointTransform.position = grappleChain.transform.position;
            if (grappleFireSound) grappleFireSound.Play();

            while (cruiser != null && !cruiser.carDestroyed &&
                   Vector3.Distance(grappleChain.endPointTransform.position, cruiser.transform.position) > 0.8f)
            {
                grappleChain.endPointTransform.position = Vector3.MoveTowards(
                    grappleChain.endPointTransform.position, cruiser.transform.position,
                    grappleTravelSpeed * Time.deltaTime);
                yield return null;
            }

            // latch
            if (grappleHitSound) grappleHitSound.Play();
            Debug.Log("Moai Pirate Ship: Cruiser grapple latched — beginning flee.");
            grapplingCruiser = true;
            chainSimCurrent = grappleChain.endPointTransform.position;  // start from where chain landed

            // ── 3. Hold + Flee phase ──────────────────────────────────────
            // During this phase:
            //   - ship navigates AWAY from the nearest player each frame
            //   - cruiser rigidbody is pulled toward the chain endpoint (ship underside)
            //   - chain endpoint snaps to cruiser transform each frame (visual only via LateUpdate)
            //   - grabbedGO drives LateUpdate to keep chain tip on the cruiser
            if (captain) 
            { 
                captain.PlayRandomPirateVoiceline(); 
            }
            grabbedGO = cruiser.gameObject;   // LateUpdate will keep chain tip on the cruiser

            Rigidbody cruiserRb = cruiser.GetComponent<Rigidbody>();

            float holdDuration = UnityEngine.Random.Range(cruiserGrappleHoldTimeMin, cruiserGrappleHoldTimeMax);
            float holdTimer = 0f;

            while (holdTimer < holdDuration)
            {
                // Abort if cruiser was destroyed mid-hold
                if (cruiser == null || cruiser.carDestroyed)
                {
                    Debug.Log("Moai Pirate Ship: Cruiser destroyed during grapple hold — releasing.");
                    break;
                }

                holdTimer += Time.deltaTime;

                // ── Find nearest living player ────────────────────────────
                PlayerControllerB nearestPlayer = null;
                float nearestDist = float.MaxValue;
                foreach (PlayerControllerB ply in RoundManager.Instance.playersManager.allPlayerScripts)
                {
                    if (ply == null || ply.isPlayerDead || !ply.isPlayerControlled) continue;
                    float d = Vector3.Distance(transform.position, ply.transform.position);
                    if (d < nearestDist)
                    {
                        nearestDist = d;
                        nearestPlayer = ply;
                    }
                }

                // ── Navigate ship away from that player ──────────────────
                if (nearestPlayer != null)
                {
                    // Direction directly away from player (XZ only — NavAgent stays on mesh)
                    Vector3 awayDir = (transform.position - nearestPlayer.transform.position);
                    awayDir.y = 0f;
                    awayDir.Normalize();

                    Vector3 fleeTarget = transform.position + awayDir * cruiserFleeSpeed;

                    // Sample a valid NavMesh point near the flee target
                    if (NavMesh.SamplePosition(fleeTarget, out NavMeshHit hit, cruiserFleeSampleRadius, NavMesh.AllAreas))
                    {
                        agent.SetDestination(hit.position);
                    }
                }

                // ── Pull cruiser rigidbody toward chain endpoint (ship underside) ──
                if (cruiserRb != null)
                {
                    Vector3 pullDir = (grappleChain.transform.position - cruiser.transform.position).normalized;
                    cruiserRb.AddForce(pullDir * cruiserDragForce, ForceMode.Force);
                }

                yield return null;
            }

            // ── 4. Release — retract chain ────────────────────────────────
            agent.ResetPath();   // stop fleeing
            grabbedGO = null;

            if (grappleRetractSound) grappleRetractSound.Play();
            Debug.Log("Moai Pirate Ship: Releasing cruiser — retracting chain.");

            Vector3 retractTarget = grappleChain.transform.position;
            while (Vector3.Distance(grappleChain.endPointTransform.position, retractTarget) > 0.3f)
            {
                retractTarget = grappleChain.transform.position;
                grappleChain.endPointTransform.position = Vector3.MoveTowards(
                    grappleChain.endPointTransform.position, retractTarget,
                    grappleTravelSpeed * 1.5f + cruiser.speed * Time.deltaTime);
                yield return null;
            }

            grappleChain.gameObject.SetActive(false);
            isGrappling = false;
            grapplingCruiser = false;
            aggroCruiser = null;
            ExitAggressive();
        }

        // ── Chain swing sim ───────────────────────────────────────────────
        private Vector3 chainSimTarget = Vector3.zero;
        private Vector3 chainSimCurrent = Vector3.zero;
        private float chainSwingTimer = 0f;
        public static float chainSwingInterval = 0.2f;   // how often it picks a new random target
        public static float chainSwingRadius = 13f;        // max XZ wander radius
        public static float chainSwingEase = 2f;          // lerp speed toward new target
        public static float chainHangDepth = 12f;          // how far below the ship it hangs

        private void ExitAggressive()
        {
            aggroPlayer = null;
            aggroEnemy = null;
            aggroScrap = null;
            aggroCruiser = null;
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
            if (shipTakingOffSound) { shipTakingOffSound.Play(); }
            phase = "rising";
            targetYLevel = transform.position.y + UnityEngine.Random.Range(lowestHeight, highestHeight);
            Debug.Log("Moai Pirate Ship: rising to height of: " + targetYLevel);
        }

        public void InitPhaseLowering()
        {
            phase = "lowering";
            var validHit = NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 30f, NavMesh.AllAreas);

            if (validHit)
            {
                targetYLevel = hit.position.y;
                if (landingBell) { landingBell.Play(); }
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

        // the ship will go through a phase where it targets enemies through a scoring 
        // system. Then it will select the highest scoring target, executing a specific action
        // based on rng.
        public void InitPhaseAggressive()
        {
            if (shipHornThreaten) { shipHornThreaten.Play(); }
            phase = "aggressive";
            rescoreTimer = 0f;   // score immediately on first update
            actionExecuted = false;
            aggroPlayer = null;
            aggroEnemy = null;
            aggroScrap = null;
            aggroCruiser = null;
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
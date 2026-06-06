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
        public AggressiveAction aggroAction;

        private float rescoreTimer = 0f;
        private const float RESCORE_INTERVAL = 3f;
        private const float ARRIVAL_DIST = 6f;       // XZ distance to consider "arrived" at target
        private const float MIN_ENEMY_SCORE = 20f;

        // stubs fire state
        private bool actionExecuted = false;

        // Audio Sources
        public AudioSource shipHornThreaten;  // more menacing horn  // 
        public static float shipHornThreatenCooldownMin = 0.25f;
        public static float shipHornThreatenCooldownMax = 12f;
        public AudioSource shipHornStealing; // steal sound indicator  //
        public AudioSource landingBell; // indicates the ship is landing  //
        public AudioSource shipTakingOffSound;  // heavy creaking sfx

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
                case "aggressive":  // started by InitPhaseAggressive() from MoaiPirateAI, if it sees someone
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
                    // 85/15: cannon or lower
                    bestAction = UnityEngine.Random.value < fireCannonOverLoweringChance ? AggressiveAction.Cannon : AggressiveAction.Lower;
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
                if (dist > MoaiPirateAI.shipSightRange * 1.33f) continue;

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

            // Apply result
            aggroPlayer = bestPlayer;
            aggroEnemy = bestEnemy;
            aggroScrap = bestScrap;
            aggroAction = bestAction;
            actionExecuted = false;  // new target, reset action flag

            if (aggroPlayer != null)
                Debug.Log($"Moai Pirate Ship: Targeting player {aggroPlayer.playerUsername}, action={aggroAction}, score={bestScore}");
            else if (aggroEnemy != null)
                Debug.Log($"Moai Pirate Ship: Targeting enemy {aggroEnemy.enemyType.enemyName}, action={aggroAction}, score={bestScore}");
            else if (aggroScrap != null)
                Debug.Log($"Moai Pirate Ship: Targeting scrap {aggroScrap.itemProperties.itemName}, score={bestScore}");
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
            return false;
        }

        private Vector3 GetTargetPosition()
        {
            if (aggroPlayer != null) return aggroPlayer.transform.position;
            if (aggroEnemy != null) return aggroEnemy.transform.position;
            if (aggroScrap != null) return aggroScrap.transform.position;
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
                    if (shipHornStealing) { shipHornStealing.Play(); }
                    FireGrapple();
                    break;
                case AggressiveAction.Lower:
                    InitPhaseLowering();
                    Debug.Log("Aggressive Action: Lowering Ship...");
                    // MoaiPirateAI handles the rest once landed
                    break;
            }

            // Cannon and grapple return to patrolling immediately after firing
            // Lower will transition via the landed phase detection in MoaiPirateAI
            if (aggroAction != AggressiveAction.Lower && aggroAction != AggressiveAction.Grapple)
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



        // Method to actually call for grappling
        public AudioSource grappleFireSound;
        public AudioSource grappleHitSound;
        public AudioSource grappleRetractSound;
        public ShipCableProceduralSimple grappleChain;
        public static float grappleHoldTime = 2.4f;  // 2.4 seconds of holding
        public static float grappleTravelSpeed = 4.2f;
        private bool isGrappling = false;
        private void FireGrapple()
        {
            Transform target = null;
            if (aggroEnemy != null) target = aggroEnemy.transform;
            else if (aggroScrap != null) target = aggroScrap.transform;

            if (target == null)
            {
                Debug.Log("Moai Pirate Ship: Grapple — no valid target.");
                return;
            }

            StartCoroutine(GrappleRoutine(target));
        }

        private IEnumerator GrappleRoutine(Transform target)
        {
            isGrappling = true;
            actionExecuted = true;

            grappleChain.gameObject.SetActive(true);
            grappleChain.endPointTransform.position = grappleChain.transform.position;
            if (grappleFireSound) grappleFireSound.Play();

            // extend
            while (target != null && Vector3.Distance(grappleChain.endPointTransform.position, target.position) > 0.4f)
            {
                grappleChain.endPointTransform.position = Vector3.MoveTowards(
                    grappleChain.endPointTransform.position, target.position,
                    grappleTravelSpeed * Time.deltaTime);
                yield return null;
            }

            // latch
            if (grappleHitSound) grappleHitSound.Play();
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
            ExitAggressive();  // ← now safe to exit
        }


        private void ExitAggressive()
        {
            aggroPlayer = null;
            aggroEnemy = null;
            aggroScrap = null;
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
            if(shipTakingOffSound) { shipTakingOffSound.Play(); }
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
                if(landingBell) { landingBell.Play(); }
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
            if (shipHornThreaten) { shipHornThreaten.Play(); };
            phase = "aggressive";
            rescoreTimer = 0f;   // score immediately on first update
            actionExecuted = false;
            aggroPlayer = null;
            aggroEnemy = null;
            aggroScrap = null;
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
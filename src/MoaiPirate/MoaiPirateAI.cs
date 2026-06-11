using System;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using static MoaiEnemy.Plugin;
using static MoaiEnemy.src.MoaiNormal.MoaiNormalNet;
using System.Collections.Generic;
using System.Reflection;
using MoaiEnemy;
using LethalLib.Modules;
using MoaiEnemy.src.MoaiPirate;
using System.Collections;
using System.Threading.Tasks;

namespace MoaiEnemy.src.MoaiNormal
{
    class MoaiPirateAI : MOAIAICORE
    {
        public String currentCommand = "Untamed";
        public GameObject triggerLinkGameObject;

        public MoaiPirateShip ship;

        new enum State
        {
            // defaults
            SearchingForPlayer,
            Guard,
            StickingInFrontOfEnemy,
            StickingInFrontOfPlayer,
            HeadSwingAttackInProgress,
            HeadingToEntrance,
            // custom
            ShipPatrolling,   // patrolling with the ship
            ShipAggressive,   // ship pursuing a scored target
            ShipPlundering,   // grappling hook / vehicle theft (future)
            HeadingToShip     // moai walking back to the ship
        }

        public override void Start()
        {
            baseInit();

            stamina = 100f;
            if (RoundManager.Instance.IsHost)
            {
                // ship setup
                NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 20f, NavMesh.AllAreas);
                GameObject GO = Instantiate(Plugin.PirateShip, hit.position, transform.rotation);
                GO.transform.localScale = transform.localScale;
                GO.GetComponent<NetworkObject>().Spawn();
                ship = GO.GetComponent<MoaiPirateShip>();

                // weapon setup
                SetupInvisShotgun();
            }
            goodBoy = -1;
        }

        // the moai has a blunderbuss that it operates.
        // The blunderbuss is an invisible item that attaches to a transform on the moai
        public Transform shotgunMount;
        private ShotgunItem mountedShotgun;
        public void SetupInvisShotgun()
        {
            // find the shotgun item
            var itemList = Resources.FindObjectsOfTypeAll<Item>();
            foreach(var item in itemList)
            {
                if (item.itemName.ToLower().Contains("shotgun") && item.spawnPrefab.GetComponent<ShotgunItem>())
                {
                    Debug.Log("Moai Pirate: Found shotgun prefab to mount to blunderbuss model. spawning... " + item.itemName);
                    var prefab = item.spawnPrefab;
                    var spawnedShotgun = UnityEngine.GameObject.Instantiate(prefab, transform.position, transform.rotation);
                    mountedShotgun = spawnedShotgun.GetComponent<ShotgunItem>();
                    spawnedShotgun.GetComponent<NetworkObject>().Spawn();

                    if (mountedShotgun.TryGetComponent<Rigidbody>(out var rb))
                    {
                        rb.isKinematic = true;
                    }
                    mountedShotgun.isHeld = true;
                    mountedShotgun.isHeldByEnemy = this;
                    mountedShotgun.hasHitGround = true;
                    mountedShotgun.reachedFloorTarget = true;
                    mountedShotgun.scrapValue = 0;
                    return;
                }
            }
            Debug.LogError("Moai Pirate: failed to find shotgun prefab to use as blunderbuss. Blunderbuss will not fire!");
        }
        
        bool notifiedClientsOfShip = false;
        float randomVoicelineTimer = 4f;
        public static float voicelineDelayLower = 1f;
        public static float voicelineDelayUpper= 16f;
        public override void Update()
        {
            base.Update();
            baseUpdate();

            if (triggerLinkGameObject && RoundManager.Instance.IsHost)
            {
                if (goodBoy > 0)
                {
                    if (!triggerLinkGameObject.activeInHierarchy)
                        triggerLinkEnableClientRpc();
                }
                else
                {
                    if (triggerLinkGameObject.activeInHierarchy)
                        triggerLinkDisableClientRpc();
                }
            }

            if(mountedShotgun)
            {
                mountedShotgun.gameObject.transform.position = shotgunMount.transform.position;
                mountedShotgun.gameObject.transform.rotation = shotgunMount.transform.rotation;
            }

            switch (currentBehaviourStateIndex)
            {
                case (int)State.SearchingForPlayer:
                    break;
                case (int)State.StickingInFrontOfPlayer:
                    break;
            };

            // Notify clients of ship once it's spawned
            if (RoundManager.Instance.IsHost)
            {
                if (ship && ship.NetworkObject && ship.NetworkObject.IsSpawned && !notifiedClientsOfShip)
                {
                    notifiedClientsOfShip = true;
                    ship.SetCaptainClientRpc(NetworkObjectId);
                    NotifyShipClientRpc(ship.NetworkObjectId);
                }
            }

            // Keep moai locked to wheel while boarded
            if (boardedShip)
            {
                transform.position = ship.WheelPoint.transform.position;
                transform.rotation = ship.WheelPoint.transform.rotation;
            }

            // Shotgun fire logic
            if(currentBehaviourStateIndex == (int)State.StickingInFrontOfPlayer)
            {
                UpdateBurstFire();
            }

            randomVoicelineTimer -= Time.deltaTime;
            if(randomVoicelineTimer <= 0)
            {
                PlayRandomPirateVoiceline();
                randomVoicelineTimer = UnityEngine.Random.Range(voicelineDelayLower, voicelineDelayUpper);
            }
        }

        public void PlayRandomPirateVoiceline()
        {
            if(isEnemyDead) { return; }
            if(PirateVoicelines != null && PirateVoicelines.Length > 0)
            {
                PlayVoicelineClientRpc(UnityEngine.Random.Range(0, PirateVoicelines.Length));
            }
        }

        // Audio Source client rpcs
        [ClientRpc]
        public void PlayVoicelineClientRpc(int index)
        {
            PirateVoicelines[index].Play();
        }

        [ClientRpc]
        public void PlayShotgunPrepareClientRpc()
        {
            ShotgunPrepareSound.Play();
        }

        [ClientRpc]
        public void PlayShotgunReloadClientRpc()
        {
            ShotgunReloadSound.Play();
        }

        // shotgun fire feature
        bool firingGun = false;
        public Animator ShotgunAnimator;
        public AudioSource[] PirateVoicelines;  // randomly playeda
        public AudioSource ShotgunPrepareSound;
        public AudioSource ShotgunReloadSound;  // currently unused
        public IEnumerator FireShotgun()
        {
            if (firingGun || isEnemyDead) { yield break; }
            firingGun = true;

            // first yell out a warning!
            PlayRandomPirateVoiceline();
            yield return new WaitForSeconds(0.5f);  // half second before cocking gun
            if (ShotgunPrepareSound) { PlayShotgunPrepareClientRpc(); }  // play cocking sound. Yes this is nonsensical

            // reload and fire animation (state transitions handle this, intentionally backwards)
            ShotgunAnimator.Play("Reload");  // transitions to firing anim
            yield return new WaitForSeconds(1.14f);
            ShotgunAnimator.Play("Fire");  // transitions to firing anim

            // now that we are in the fire animation, actually fire the gun
            int tempHealth = enemyHP;
            enemyHP = 9999;
            mountedShotgun.shellsLoaded = 2;
            mountedShotgun.isReloading = false;
            mountedShotgun.safetyOn = false;
            mountedShotgun.ShootGunAndSync(false);

            yield return new WaitForSeconds(0.3f);
            enemyHP = tempHealth;

            firingGun = false;
        }

        // ── Burst fire ────────────────────────────────────────────────────
        public static int burstShotsMin = 2;
        public static int burstShotsMax = 4;
        public static float burstInterval = 1.8f;   // time between shots in a burst
        public static float burstCooldownMin = 4f;
        public static float burstCooldownMax = 11f;

        private float burstCooldownTimer = 0f;

        public void UpdateBurstFire()
        {
            if (firingGun) return;

            burstCooldownTimer -= Time.deltaTime;
            if (burstCooldownTimer <= 0f)
            {
                burstCooldownTimer = UnityEngine.Random.Range(burstCooldownMin, burstCooldownMax);
                StartCoroutine(BurstFireRoutine());
            }
        }

        private IEnumerator BurstFireRoutine()
        {
                int shots = UnityEngine.Random.Range(burstShotsMin, burstShotsMax + 1);
                for (int i = 0; i < shots; i++)
                {
                    yield return StartCoroutine(FireShotgun());   // wait for each shot to finish before next
                    yield return new WaitForSeconds(burstInterval);
                }

            // moai is done chasing after burst
            stamina = 0f;
        }

        [ClientRpc]
        public void triggerLinkEnableClientRpc()
        {
            triggerLinkGameObject.SetActive(true);
        }

        [ClientRpc]
        public void triggerLinkDisableClientRpc()
        {
            triggerLinkGameObject.SetActive(false);
        }

        float timeLeftPatrollingOffShip = 0f;
        public static float shipSightRange = 60f;

        public override void DoAIInterval()
        {
            if (isEnemyDead || !RoundManager.Instance.IsHost) return;

            base.DoAIInterval();
            baseAIInterval();

            if (transform.localScale.y > 2.1f) { transform.localScale = new Vector3(2, 2, 2); } // can't be scaled to be larger, very buggy otherwise
            agent.acceleration = 8 * moaiGlobalSpeed.Value;

            switch (currentBehaviourStateIndex)
            {
                case (int)State.SearchingForPlayer:
                    baseSearchingForPlayer();

                    if (timeLeftPatrollingOffShip <= 0)
                    {
                        timeLeftPatrollingOffShip = UnityEngine.Random.Range(10f, 35f);
                        SwitchToBehaviourClientRpc((int)State.HeadingToShip);
                        targetPlayer = null;
                        SetDestinationToPosition(GetWheelDestination());
                        StopSearch(currentSearch);
                        return;
                    }
                    timeLeftPatrollingOffShip -= 0.2f;
                    break;

                case (int)State.HeadingToEntrance:
                    // outside-only enemy, redirect immediately
                    SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                    break;

                case (int)State.Guard:
                    if (goodBoy > 0 && currentCommand.Equals("Tamed"))
                        agent.speed = 0;
                    else
                        baseGuard();
                    break;

                case (int)State.StickingInFrontOfEnemy:
                    baseStickingInFrontOfEnemy();
                    break;

                case (int)State.StickingInFrontOfPlayer:
                    baseStickingInFrontOfPlayer();
                    agent.speed = 4.85f * moaiGlobalSpeed.Value;  // base moai speed = 5.3f, pirates are slower than this
                    break;

                case (int)State.HeadSwingAttackInProgress:
                    baseHeadSwingAttackInProgress();
                    break;

                case (int)State.HeadingToShip:
                    if (agent.destination == Vector3.zero || !agent.hasPath)
                    {
                        SetDestinationToPosition(GetWheelDestination());
                        try
                        {
                            if (currentSearch != null) StopSearch(currentSearch);
                        }
                        catch (Exception e) { Debug.LogError(e); }
                    }

                    if (agent.remainingDistance <= 2f)
                    {
                        SnapToWheelClientRpc(true);
                        SwitchToBehaviourClientRpc((int)State.ShipPatrolling);
                        ship.InitPhaseRising();
                        timeLeftPatrollingOffShip = UnityEngine.Random.Range(5f, 20f);
                        return;
                    }
                    break;

                case (int)State.ShipPatrolling:
                    // Exit 1: ship landed naturally — dismount, patrol on foot
                    if (ship.phase.Equals("landed"))
                    {
                        Debug.Log("Pirate Moai: De ship has completed the trip. Looking for vitums yaaarg");
                        SnapToWheelClientRpc(false);
                        SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                        StartSearch(transform.position);
                        return;
                    }

                    // Exit 2: target spotted — hand off to ship's aggressive scoring
                    if (FoundClosestPlayerInRange(shipSightRange, true))
                    {
                        Debug.Log("Pirate Moai: Target spotted! Entering aggressive phase yaaarg");
                        ship.InitPhaseAggressive();
                        SwitchToBehaviourClientRpc((int)State.ShipAggressive);
                        return;
                    }
                    break;

                case (int)State.ShipAggressive:
                    // Ship's UpdateAggressive() handles navigation and action execution.
                    // We just watch for the ship dropping back to traveling/landed,
                    // which signals that the aggressive phase ended.

                    // if grappling, we must remain aggressive until the ship is done doing so
                    if (ship.isGrappling) { return; }

                    if (ship.phase.Equals("traveling") || ship.phase.Equals("rising"))
                    {
                        // Ship finished its aggressive action, resume patrolling
                        Debug.Log("Pirate Moai: Aggressive phase complete, resuming patrol.");
                        SwitchToBehaviourClientRpc((int)State.ShipPatrolling);
                        return;
                    }

                    if (ship.phase.Equals("landed"))
                    {
                        // Ship lowered to attack — dismount, fight on foot
                        Debug.Log("Pirate Moai: Ship landed aggressively, dismounting.");
                        SnapToWheelClientRpc(false);
                        SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                        StartSearch(transform.position);
                        return;
                    }
                    break;

                default:
                    LogDebug("This Behavior State doesn't exist!");
                    break;
            }
        }

        // override is the same as MOAIAICORE except without stamina update from any hit, only the player can make it angry
        public override void HitEnemy(int force = 1, PlayerControllerB playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
        {
            base.HitEnemy(force, playerWhoHit, playHitSFX);
            if (this.isEnemyDead)
            {
                return;
            }
            this.enemyHP -= force;

            if (playerWhoHit != null)
            {
                provokePoints += 20 * force;
                targetPlayer = playerWhoHit;
                stamina = 60f;
            }
            recovering = false;
            if (base.IsOwner)
            {
                if (this.enemyHP <= 0)
                {
                    base.KillEnemyOnOwnerClient(false);
                    this.stopAllSound();
                    animator.SetInteger("state", 3);
                    isEnemyDead = true;
                    moaiSoundPlayClientRpc("creatureDeath");
                    return;
                }

                moaiSoundPlayClientRpc("creatureHit");
            }
        }

        public Vector3 GetWheelDestination()
        {
            NavMesh.SamplePosition(ship.WheelPoint.transform.position, out NavMeshHit hit, 30f, NavMesh.AllAreas);
            return hit.position;
        }

        bool boardedShip = false;

        [ClientRpc]
        public void SnapToWheelClientRpc(bool attach)
        {
            if (attach)
            {
                agent.updatePosition = false;
                boardedShip = true;
            }
            else
            {
                agent.updatePosition = true;
                NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 20f, NavMesh.AllAreas);
                transform.position = hit.position;
                boardedShip = false;
            }
        }

        [ClientRpc]
        public void NotifyShipClientRpc(ulong uid)
        {
            foreach (MoaiPirateShip tempShip in FindObjectsOfType<MoaiPirateShip>())
            {
                if (tempShip.NetworkObjectId == uid)
                {
                    ship = tempShip;
                }
            }
        }
    }
}
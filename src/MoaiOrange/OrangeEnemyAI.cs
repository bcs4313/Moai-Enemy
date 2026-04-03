using MoaiEnemy.src.MoaiNormal;
using System;
using Unity.Netcode;
using UnityEngine.AI;
using UnityEngine;
using static MoaiEnemy.Plugin;
using GameNetcodeStuff;
using System.Collections;
using static UnityEngine.UI.Image;
using static UnityEngine.Rendering.DebugUI;
using Object = UnityEngine.Object;
using System.Threading.Tasks;
using System.Collections.Generic;
using LethalLib.Modules;

namespace MoaiEnemy.src.MoaiOrange
{
    class OrangeEnemyAI : MOAIAICORE
    {

        new enum State
        {
            // defaults
            SearchingForPlayer,
            Guard,
            StickingInFrontOfEnemy,
            StickingInFrontOfPlayer,
            HeadSwingAttackInProgress,
            HeadingToEntrance,

            //define custom below
            // this moai loves dirt, and will drill through it to sneak up on players. 
            Kidnapping, // goes above ground, grabs player/enemy, goes below ground, slowly drowning them. This does tick damage.
            GroundHopping, // goes underground, and bunny hops between multiple locations in and out of the ground
        }

        // the orange moai digs, but he has to emerge for some time to catch his breath
        // 3.6 and 8.4 = 30% of the time above ground and 70% below
        // the moai can become aggressive above ground. If he does he will kidnap the target.
        // if below ground it depends (not sure how to define if he just leaps or kidnaps)
        float surfaceTime = 13.8f;
        float undergroundTime = 16.8f;
        float cycleTimer = 0f;
        bool isUnderground = false;  // moai is only underground after finishing its digging animation
        int diggingState = 0;  // 0 = not digging, 1 = digging up, 2 = digging down
        float digStateTimer = 0f;  // sets a hard limit on the underground digging time

        public AudioSource customMoaiDrillSound;
        public AudioSource customMoaiDigSound;
        bool hasPlayedDigAnim = false;
        bool hasPlayedUndergroundAnim = false;

        // leap vars
        float leapAnimationLength = 1.0f;
        float leapPeakHeight = 3f;
        float timeBetweenLeaps = 1.1f;
        public AudioSource leapPrepareSound;
        public AudioSource[] leapHitSounds;
        bool isInLeapSequence = false;
        bool doneWithLeaps = false;
        float hopMinRange = 12;
        float hopMaxRange = 18;
        public AudioSource explosionImpactSound;
        float beeSpawnChance = 0.25f;

        // explosion vars
        float impactRange = 5.7f;
        int impactDamage = 35;
        float impactForce = 4f;

        // particles
        public ParticleSystem undergroundParticleSystem;
        public ParticleSystem emergeParticleSystem; // non continuous
        public ParticleSystem explosionParticles; // non continuous

        private List<RedLocustBees> spawnedBees;


        // kidnap vars
        PlayerControllerB yoinkedPlayer;
        public Transform playerGrabPoint;
        bool sneakingUpOnPlayer = false;
        float letGoTimer = 0f;  // moai lets go of player after timer ends
        PlayerControllerB targetToYoink;
        private List<Collider> disabledColliders = new List<Collider>();
        float kidnapImpatienceTimer = 12f;  // moai gets impatient and stops being sneaky
        float kidnapImpatienceDuration = 12f;
        float grabRange = 2;

        public override void Start()
        {
            baseInit();
            spawnedBees = new List<RedLocustBees>();
            leapPrepareSound.volume *= (Plugin.moaiGlobalMusicVol.Value / 0.6f);
            for(int i = 0; i < leapHitSounds.Length; i++)
            {
                leapHitSounds[i].volume *= (Plugin.moaiGlobalMusicVol.Value / 0.6f);
            }
            explosionImpactSound.volume *= (Plugin.moaiGlobalMusicVol.Value / 0.6f);
            customMoaiDrillSound.volume *= (Plugin.moaiGlobalMusicVol.Value / 0.6f);
            customMoaiDigSound.volume *= (Plugin.moaiGlobalMusicVol.Value / 0.6f);
        }

        public override void setPitches(float pitchAlter)
        {
            leapPrepareSound.pitch /= pitchAlter;
            for (int i = 0; i < leapHitSounds.Length; i++)
            {
                leapHitSounds[i].pitch /= pitchAlter;
            }
            explosionImpactSound.pitch /= pitchAlter;
            customMoaiDrillSound.pitch /= pitchAlter;
            customMoaiDigSound.pitch /= pitchAlter;
        }

        // bee cleanup
        public override void OnDestroy()
        {
            for(int i = 0; i < spawnedBees.Count; i++)
            {
                var bees = spawnedBees[i];
                destroyBeesClientRpc(bees.NetworkObject.NetworkObjectId);
            }
            spawnedBees.Clear();

            if (RoundManager.Instance.IsHost)
            {
                if (yoinkedPlayer) { letGoOfPlayerClientRpc(yoinkedPlayer.NetworkObject.NetworkObjectId); }
            }

            base.OnDestroy();
        }

        [ClientRpc]
        public void destroyBeesClientRpc(ulong netid)
        {
            var beeArr = UnityEngine.Object.FindObjectsOfType<RedLocustBees>();
            foreach(var bee in beeArr)
            {
                if(bee && bee.NetworkObject.NetworkObjectId == netid)
                {
                    if (bee.hive)
                    {
                        Destroy(bee.hive.gameObject);
                    }
                    Destroy(bee.gameObject);
                }
            }
        }

        [ClientRpc]
        public void moveHiveClientRpc(ulong netid)
        {
            var beeArr = UnityEngine.Object.FindObjectsOfType<RedLocustBees>();
            foreach (var bee in beeArr)
            {
                if (bee && bee.NetworkObject.NetworkObjectId == netid)
                {
                    var hive = bee.hive;
                    hive.targetFloorPosition = new Vector3(-333, -333, -333);
                    hive.fallTime = 0;
                    bee.agent.speed = 4.5f;
                    if(hive.GetComponent<MeshRenderer>())
                    {
                        hive.GetComponent<MeshRenderer>().enabled = false;
                    }

                    if (hive.GetComponent<BoxCollider>())
                    {
                        hive.GetComponent<BoxCollider>().enabled = false;
                    }
                }
            }
        }

        public override void Update()
        {
            base.Update();
            baseUpdate();

            // prevents drowning when grabbed
            if(yoinkedPlayer && yoinkedPlayer.NetworkObject.NetworkObjectId == RoundManager.Instance.playersManager.localPlayerController.NetworkObject.NetworkObjectId)
            {
                StartOfRound.Instance.drowningTimer = (float)(enemyRandom.NextDouble() + 0.1f);
            }

            if (!animator.enabled) { animator.enabled = true; }
            if(!RoundManager.Instance.IsHost) { return; }
            // all host only logic goes under here
            cycleTimer += Time.deltaTime;
            // in the following states the moai will simply cycle between digging and not digging
            // (defined by underground time and surface time)
            // searchingforplayer, guard, headingtoentrance
            if (currentBehaviourStateIndex == (int)State.SearchingForPlayer || currentBehaviourStateIndex == (int)State.Guard || currentBehaviourStateIndex == (int)State.HeadingToEntrance)
            {
                //cycleTimer += Time.deltaTime;
                if (isUnderground)
                {
                    if(cycleTimer > undergroundTime)
                    {
                        cycleTimer = 0f;
                        diggingState = 1;
                        hasPlayedDigAnim = false;
                        hasPlayedUndergroundAnim = false;
                    }
                }
                else
                {
                    if(cycleTimer > surfaceTime)
                    {
                        cycleTimer = 0f;
                        diggingState = 2;
                        hasPlayedDigAnim = false;
                        hasPlayedUndergroundAnim = false;
                    }
                }
            }

            // goes above ground to eat / attack enemy
            if(currentBehaviourStateIndex == (int)State.HeadSwingAttackInProgress || currentBehaviourStateIndex == (int)State.StickingInFrontOfEnemy)
            {
                if (isUnderground)
                {
                    if (cycleTimer > 2.5)
                    {
                        cycleTimer = 0f;
                        diggingState = 1;
                        hasPlayedDigAnim = false;
                        hasPlayedUndergroundAnim = false;
                    }
                }
            }

            // enforce animation speed 
            if(currentBehaviourStateIndex == (int)State.GroundHopping)
            {
                animator.speed = 1;
            }

            // handle emerging from ground
            if (diggingState == 1)
            {
                digStateTimer += Time.deltaTime;

                if (!hasPlayedDigAnim)
                {
                    orangeEmergeClientRpc();
                    hasPlayedDigAnim = true;
                }

                if (digStateTimer > 1.1f)
                {
                    diggingState = 0;
                    isUnderground = false;
                    hasPlayedDigAnim = false;
                    digStateTimer = 0f;
                }
            }
            // handle digging into ground
            else if (diggingState == 2)
            {
                digStateTimer += Time.deltaTime;

                if (!hasPlayedDigAnim)
                {
                    orangeDigClientRpc();
                    hasPlayedDigAnim = true;
                }

                if (digStateTimer > 1.1f)
                {
                    diggingState = 0;
                    isUnderground = true;
                    hasPlayedDigAnim = false;
                    digStateTimer = 0f;
                }
            }
            else
            {
                digStateTimer = 0f;
            }

            // handle effects from being underground
            if(isUnderground && diggingState == 0)
            {
                // underground effects proc (post digging animation)
                if (!animator.GetCurrentAnimatorStateInfo(0).IsName("RemainUnderground") && !hasPlayedUndergroundAnim)
                {
                    undergroundActivateClientRpc();
                    hasPlayedUndergroundAnim = true;
                }

                if(!customMoaiDigSound.isPlaying)
                {
                    playUndergroundSoundClientRpc(true);
                }
            }
            else
            {
                hasPlayedUndergroundAnim = false;
                if(customMoaiDigSound.isPlaying)
                {
                    playUndergroundSoundClientRpc(false);
                }
            }

            // revert back to normal animator if diggingstate == 0
            // ape code
            if ((animator.GetCurrentAnimatorStateInfo(0).IsName("RemainUnderground") || animator.GetCurrentAnimatorStateInfo(0).IsName("DigUp") || 
                animator.GetCurrentAnimatorStateInfo(0).IsName("DigDown")) && diggingState == 0 && isUnderground == false)
            {
                playWalkClientRpc();
            }
        }

        [ClientRpc]
        public void playWalkClientRpc()
        {
            animator.Play("Walk");
        }

        public async void summonBees(Vector3 position)
        { // I can 100% confirm this works now, no issues
            // this code ACTUALLY spawns the bees. Took forever to figure out.
            // improved to NOT need an id
            RedLocustBees[] beeArr = Resources.FindObjectsOfTypeAll<RedLocustBees>();
            RedLocustBees beeobj = beeArr[0];
            Debug.Log("Orange Moai: spawning bee obj: " + beeobj.name + " - " + beeobj.GetType() + " - " + beeobj.ToString());
            GameObject beebase = beeobj.enemyType.enemyPrefab;
            GameObject bees = UnityEngine.Object.Instantiate<GameObject>(beebase, position, Quaternion.identity, new GameObject().transform);

            bees.GetComponentInChildren<MeshRenderer>().enabled = false;
            bees.GetComponentInChildren<Unity.Netcode.NetworkObject>().Spawn(true);
            //m.SpawnedEnemies.Add(bees.GetComponent<EnemyAI>());

            // gameobject spawned is "bees"
            bees.SetActive(true);
            var comp = bees.GetComponent<RedLocustBees>();
            spawnedBees.Add(comp);  // automatically managed

            // bees will despawn in some time (4-10 seconds)
            await Task.Delay((int)(enemyRandom.NextDouble() * 5000 + 15000));
            destroyBeesClientRpc(comp.NetworkObject.NetworkObjectId);
        }

        // asset rip from lethal company code, used so moai doesn't self dmg
        public static void moaiSpawnExplosion(Vector3 explosionPosition, float killRange = 1f, float damageRange = 1f, int nonLethalDamage = 50, float physicsForce = 0f, GameObject overridePrefab = null, bool goThroughCar = false)
        {
            float num = Vector3.Distance(GameNetworkManager.Instance.localPlayerController.transform.position, explosionPosition);
            if (num < 14f)
            {
                HUDManager.Instance.ShakeCamera(ScreenShakeType.Big);
            }
            else if (num < 25f)
            {
                HUDManager.Instance.ShakeCamera(ScreenShakeType.Small);
            }
            bool flag = false;
            Collider[] array = Physics.OverlapSphere(explosionPosition, damageRange, 2621448, QueryTriggerInteraction.Collide);
            RaycastHit raycastHit;
            PlayerControllerB playerControllerB;
            for (int i = 0; i < array.Length; i++)
            {
                float num2 = Vector3.Distance(explosionPosition, array[i].transform.position);
                if (!Physics.Linecast(explosionPosition, array[i].transform.position + Vector3.up * 0.3f, out raycastHit, 1073742080, QueryTriggerInteraction.Ignore) || ((goThroughCar || raycastHit.collider.gameObject.layer != 30) && num2 <= 4f))
                {
                    if (array[i].gameObject.layer == 3 && !flag)
                    {
                        playerControllerB = array[i].gameObject.GetComponent<PlayerControllerB>();
                        if (playerControllerB != null && playerControllerB.IsOwner)
                        {
                            flag = true;
                            if (num2 < killRange)
                            {
                                Vector3 vector = Vector3.Normalize(playerControllerB.gameplayCamera.transform.position - explosionPosition) * 80f / Vector3.Distance(playerControllerB.gameplayCamera.transform.position, explosionPosition);
                                playerControllerB.KillPlayer(vector, true, CauseOfDeath.Blast, 0, default(Vector3));
                            }
                            else if (num2 < damageRange)
                            {
                                Vector3 vector = Vector3.Normalize(playerControllerB.gameplayCamera.transform.position - explosionPosition) * 80f / Vector3.Distance(playerControllerB.gameplayCamera.transform.position, explosionPosition);
                                playerControllerB.DamagePlayer(nonLethalDamage, true, true, CauseOfDeath.Blast, 0, false, vector);
                            }
                        }
                    }
                    else if (array[i].gameObject.layer == 19)
                    {
                        EnemyAICollisionDetect componentInChildren2 = array[i].gameObject.GetComponentInChildren<EnemyAICollisionDetect>();
                        if (componentInChildren2 != null && componentInChildren2.mainScript.IsOwner && num2 < 4.5f)
                        {
                            if (componentInChildren2.gameObject.name.ToLower().Contains("moai"))
                            {
                                componentInChildren2.mainScript.HitEnemyOnLocalClient(6, default(Vector3), null, false, -1);
                                componentInChildren2.mainScript.HitFromExplosion(num2);
                            }
                        }
                    }
                }
            }
            playerControllerB = GameNetworkManager.Instance.localPlayerController;
            if (physicsForce > 0f && Vector3.Distance(playerControllerB.transform.position, explosionPosition) < 35f && !Physics.Linecast(explosionPosition, playerControllerB.transform.position + Vector3.up * 0.3f, out raycastHit, 256, QueryTriggerInteraction.Ignore))
            {
                float num3 = Vector3.Distance(playerControllerB.transform.position, explosionPosition);
                Vector3 b = Vector3.Normalize(playerControllerB.transform.position + Vector3.up * num3 - explosionPosition) / (num3 * 0.35f) * physicsForce;
                if (b.magnitude > 2f)
                {
                    if (b.magnitude > 10f)
                    {
                        playerControllerB.CancelSpecialTriggerAnimations();
                    }
                    if (!playerControllerB.inVehicleAnimation || (playerControllerB.externalForceAutoFade + b).magnitude > 50f)
                    {
                        playerControllerB.externalForceAutoFade += b;
                    }
                }
            }
            VehicleController vehicleController = Object.FindObjectOfType<VehicleController>();
            if (vehicleController != null && !vehicleController.magnetedToShip && physicsForce > 0f && Vector3.Distance(vehicleController.transform.position, explosionPosition) < 35f)
            {
                vehicleController.mainRigidbody.AddExplosionForce(physicsForce * 50f, explosionPosition, 12f, 3f, ForceMode.Impulse);
            }
            int layerMask = ~LayerMask.GetMask(new string[]
            {
            "Room"
            });
            layerMask = ~LayerMask.GetMask(new string[]
            {
            "Colliders"
            });
            array = Physics.OverlapSphere(explosionPosition, 10f, layerMask);
            for (int j = 0; j < array.Length; j++)
            {
                Rigidbody component = array[j].GetComponent<Rigidbody>();
                if (component != null)
                {
                    component.AddExplosionForce(70f, explosionPosition, 10f);
                }
            }
        }

        [ClientRpc]
        public void explosionClientRpc()
        {
            explosionParticles.Play();
            explosionImpactSound.Play();
            moaiSpawnExplosion(this.transform.position, 0f, impactRange, impactDamage, impactForce, null, true);  // landmine has 5.7f kill range and 6f damage range, with 50 physics force
        }

        [ClientRpc]
        public void playUndergroundSoundClientRpc(bool active)
        {
            if (active)
            {
                customMoaiDigSound.Play();
            }
            else
            {
                customMoaiDigSound.Stop();
            }
        }
        [ClientRpc]
        public void playHitSoundClientRpc()
        {
            leapHitSounds[enemyRandom.Next(0, leapHitSounds.Length)].Play();
        }

        [ClientRpc]
        public void playPrepareSoundClientRpc()
        {
            leapPrepareSound.Play();
        }
        
        // play the digging animation
        [ClientRpc]
        public void orangeDigClientRpc()
        {
            animator.Play("DigDown");
            stopAllSound();
            customMoaiDrillSound.Play();
        }

        // play the hop animation
        [ClientRpc]
        public void hopAnimationClientRpc()
        {
            animator.Play("Hop");
            stopAllSound();
        }

        // play the emerge animation
        [ClientRpc]
        public void orangeEmergeClientRpc()
        {
            animator.Play("DigUp");
            stopAllSound();
            customMoaiDrillSound.Play();
        }

        // this animation simply leaves the moai underground
        [ClientRpc]
        public void undergroundActivateClientRpc()
        {
            animator.Play("RemainUnderground");
        }

        // the more a player is looking away, the more likely an orange moai will attempt to sneak up on you
        bool ShouldKidnap(PlayerControllerB target)
        {
            if (target == null || target.isPlayerDead || yoinkedPlayer != null)
                return false;

            float distance = Vector3.Distance(transform.position, target.transform.position);
            if (distance > 16.5f)
                return false;

            // Vector from player to Moai
            Vector3 toMoai = (transform.position - target.gameplayCamera.transform.position).normalized;
            float lookDot = Vector3.Dot(target.gameplayCamera.transform.forward, toMoai);

            // Calculate bonus from view angle (dot = -1 means fully turned away)
            float baseChance = 0.15f;       // 15% if looking at Moai
            float bonusMultiplier = Mathf.Clamp01((-lookDot - 0.1f) / 0.9f);
            // Maps lookDot from -1 to 0.1 → 0 to 1
            float finalChance = Mathf.Lerp(baseChance, 1f, bonusMultiplier);

            float roll = UnityEngine.Random.Range(0f, 1f);
            return roll < finalChance;
        }

        public override void DoAIInterval()
        {
            if(!RoundManager.Instance.IsHost) { return; }
            letGoTimer -= 0.2f;
            if (isEnemyDead)
            {
                if(customMoaiDigSound.isPlaying) { customMoaiDigSound.Stop(); }
                if (RoundManager.Instance.IsHost)
                {
                    if (yoinkedPlayer) { letGoOfPlayerClientRpc(yoinkedPlayer.NetworkObject.NetworkObjectId); }
                }
                return;
            };

            if(isUnderground)
            {
                if(!undergroundParticleSystem.isEmitting) { ToggleUndergroundParticlesClientRpc(true); }
            }
            else
            {
                if (undergroundParticleSystem.isEmitting) { ToggleUndergroundParticlesClientRpc(false); }
            }

            // make the moai very quiet if its trying to sneak up on the player
            if(currentBehaviourStateIndex == (int)State.Kidnapping && sneakingUpOnPlayer)
            {
                customMoaiDrillSound.volume = 0.15f * Plugin.moaiGlobalMusicVol.Value;
                customMoaiDigSound.volume = 0.1f * Plugin.moaiGlobalMusicVol.Value;
            }
            else
            {
                customMoaiDrillSound.volume = 0.647f * Plugin.moaiGlobalMusicVol.Value;
                customMoaiDigSound.volume = 0.208f * Plugin.moaiGlobalMusicVol.Value;
            }

            /*
            if(yoinkedPlayer) 
            {
                if (!quicksandTrigger.activeInHierarchy) { quicksandTrigger.SetActive(true); }
            }
            else
            {
                if(quicksandTrigger.activeInHierarchy) { quicksandTrigger.SetActive(false); }
            }
            */

            // cleanup of spawned bees
            // shift of hive away to anger bees
            List<RedLocustBees> removeTargets = new List<RedLocustBees>();
            for(int i = 0; i < spawnedBees.Count; i++)
            {
                RedLocustBees bees = spawnedBees[i];
                if(bees == null)
                {
                    removeTargets.Add(bees);
                }
                else
                {
                    if(bees.hive)
                    {
                        moveHiveClientRpc(bees.NetworkObject.NetworkObjectId);
                    }
                }
            }

            for (int i = 0; i < removeTargets.Count; i++)
            {
                RedLocustBees bees = removeTargets[i];
                spawnedBees.Remove(bees);
            }

            base.DoAIInterval();
            baseAIInterval();

            if (yoinkedPlayer && yoinkedPlayer.isPlayerDead) { yoinkedPlayer = null; }
            if(currentBehaviourStateIndex != (int)State.GroundHopping) { isInLeapSequence = false; }

            // kidnapping stuff
            if ((letGoTimer < 0 && yoinkedPlayer != null) || (yoinkedPlayer != null && yoinkedPlayer.isPlayerDead))
            {
                letGoOfPlayerClientRpc(yoinkedPlayer.NetworkObject.NetworkObjectId);
            }

            agent.acceleration = 8 * moaiGlobalSpeed.Value;
            switch (currentBehaviourStateIndex)
            {
                case (int)State.SearchingForPlayer:
                    baseSearchingForPlayer();
                    break;
                case (int)State.HeadingToEntrance:
                    baseHeadingToEntrance();
                    break;
                case (int)State.Guard:
                    baseGuard();
                    break;
                case (int)State.StickingInFrontOfEnemy:
                    baseStickingInFrontOfEnemy();
                    break;
                case (int)State.StickingInFrontOfPlayer:
                    baseStickingInFrontOfPlayer();

                    // two scenarios, either the moai will kidnap or charge up a leap attack
                    if (targetPlayer)
                    {
                        if(ShouldKidnap(targetPlayer))
                        {
                            // swap with kidnap behaviour later
                            sneakingUpOnPlayer = true;
                            kidnapImpatienceTimer = kidnapImpatienceDuration;
                            targetToYoink = targetPlayer;
                            SwitchToBehaviourClientRpc((int)State.Kidnapping);
                            StopSearch(currentSearch);
                        }
                        else
                        {
                            SwitchToBehaviourClientRpc((int)State.GroundHopping);
                            doneWithLeaps = false;
                        }
                    }
                    break;

                case (int)State.HeadSwingAttackInProgress:
                    baseHeadSwingAttackInProgress();
                    break;
                case (int)State.GroundHopping:
                    agent.speed = 0;
                    if (!isInLeapSequence && !doneWithLeaps)
                    {
                        isInLeapSequence = true;
                        StartCoroutine(DoGroundHopSequence(UnityEngine.Random.Range(2, 5)));
                    }
                    else if (doneWithLeaps)
                    {
                        StartSearch(transform.position);
                        targetPlayer = null;  // to prevent following the player afterwards
                        SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                    }
                    break;
                case (int)State.Kidnapping:
                    // stay underground until close to player
                    // once close, emerge and attempt to grab the player
                    // then go back underground
                    // revert case 1 (player is lost/dies)
                    stopAllSound();
                    if(!targetToYoink || targetToYoink.isPlayerDead || Vector3.Distance(transform.position, targetToYoink.transform.position) > 35f) 
                    {
                        SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                        StartSearch(transform.position);
                        return;
                    }

                    SetDestinationToPosition(targetToYoink.transform.position);

                    if (sneakingUpOnPlayer)
                    {
                        agent.speed = 5.3f * moaiGlobalSpeed.Value;
                        kidnapImpatienceTimer -= 0.2f;
                        if (isUnderground)
                        {
                            if(Vector3.Distance(transform.position, targetToYoink.transform.position) < grabRange * transform.localScale.x)
                            {
                                sneakingUpOnPlayer = false;
                                diggingState = 1;  // digging up
                            }
                        }
                        else
                        {
                            diggingState = 2;  // digging down
                        }

                        // moai can lose its patience in 12 seconds and no longer try to kidnap someone
                        if(kidnapImpatienceTimer <= 0)
                        {
                            cycleTimer = 0;
                            diggingState = 1; // dig back up
                            letGoTimer = (float)(enemyRandom.NextDouble() * 25 + 2);
                            StartSearch(transform.position);
                            SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                        }
                    }
                    else
                    {
                        if (isUnderground)
                        {
                            diggingState = 1;  // digging up
                        }
                        else
                        { // grab player if still in range
                            if (Vector3.Distance(transform.position, targetToYoink.transform.position) < grabRange * transform.localScale.x)
                            {
                                attachPlayerClientRpc(targetToYoink.NetworkObject.NetworkObjectId, true);
                                cycleTimer = 0;
                                diggingState = 2; // dig back down
                                letGoTimer = (float)(enemyRandom.NextDouble() * 25 + 2);
                                StartSearch(transform.position);
                                SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                            }
                            else
                            {
                                // give up
                                letGoTimer = (float)(enemyRandom.NextDouble() * 25 + 2);
                                StartSearch(transform.position);
                                SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                            }
                        }
                    }
                    break;
                /*
                case (int)State.KidnapInProgress:
                    agent.speed = 5.6f * moaiGlobalSpeed.Value;
                    // roams around, causing chaos
                    targetPlayer = null;
                    if(currentSearch == null) { StartSearch(transform.position); }

                    break;
                */
                default:
                    LogDebug("This Behavior State doesn't exist!");
                    break;
            }
        }

        [ClientRpc]
        public void attachPlayerClientRpc(ulong playerId, bool healPlayer = false)
        {
            RoundManager m = RoundManager.Instance;
            PlayerControllerB[] players = m.playersManager.allPlayerScripts;
            for (int i = 0; i < players.Length; i++)
            {
                PlayerControllerB player = players[i];
                if (player.NetworkObject.NetworkObjectId == playerId)
                {
                    yoinkedPlayer = player;
                    player.transform.position = playerGrabPoint.position;
                    player.transform.parent = playerGrabPoint;
                    player.playerCollider.enabled = false;
                    //DisablePlayerColliders(player);
                    if (healPlayer) { player.DamagePlayer(-30); }
                    return;
                }
            }
        }

        [ClientRpc]
        public void letGoOfPlayerClientRpc(ulong playerId)
        {
            PlayerControllerB targetPlayer = null;
            RoundManager m = RoundManager.Instance;
            PlayerControllerB[] players = m.playersManager.allPlayerScripts;
            for (int i = 0; i < players.Length; i++)
            {
                PlayerControllerB player = players[i];
                if (player.NetworkObject.NetworkObjectId == playerId)
                {
                    targetPlayer = player;
                }

            }
            targetPlayer.transform.parent = null;
            targetPlayer.playerCollider.enabled = true;

            // the player needs to be on a navmesh spot (avoiding all collision bugs)
            NavMeshHit hit;
            var valid = UnityEngine.AI.NavMesh.SamplePosition(targetPlayer.transform.position, out hit, 15f, NavMesh.AllAreas);
            if (valid)
            {
                targetPlayer.transform.position = hit.position;
                //EnablePlayerColliders(targetPlayer);
            }
            yoinkedPlayer = null;
        }

        [ClientRpc]
        public void ToggleUndergroundParticlesClientRpc(bool value)
        {
            if(value)
            {
                undergroundParticleSystem.Play();
            }
            else
            {
                undergroundParticleSystem.Stop();
            }
        }

        [ClientRpc]
        public void PlayEmergeParticlesClientRpc()
        {
            emergeParticleSystem.Play(); // one shot particles
        }

        public IEnumerator DoGroundHopSequence(int amount)
        {
            agent.enabled = false;

            // Dig down animation
            diggingState = 2;
            orangeDigClientRpc();
            yield return new WaitForSeconds(1.1f);
            ToggleUndergroundParticlesClientRpc(true);
            playPrepareSoundClientRpc();

            yield return new WaitForSeconds(0.7f);
            undergroundParticleSystem.Stop();

            diggingState = 0;

            // Execute each hop
            for (int i = 0; i < amount; i++)
            {
                Vector3 dir = UnityEngine.Random.insideUnitSphere;
                dir.y = 0;
                dir.Normalize();

                stamina -= 20f;

                ToggleUndergroundParticlesClientRpc(false);
                PlayEmergeParticlesClientRpc();
                playHitSoundClientRpc();

                if(targetPlayer == null) { break; }
                Vector3 target = GetSmartHopTarget(transform.position, targetPlayer.transform.position, hopMinRange, hopMaxRange);
                yield return StartCoroutine(DoGroundHop(target));
                yield return new WaitForSeconds(UnityEngine.Random.Range(0.4f, 0.6f));
            }

            // Final dig-up after last hop
            //diggingState = 1;
            //orangeEmergeClientRpc();
            playHitSoundClientRpc();
            yield return new WaitForSeconds(1.1f);

            agent.enabled = true;
            isInLeapSequence = false;
            doneWithLeaps = true;

            if(stamina < 0f) { stamina = 0f; }
            recovering = true;
        }

        Vector3 GetSmartHopTarget(Vector3 origin, Vector3 playerPos, float minRange, float maxRange)
        {
            Vector3 toPlayer = playerPos - origin;
            float distToPlayer = toPlayer.magnitude;

            // 1. If within minimum range, hop directly onto player
            if (distToPlayer < minRange)
                return playerPos;

            // 2. Prefer jumps toward the player but with some scatter
            Vector3 bestCandidate = playerPos;
            float bestScore = float.MinValue;

            for (int i = 0; i < 10; i++)
            {
                // Create a biased direction: 70% toward player, 30% noise
                Vector3 randomOffset = UnityEngine.Random.insideUnitSphere;
                randomOffset.y = 0f;
                Vector3 dir = (toPlayer.normalized * 0.7f + randomOffset.normalized * 0.3f).normalized;

                float distance = UnityEngine.Random.Range(minRange, maxRange);
                Vector3 candidate = origin + dir * distance;

                if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 4f, NavMesh.AllAreas))
                {
                    // Score based on closeness to player (closer = more aggressive)
                    float score = -Vector3.Distance(hit.position, playerPos);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestCandidate = hit.position;
                    }
                }
            }

            return bestCandidate;
        }
        public override void OnCollideWithPlayer(Collider other)
        {
            if (timeSinceHittingLocalPlayer < 0.5f)
            {
                return;
            }
            PlayerControllerB playerControllerB = MeetsStandardPlayerCollisionConditions(other);
            if (playerControllerB != null)
            {
                //LogIfDebugBuild("Example Enemy Collision with Player!");
                timeSinceHittingLocalPlayer = 0f;
                if (!transform.Find("Halo") || !transform.Find("Halo").gameObject.activeSelf)
                {
                    if (playerControllerB.health < 30)
                    {
                        playerControllerB.KillPlayer(playerControllerB.velocityLastFrame, true, CauseOfDeath.Mauling, 0);
                    }
                    else
                    {
                        if (!isUnderground && currentBehaviourStateIndex != (int)State.Kidnapping)
                        {
                            playerControllerB.DamagePlayer(30);
                        }
                    }
                }
                else // normal moai good boy effect is healing
                {
                    if (transform.Find("Halo").gameObject.activeSelf && playerControllerB.health <= 90)
                    {
                        playerControllerB.DamagePlayer(-10);
                    }
                }
            }
        }


        public IEnumerator DoGroundHop(Vector3 targetPos)
        {
            // begin a pop up animation (moai should arc over the ground)
            hopAnimationClientRpc(); // peak height at 25 frames, upside down at 50 frames, back underground at 60 frames

            // Moai is now underground — tween to new position in arc
            Vector3 start = transform.position;
            float duration = leapAnimationLength;
            float elapsed = 0f;
            float arcPeak = leapPeakHeight;

            while (elapsed < duration)
            {
                float t = elapsed / duration;
                float height = Mathf.Sin(t * Mathf.PI) * arcPeak;
                transform.position = Vector3.Lerp(start, targetPos, t) + Vector3.up * height;
                elapsed += Time.deltaTime;
                yield return null;
            }

            explosionClientRpc();

            /*
            elapsed = 0;
            duration = timeBetweenLeaps / 2;
            while (elapsed < duration)
            {
                float t = elapsed / duration;
                float height = Mathf.Sin(t * Mathf.PI) * arcPeak;
                transform.position = Vector3.Lerp(start, targetPos, t) + Vector3.up * height;
                elapsed += Time.deltaTime;
                yield return null;
            }
            */

            transform.position = targetPos;

            // BEES BEES BEES BEES BEES!
            if (enemyRandom.NextDouble() < beeSpawnChance)
            {
                summonBees(this.transform.position);
            }

            if(yoinkedPlayer)
            {
                yoinkedPlayer.DamagePlayer(10);
            }

            // Remain underground!
            undergroundActivateClientRpc();
            isUnderground = true;
            yield return new WaitForSeconds(timeBetweenLeaps);
        }

        /*
        [ClientRpc]
        void DisablePlayerColliders(PlayerControllerB player)
        {
            disabledColliders.Clear();
            foreach (var col in player.GetComponentsInChildren<Collider>())
            {
                if (col.enabled)
                {
                    col.enabled = false;
                    disabledColliders.Add(col);
                }
            }
        }

        [ClientRpc]
        void EnablePlayerColliders(PlayerControllerB player)
        {
            foreach (var col in disabledColliders)
            {
                if (col != null) col.enabled = true;
            }
            disabledColliders.Clear();
        }
        */
    }
}

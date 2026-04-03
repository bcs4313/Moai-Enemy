using System;
using System.Collections;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UIElements;
using static MoaiEnemy.Plugin;
using System.Threading.Tasks;
using System.Linq;
using static MoaiEnemy.src.MoaiNormal.MoaiNormalNet;
using LethalLib.Modules;
using UnityEngine.SocialPlatforms;
using static UnityEngine.UI.GridLayoutGroup;
using static UnityEngine.ParticleSystem.PlaybackState;

namespace MoaiEnemy.src.MoaiNormal
{

    class PurpleEnemyAI : MOAIAICORE
    {
        // Audio Sources
        public AudioSource creatureGrab;
        public AudioSource creatureHold;
        public AudioSource creatureThrow;
        public AudioSource creatureTeleportOut;
        public AudioSource creatureTeleportIn;

        // teleport vars
        protected float teleportCooldownSetRoaming = 8;
        protected float teleportCooldownSetPlayer = 2f;
        protected float teleportCooldown = 8;
        protected PlayerControllerB playerTeleAnchor = null;
        protected float playerGrabTime = 0f;
        protected float playerGrabDuration = 0f;
        protected float playerThrowMin = 60f;
        protected float playerThrowMax = 150f;
        protected float playerGrabStamina = 0f;
        public GameObject teleportEffect;

        // pad vars
        protected float padSpawnChance = 0.1f;
        protected int tempHp = 0;
        protected int tempHpCD = 0;

        // throw effect
        public GameObject holdEffect;

        // throw vars
        protected int scanRate = 25;  // every 5 seconds
        public Transform throwPoint;
        float sightRange = 20f;
        protected EnemyAI currentEnemyHeld = null;
        protected PlayerControllerB currentPlayerHeld = null;
        protected GameObject currentTrapHeld = null;
        protected GameObject currentTrapThrowing = null;
        protected EnemyAI currentEnemyThrowing = null;
        Vector3 throwTarget = Vector3.zero;
        protected int healthBeforeGrab = 0;

        // cooldown related throw vars
        // the moai needs a grab cooldown
        // only applies if patrolling
        public float enemyGrabTime = 0f;
        public float enemyReleaseTime = 0f;
        public float enemyGrabDuration = 0f;
        public float enemyReleaseDuration = 0f;
        public float enemyAngerTime = 0f;

        // used in Vector3.Lerp of Throw
        protected float startThrowTime = 0;
        protected float throwTravelDuration = 0.5f;
        protected float throwArcHeight = 3;

        public static EnemyAI[] managedEnemies;

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
            Grabbing,
            Throwing,
            Teleporting
        }

        public override void Start()
        {
            baseInit();
            holdEffect.SetActive(false);
            creatureGrab.volume = moaiGlobalMusicVol.Value * 1.35f;
            creatureHold.volume = moaiGlobalMusicVol.Value;
            creatureThrow.volume = moaiGlobalMusicVol.Value * 1.45f;
            creatureTeleportOut.volume = moaiGlobalMusicVol.Value;
            creatureTeleportIn.volume = moaiGlobalMusicVol.Value;
        }

        public override void setPitches(float pitchAlter)
        {
            creatureGrab.pitch /= pitchAlter;
            creatureHold.pitch /= pitchAlter;
            creatureThrow.pitch /= pitchAlter;
            creatureTeleportOut.pitch /= pitchAlter;
            creatureTeleportIn.pitch /= pitchAlter;
        }

        public void grabEnemy(EnemyAI ai)
        {
            // Assign to moai transform
            ai.transform.parent = this.transform;
            ai.transform.localPosition = throwPoint.transform.localPosition;

            // disable the enemy (for now)
            ai.enabled = false;
            ai.agent.enabled = false;
            ai.isEnemyDead = true;
            ai.creatureAnimator.enabled = false;
            healthBeforeGrab = ai.enemyHP;
            currentEnemyHeld = ai;

            // make the enemy shut up
            foreach(AudioSource source in ai.gameObject.GetComponentsInChildren<AudioSource>())
            {
                source.Stop();
            }

            foreach (AudioSource source in ai.gameObject.GetComponentsInParent<AudioSource>())
            {
                source.Stop();
            }

            playGrabSoundClientRpc();
            enemyGrabTime = Time.time;
            enemyGrabDuration = (float)(enemyRandom.NextDouble() * 60f);
        }

        public void grabTrap(GameObject trap)
        {
            // Assign to moai transform
            trap.transform.parent = this.transform;
            trap.transform.localPosition = throwPoint.transform.localPosition;
            currentTrapHeld = trap;

            playGrabSoundClientRpc();
        }

        public void letGoOfTrap(GameObject trap)
        {
            // Assign to moai transform
            trap.transform.parent = null;
            currentTrapHeld = null;

            // snap trap to navmesh position
            NavMeshHit hit;
            if (NavMesh.SamplePosition(trap.transform.position, out hit, 15f, NavMesh.AllAreas))
            {
                trap.transform.position = hit.position;
            }

            stopGrabSoundClientRpc();
        }

        public bool notHoldingSomething()
        {
            return !currentEnemyHeld && !currentPlayerHeld && !currentTrapHeld;
        }

        [ClientRpc]
        public void grabPlayerClientRpc(ulong playerId)
        {
            RoundManager m = RoundManager.Instance;
            PlayerControllerB[] players = m.playersManager.allPlayerScripts;
            for (int i = 0; i < players.Length; i++)
            {
                PlayerControllerB ply = players[i];
                if (ply.NetworkObject.NetworkObjectId == playerId)
                {
                    // Assign to moai transform
                    ply.transform.position = throwPoint.transform.position;
                    ply.transform.parent = throwPoint.transform;

                    // disable the player hitbox (for now)
                    ply.playerCollider.enabled = false;

                    currentPlayerHeld = ply;

                    playGrabSoundClientRpc();
                    playerGrabTime = Time.time;

                    var rand = enemyRandom.NextDouble();
                    if (rand < 0.75f)
                    {  // short duration
                        playerGrabDuration = (float)enemyRandom.NextDouble() * 3;
                    }
                    else if (rand < 0.95f)
                    {  // long duration
                        playerGrabDuration = (float)enemyRandom.NextDouble() * 9 + 3;
                    }
                    else
                    {  // VERY long duration
                        playerGrabDuration = (float)enemyRandom.NextDouble() * 18 + 5;
                    }

                    // to remove the chasing noise and go back to normal state until the throw is done
                    playerGrabStamina = stamina;
                    stamina = 0;
                }
            }
        }

        [ClientRpc]
        public void letGoOfPlayerClientRpc(ulong playerId, float launch = 0f)
        {
            // player snap calcluation
            Vector3 snapPos = Vector3.zero;

            NavMeshHit hit;
            if (NavMesh.SamplePosition(this.mouth.position, out hit, 15f, NavMesh.AllAreas))
            {
                snapPos = hit.position;
            }

            RoundManager m = RoundManager.Instance;
            PlayerControllerB[] players = m.playersManager.allPlayerScripts;
            for (int i = 0; i < players.Length; i++)
            {
                PlayerControllerB ply = players[i];
                if (ply.NetworkObject.NetworkObjectId == playerId)
                {
                    // Remove the grabbing parent transform
                    ply.transform.parent = null;

                    // re-enable the player hitbox
                    ply.playerCollider.enabled = true;


                    // ignore snapPos if we aren't out of bounds
                    RaycastHit rayhit;
                    if (Physics.Raycast(ply.transform.position, UnityEngine.Vector3.down, out rayhit, 7f * transform.localScale.magnitude))
                    {
                        snapPos = Vector3.zero;
                    }

                    if (snapPos != Vector3.zero)
                    {
                        ply.transform.position = snapPos;
                    }

                    currentPlayerHeld = null;

                    if(launch != 0f)
                    {
                        tempHp = enemyHP;
                        enemyHP = 9999;
                        if (snapPos != Vector3.zero)
                        {
                            Landmine.SpawnExplosion(snapPos, false, 0, 0, 0, launch);
                        }
                        else
                        {
                            Landmine.SpawnExplosion(this.mouth.position, false, 0, 0, 0, launch);
                        }
                        tempHpCD = 6;
                        playThrowSoundClientRpc();
                        stamina = playerGrabStamina;  // back to chasing state
                    }

                    stopGrabSoundClientRpc();
                }
            }
        }

        public void letGoOfEnemy(EnemyAI ai)
        {
            // Assign to moai transform
            ai.transform.parent = null;

            // enable the enemy
            ai.enabled = true;
            ai.agent.enabled = true;
            ai.isEnemyDead = false;
            ai.creatureAnimator.enabled = true;

            if (healthBeforeGrab < 0)  // REVIVE THEM
            {
                ai.enemyHP = 2;
            }
            else
            {
                healthBeforeGrab = ai.enemyHP;
            }

            // un angel any moai
            if (ai.name.ToLower().Contains("moai"))
            {
                try
                {
                    MOAIAICORE mai = (MOAIAICORE)ai;
                    mai.goodBoy = -42;  // heh
                }
                catch (Exception e)
                {

                }

                if (ai.name.ToLower().Contains("red"))
                {
                    try
                    {
                        RedEnemyAI red = (RedEnemyAI)ai;
                        red.angerMoai();  // heh
                    }
                    catch (Exception e)
                    {

                    }
                }
            }

            // snap enemy to navmesh position
            NavMeshHit hit;
            if (NavMesh.SamplePosition(ai.transform.position, out hit, 15f, NavMesh.AllAreas))
            {
                ai.agent.Warp(hit.position);
            }
            enemyReleaseTime = Time.time;
            stopGrabSoundClientRpc();

            if ((Time.time - enemyAngerTime) < 8f)
            {
                enemyReleaseDuration = 0;
            }
            else
            {
                enemyReleaseDuration = (float)(enemyRandom.NextDouble() * 10f) + 5;
            }


            currentEnemyHeld = null;
        }

        [ClientRpc]
        public void playGrabSoundClientRpc()
        {
            creatureGrab.Play();
            creatureHold.Play();
            holdEffect.SetActive(true);
        }

        [ClientRpc]
        public void stopGrabSoundClientRpc()
        {
            creatureHold.Stop();
            holdEffect.SetActive(false);
        }

        [ClientRpc]
        public void playThrowSoundClientRpc()
        {
            creatureThrow.Play();
        }

        protected EnemyAI ClosestEnemyInRangePurple(float range)
        {
            var enemies = managedEnemies;

            var closestDist = range;
            EnemyAI closestEnemy = null;

            foreach (var enemy in enemies)
            {
                if (enemy == null)
                {
                    continue;
                }

                // General filtering
                var dist = Vector3.Distance(transform.position, enemy.transform.position);
                if (dist < closestDist && enemy.enemyHP > 0 && !enemy.isEnemyDead && !unreachableEnemies.Contains(enemy) && enemy.GetInstanceID() != GetInstanceID())
                {
                    closestDist = dist;
                    closestEnemy = enemy;
                }
            }

            return closestEnemy;
        }


        public Vector3 pickThrowTargetAI()
        {
            if(!targetPlayer)
            {
                return Vector3.zero;
            }
            NavMeshHit hit;
            var sample = NavMesh.SamplePosition(targetPlayer.transform.position + targetPlayer.velocityLastFrame, out hit, 20f, NavMesh.AllAreas);
            if (sample)
            {
                return hit.position;
            }
            return Vector3.zero;
        }

        [ClientRpc]
        public void updateThrowPositionClientRpc(Vector3 position, Quaternion rotation, ulong uid)
        {
            var enemies = managedEnemies;
            EnemyAI cur_enemy = null;

            foreach (var enemy in enemies)
            {
                if (enemy == null)
                {
                    continue;
                }

                if (enemy.NetworkObject.NetworkObjectId == uid)
                {
                    cur_enemy = enemy;
                    break;
                }
            }

            if (cur_enemy)
            {
                if (rotation != Quaternion.identity)
                {
                    cur_enemy.transform.rotation = rotation;
                }

                cur_enemy.transform.position = position;
            }
        }

        public override void Update()
        {
            base.Update();
            baseUpdate();

            teleportCooldown -= Time.deltaTime;

            // vector3 transition in throw
            if (currentEnemyThrowing && throwTarget != Vector3.zero)
            {
                var timeSinceThrow = Time.time - startThrowTime;
                var t = timeSinceThrow / throwTravelDuration;  //  t is a normalized lerp value (0->1)
                Vector3 lerpPos = Vector3.Lerp(throwPoint.transform.position, throwTarget, t);  // half second throw

                // add vertical arc
                float verticalOffset = -4 * throwArcHeight * t * t + 4 * throwArcHeight * t;

                var rot = currentEnemyThrowing.transform.rotation;

                if (currentEnemyThrowing.name.ToLower().Contains("moai"))
                {
                    rot = new Quaternion(rot.x, this.transform.rotation.y, rot.z, rot.w);
                }
                updateThrowPositionClientRpc(new Vector3(lerpPos.x, lerpPos.y + verticalOffset, lerpPos.z), rot, currentEnemyThrowing.NetworkObject.NetworkObjectId);

                if (timeSinceThrow >= throwTravelDuration)
                {
                    letGoOfEnemy(currentEnemyThrowing);
                    currentEnemyHeld = null;
                    currentEnemyThrowing = null;
                    throwTarget = Vector3.zero;
                }
            }

            // vector3 transition in throw 2
            if (currentTrapThrowing && throwTarget != Vector3.zero)
            {
                var timeSinceThrow = Time.time - startThrowTime;
                var t = timeSinceThrow / throwTravelDuration;  //  t is a normalized lerp value (0->1)
                Vector3 lerpPos = Vector3.Lerp(throwPoint.transform.position, throwTarget, t);  // half second throw

                // add vertical arc
                float verticalOffset = -4 * throwArcHeight * t * t + 4 * throwArcHeight * t;

                currentTrapThrowing.transform.position = new Vector3(lerpPos.x, lerpPos.y + verticalOffset, lerpPos.z);
                var rot = currentTrapThrowing.transform.rotation;

                if (currentTrapThrowing.name.ToLower().Contains("moai"))
                {
                    currentTrapThrowing.transform.rotation.Set(rot.x, this.transform.rotation.y, rot.z, rot.w);
                }

                if (timeSinceThrow >= throwTravelDuration)
                {
                    letGoOfTrap(currentTrapThrowing);
                    currentTrapHeld = null;
                    currentTrapThrowing = null;
                    throwTarget = Vector3.zero;
                }
            }

            // stop sounds and let go of everything if dead
            if (isEnemyDead || enemyHP <= 0)
            {
                if (currentEnemyHeld)
                {
                    letGoOfEnemy(currentEnemyHeld);
                    currentEnemyHeld = null;
                }
                if(currentPlayerHeld)
                {
                    letGoOfPlayerClientRpc(currentPlayerHeld.NetworkObject.NetworkObjectId);
                    currentPlayerHeld = null;
                }
                if (creatureHold.isPlaying)
                {
                    creatureHold.Stop();
                }
            }

            // let go of enemy if held for too long
            if(currentEnemyHeld && (Time.time - this.enemyGrabTime) > enemyGrabDuration)
            {
                if((Time.time - enemyAngerTime) > 8)
                {
                    letGoOfEnemy(currentEnemyHeld);
                }
            }

            // teleport update logic
            if(creatureTeleportOut.isPlaying)
            {
                // include particle effects
                if (!teleportEffect.activeInHierarchy)
                {
                    teleportEffect.SetActive(true);
                }

                // teleport moai at certain time
                if(creatureTeleportOut.time >= 0.58f && playerTeleAnchor && teleportCooldown <= 0)
                {
                    teleportMoaiAroundPlayer();
                    playerTeleAnchor = null;
                    teleportCooldown = teleportCooldownSetPlayer;
                }
            }
            else
            {
                // remove particle effects
                if (teleportEffect.activeInHierarchy)
                {
                    teleportEffect.SetActive(false);
                }
            }
        }

        public void teleportMoaiAroundPlayer()
        {
            for (int i = 0; i < 5; i++)
            {
                Vector3 targetPoint = Vector3.zero;
                if (playerTeleAnchor)
                {
                    // teleport around player (typically behind them)
                    Vector3 playerPos = playerTeleAnchor.transform.position;

                    Vector3 playerBackward = -playerTeleAnchor.transform.forward.normalized;

                    // Generate a random angle within the rotation range
                    float randomAngle = (float)(enemyRandom.NextDouble() * 210 - 105);

                    // Rotate the backward vector by the random angle
                    Vector3 randomDirection = Quaternion.Euler(0, randomAngle, 0) * playerBackward;

                    float range = 0;
                    if(enemyRandom.NextDouble() > 0.5)
                    {
                        range = 1.5f;  // tight range
                    }
                    else
                    {
                        range = 5;  // wide range
                    }

                    targetPoint = playerPos + (randomDirection * (float)((enemyRandom.NextDouble() * range)+2f + (this.transform.localScale.magnitude * 2f)));
                }
                else
                {
                    randomTeleport();
                    return;
                }

                if (targetPoint != Vector3.zero)
                {
                    NavMeshHit hit;
                    bool result = NavMesh.SamplePosition(targetPoint, out hit, 25f, NavMesh.AllAreas);
                    if (result)
                    {
                        this.transform.position = hit.position;
                        return;
                    }
                }
            }
        }

        public void randomTeleport()
        {
            Vector3 targetPoint = Vector3.zero;
            // random tele
            var x = transform.position.x;
            var y = transform.position.y;
            var z = transform.position.z;

            var rand_x = (float)enemyRandom.NextDouble() * 40f - 20f;
            var rand_y = transform.position.y;
            var rand_z = (float)enemyRandom.NextDouble() * 40f - 20f;

            targetPoint = new Vector3(x + rand_x, y + rand_y, z + rand_z);

            if (targetPoint != Vector3.zero)
            {
                NavMeshHit hit;
                bool result = NavMesh.SamplePosition(targetPoint, out hit, 25f, NavMesh.AllAreas);
                if (result)
                {
                    this.transform.position = hit.position;
                    return;
                }
            }

        }

        [ClientRpc]
        public void stopSFXClientRpc()
        {
            creatureSFX.Stop();
        }

        [ClientRpc]
        public void updateEnemiesClientRpc()
        {
            managedEnemies = FindObjectsOfType<EnemyAI>();
        }


        public override void DoAIInterval()
        {
            if (isEnemyDead)
            {
                return;
            };
            base.DoAIInterval();
            baseAIInterval();

            if(sourcecycle % scanRate == 0)
            {
                updateEnemiesClientRpc();
            }

            // throw a player if held
            if((Time.time - playerGrabTime) > playerGrabDuration && currentPlayerHeld)
            {
                float force = (float)(playerThrowMin + (enemyRandom.NextDouble() * (playerThrowMax - playerThrowMin) * enemyRandom.NextDouble()));
                letGoOfPlayerClientRpc(currentPlayerHeld.NetworkObject.NetworkObjectId, force);
            }

            // temp HP for explosion
            if(tempHpCD > 0)
            {
                tempHpCD --;
                if(tempHpCD == 1)
                {
                    enemyHP = tempHp;
                }
            }

            switch (currentBehaviourStateIndex)
            {
                case (int)State.SearchingForPlayer:
                    // if an enemy is found, go up and grab the enemy. The enemy is then disabled.
                    //ClosestEnemyInRange(28)

                    if(creatureSFX.isPlaying)
                    {
                        stopSFXClientRpc();
                    }

                    // go grab an enemy!
                    if(ClosestEnemyInRangePurple(sightRange) && goodBoy <= 0 && notHoldingSomething())
                    {
                        // cooldown check
                        if ((Time.time - enemyReleaseTime) > enemyReleaseDuration)
                        {
                            SwitchToBehaviourClientRpc((int)State.StickingInFrontOfEnemy);
                        }
                    }
                    baseSearchingForPlayer();
                    break;
                case (int)State.HeadingToEntrance:
                    baseHeadingToEntrance();
                    break;
                case (int)State.Guard:
                    baseGuard();
                    break;
                case(int)State.Throwing:
                    agent.speed = 1.5f * moaiGlobalSpeed.Value;

                    // Enemy Throwing
                    if (currentEnemyHeld && !currentEnemyThrowing)
                    {
                        currentEnemyThrowing = currentEnemyHeld;
                        throwTarget = pickThrowTargetAI();
                        startThrowTime = Time.time;
                        if(throwTarget != Vector3.zero)
                        {
                            playThrowSoundClientRpc();
                        }
                    }
                    if(currentEnemyThrowing && throwTarget == Vector3.zero)
                    {
                        letGoOfEnemy(currentEnemyThrowing);
                        currentEnemyHeld = null;
                        currentEnemyThrowing = null;
                        throwTarget = Vector3.zero;
                        creatureVoice.Play();
                        voicePlayClientRpc();
                        SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                    }

                    // trap throwing
                    if (currentTrapHeld && !currentTrapThrowing)
                    {
                        currentEnemyThrowing = currentEnemyHeld;
                        throwTarget = pickThrowTargetAI();
                        startThrowTime = Time.time;
                        if (throwTarget != Vector3.zero)
                        {
                            playThrowSoundClientRpc();
                        }
                    }
                    if (currentTrapThrowing && throwTarget == Vector3.zero)
                    {
                        letGoOfTrap(currentTrapThrowing);
                        currentTrapHeld = null;
                        currentTrapThrowing = null;
                        throwTarget = Vector3.zero;
                        creatureVoice.Play();
                        voicePlayClientRpc();
                        SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                    }

                    if (notHoldingSomething())
                    {
                        creatureVoice.Play();
                        voicePlayClientRpc();
                        SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                    }
                    break;
                case (int)State.StickingInFrontOfEnemy:

                    if (goodBoy >= 0)
                    {
                        baseStickingInFrontOfEnemy();
                    }
                    else
                    {
                        targetPlayer = null;
                        agent.speed = 7f * moaiGlobalSpeed.Value;
                        var closestMonster = ClosestEnemyInRangePurple(sightRange);
                        this.stamina -= 1.5f;  // all stamina (150) is lost in 15 seconds?

                        // sound switch 
                        if (!creatureSFX.isPlaying)
                        {
                            moaiSoundPlayClientRpc("creatureSFX");
                        }

                        // mark off enemy if unreachable
                        if (agent.pathStatus == NavMeshPathStatus.PathPartial || agent.pathStatus == NavMeshPathStatus.PathInvalid)
                        {
                            unreachableEnemies.Add(closestMonster);
                        }

                        if (!closestMonster || (Time.time - enemyReleaseTime) < enemyReleaseDuration)
                        {
                            StartSearch(transform.position);
                            SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                        }

                        if(!notHoldingSomething())
                        {
                            StartSearch(transform.position);
                            SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                        }

                        // Charge into monster
                        StalkPos = closestMonster.transform.position;
                        SetDestinationToPosition(StalkPos, checkForPath: false);
                    }
                    break;
                case (int)State.StickingInFrontOfPlayer:
                    enemyAngerTime = Time.time;
                    // if we are holding an enemy, THROW IT!
                    if (currentEnemyHeld || currentTrapHeld)
                    {
                        SwitchToBehaviourClientRpc((int)State.Throwing);
                    }

                    // teleport trigger
                    if (notHoldingSomething() && teleportCooldown <= 0)
                    {
                        // if the cooldown for teleport is done, the moai
                        // will teleport at a random time + 2 seconds
                        if (enemyRandom.NextDouble() <= 0.09f)
                        {
                            stopAllSound();
                            creatureTeleportOut.Stop();
                            creatureTeleportOut.time = 0;
                            SwitchToBehaviourClientRpc((int)State.Teleporting);
                        }
                    }

                    // pad spawner
                    if (notHoldingSomething())
                    {
                        // if the cooldown for teleport is done, the moai
                        // will spawn a pad at a random time, avg time = 2.5 seconds
                        if (enemyRandom.NextDouble() <= padSpawnChance)
                        {
                            // spawn pad
                            spawnPad();
                        }
                    }

                    baseStickingInFrontOfPlayer();
                    break;

                case (int)State.Teleporting:
                    agent.speed = 1.5f * moaiGlobalSpeed.Value;

                    if(targetPlayer)
                    {
                        playerTeleAnchor = targetPlayer;

                        if (!creatureTeleportOut.isPlaying)
                        {
                            creatureTeleportOutPlayClientRpc();
                        }

                        targetPlayer = null;
                    }
                    
                    if(!targetPlayer && !playerTeleAnchor)
                    {
                        creatureVoice.Play();
                        voicePlayClientRpc();
                        StartSearch(transform.position);
                        SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                    }
                    break;

                case (int)State.HeadSwingAttackInProgress:
                    baseHeadSwingAttackInProgress();
                    break;
                default:
                    LogDebug("This Behavior State doesn't exist!");
                    break;
            }
        }

        [ClientRpc]
        public void creatureTeleportOutPlayClientRpc()
        {
            creatureTeleportOut.Play();
        }

        public void spawnPad()
        {
            if(!RoundManager.Instance.IsHost)
            {
                return;
            }

            // select position
            Vector3 spawnPos = selectPadSpawnPosition();
            if(spawnPos == Vector3.zero) { return; }

            // now for actually spawning the prefab
            GameObject gameObject = UnityEngine.Object.Instantiate(Plugin.PlasmaPad, spawnPos, Plugin.PlasmaPad.transform.rotation);
            gameObject.transform.parent = null;
            gameObject.transform.position = spawnPos;
            gameObject.SetActive(value: true);
            gameObject.GetComponent<NetworkObject>().Spawn();
            gameObject.GetComponent<QuantumPad>().owner = this;
        }

        public Vector3 selectPadSpawnPosition()
        {
            // we need a player for this to work
            if(!targetPlayer) { return Vector3.zero; }
            Vector3 playerPos = targetPlayer.transform.position;
            Vector3 playerVector = targetPlayer.transform.forward.normalized;

            // Generate a random angle within the rotation range
            float randomAngle = (float)(enemyRandom.NextDouble() * 360);

            // Rotate the vector by the random angle
            Vector3 randomDirection = Quaternion.Euler(0, randomAngle, 0) * playerVector;

            // scale vector, place on navmesh, and return it
            Vector3 selectedPoint = playerPos + (randomDirection * (float)(enemyRandom.NextDouble() * 10 + 1.5));

            NavMeshHit hit;
            bool result = NavMesh.SamplePosition(selectedPoint, out hit, 20f, NavMesh.AllAreas);

            if(result)
            {
                return hit.position;
            }
            else
            {
                return Vector3.zero;
            }
        }

        public override void OnCollideWithEnemy(Collider other, EnemyAI collidedEnemy = null)
        {
            if (this.timeSinceHittingLocalPlayer < 0.5f || collidedEnemy.isEnemyDead || isEnemyDead)
            {
                return;
            }
            this.timeSinceHittingLocalPlayer = 0f;

            if(notHoldingSomething() && (Time.time - enemyReleaseTime) > enemyReleaseDuration)
            {
                grabEnemy(collidedEnemy);

                if (this.currentBehaviourStateIndex == (int)State.StickingInFrontOfPlayer)
                {
                    StartSearch(transform.position);
                }
            }
            else
            {
                if (collidedEnemy.gameObject.name.ToLower().Contains("moai"))
                {
                    // halos don't hit halos, non-halos don't hit non-halos
                    if (transform.Find("Halo").gameObject.activeSelf == collidedEnemy.transform.Find("Halo").gameObject.activeSelf)
                    {
                        return;
                    }
                }
                collidedEnemy.HitEnemy(1, null, true);
            }
        }

        void OnCollisionEnter(UnityEngine.Collision collision)
        {
            if (!RoundManager.Instance.IsHost)
            {
                return;
            }

            var hit = collision.gameObject;
            if (collision.gameObject)
            {
                // grab gameObject if it is a hazard
                var trapOptions = RoundManager.Instance.currentLevel.spawnableMapObjects;
                foreach(SpawnableMapObject option in trapOptions)
                {
                    if(isTrapGO(hit))
                    {
                        grabTrap(hit);
                    }
                }
            }
        }

        /**
        public void grabNearbyTraps()
        {
            if (!RoundManager.Instance.IsHost)
            {
                return;
            }

            Physics.OverlapSphere()

            RoundManager.Instance.spawnedSyncedObjects

            var hit = collision.gameObject;
            if (collision.gameObject)
            {
                // grab gameObject if it is a hazard
                var trapOptions = RoundManager.Instance.currentLevel.spawnableMapObjects;
                foreach (SpawnableMapObject option in trapOptions)
                {
                    if (isTrapGO(hit))
                    {
                        grabTrap(hit);
                    }
                }
            }
        }
        **/

        public bool isTrapGO(GameObject GO)
        {
            var trapOptions = RoundManager.Instance.currentLevel.spawnableMapObjects;
            foreach (SpawnableMapObject option in trapOptions)
            {
                if (option.prefabToSpawn.name.Equals(GO.name))
                {
                    return true;
                }
            }
            return false;
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
                    if(notHoldingSomething() && goodBoy <= 0)
                    {
                        playerControllerB.DamagePlayer(10);
                        if (RoundManager.Instance.IsHost)
                        {
                            grabPlayerClientRpc(playerControllerB.NetworkObject.NetworkObjectId);
                        }
                        else
                        {
                            grabPlayerServerRpc(playerControllerB.NetworkObject.NetworkObjectId);
                        }
                    }
                    else if (playerControllerB.health < 30)
                    {
                        playerControllerB.KillPlayer(playerControllerB.velocityLastFrame, true, CauseOfDeath.Mauling, 0);
                    }
                    else
                    {
                        playerControllerB.DamagePlayer(30);
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

        [ServerRpc(RequireOwnership = false)]
        public void grabPlayerServerRpc(ulong netid)
        {
            grabPlayerClientRpc(netid);
        }

        [ClientRpc]
        public void voicePlayClientRpc()
        {
            if(!creatureVoice.isPlaying)
            {
                creatureVoice.Play();
            }
        }

        public override void HitEnemy(int force = 1, PlayerControllerB playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
        {
            base.HitEnemy(force, playerWhoHit, playHitSFX);
            if (this.isEnemyDead)
            {
                return;
            }

            if (playerWhoHit != null)
            {
                provokePoints += 20 * force;
                stamina = 60;
            }
        }
    }
}
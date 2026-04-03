using GameNetcodeStuff;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using static MoaiEnemy.src.MoaiNormal.MoaiNormalNet;
using static MoaiEnemy.Plugin;
using Unity.Netcode;
using UnityEngine.AI;
using MoaiEnemy.src.Utilities;
using LethalLib.Modules;
using static UnityEngine.GraphicsBuffer;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Rendering;
using System.Numerics;
using Vector3 = UnityEngine.Vector3;
using Quaternion = UnityEngine.Quaternion;

namespace MoaiEnemy.src.MoaiNormal
{
    // using this to gain more control over the ai system...
    class SoulDevourerAI : EnemyAI
    {
        protected Animator animator;

        // patrol variables
        protected float starePoints = 0;
        protected float angerRate = 110;
        protected int attackState = 0; // 3 states of attack, 0, 1, 2
        bool proccedRoar = false;
        bool proccedCrouch = false;
        bool proccedRun = false;
        bool proceedShred = false;
        bool proceedLeap = false;
        PlayerControllerB shreddedPlayer = null;
        public Collider uprightCollider;
        public Collider foursCollider;

        // thrash variables
        public Transform mouth;
        protected bool thrashingCorpse = false;
        protected PlayerControllerB attachedPlayer = null;
        float attachTime = 0;
        bool isOutSwitch = false;
        float lastThrashTime = 0f;
        public Transform legCastPoint;  // used to see if we are allowed to thrash

        // leap variables
        protected float baseLeapChance = 100;
        protected float leaptime = 0;
        protected Vector3 leapPos;

        // related to entering and exiting entrances
        // updated once every 4-ish seconds
        protected EntranceTeleport nearestEntrance = null;
        public Vector3 nearestEntranceNavPosition = Vector3.zero;
        protected PlayerControllerB mostRecentPlayer = null;
        protected int entranceDelay = 0;  // prevents constant rentry / exit
        protected float chanceToLocateEntrancePlayerBonus = 0;
        protected float chanceToLocateEntrance = 0;

        // updated once every 15 seconds
        public List<EnemyAI> unreachableEnemies = new List<EnemyAI>();
        public Vector3 itemNavmeshPosition = Vector3.zero;
        protected int sourcecycle = 75;

        // stamina mechancis
        protected float stamina = 0; // moai use stamina to chase the player
        protected bool recovering = false; // moai don't chase if they are recovering
        public int provokePoints = 0;

#pragma warning disable 0649
        public Transform turnCompass;
#pragma warning restore 0649
        protected float timeSinceHittingLocalPlayer;
        protected float timeSinceNewRandPos;
        protected Vector3 positionRandomness;
        protected Vector3 StalkPos;
        protected System.Random enemyRandom;
        protected bool isDeadAnimationDone;

        // flee logic
        private float timeSinceFleeStart = 0f;
        private Vector3 fleeDestination = Vector3.zero;
        public ParticleSystem shadowParticles;
        private bool awaitingShadowMode = false;
        private bool shadowEnable = false;   // starts on shadow enable false until a certain duraton
        private bool isVisible = true;
        public GameObject meshRenderRoot;
        float timeControl = 1f;

        // custom sounds
        public AudioSource creatureRoar;
        public AudioSource creatureHit1;
        public AudioSource creatureHit2;
        int hitRotation = 1;  // just what hit sound to play (oscillates)
        public AudioSource creatureStare;
        public AudioSource creatureExtract;
        public AudioSource creatureLeap;
        public AudioSource creatureDeath;
        public AudioSource creatureFlee;
        bool markDead = false;

        public Volume postProcessVolume;  // for glitch / blood pulse effect
        protected float processScalar = 0.2f;

        // animation vars (new)
        protected float runAnimationCoefficient = 14f;
        protected float walkAnimationCoefficient = 3f;

        public enum State
        {
            SearchingForPlayer,
            Staring,
            //StickingInFrontOfPlayer,
            HeadingToEntrance,
            Attacking,
            Shredding,
            Leaping,
            Fleeing,
            HeadingToMusic,
            Crying
        }

        public void LogDebug(string text)
        {
#if DEBUG
            Plugin.Logger.LogInfo(text);
#endif
        }

        public void facePosition(Vector3 pos)
        {
            Vector3 directionToTarget = pos - transform.position;
            directionToTarget.y = 0f; // Ignore vertical difference

            // If directionToTarget is not zero, rotate to face target
            if (directionToTarget != Vector3.zero)
            {
                // Calculate the rotation to face the target only in the Y-axis (yaw)
                Quaternion targetRotation = Quaternion.LookRotation(directionToTarget);

                // Apply the rotation to the object's transform, preserving current pitch and roll
                transform.rotation = Quaternion.Euler(0f, targetRotation.eulerAngles.y, 0f);
            }
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
        }

        public PlayerControllerB getNearestPlayer()
        {
            var players = RoundManager.Instance.playersManager.allPlayerScripts;

            PlayerControllerB bestPlayer = null;
            float bestDistance = 999999999f;
            for (int i = 0; i < players.Length; i++)
            {
                var player = players[i];
                if (player && !player.isPlayerDead && player.isPlayerControlled && Vector3.Distance(this.transform.position, player.transform.position) < bestDistance)
                {
                    bestDistance = Vector3.Distance(this.transform.position, player.transform.position);
                    bestPlayer = player;
                }
            }

            return bestPlayer;
        }

        // just does distance checks on the item ship and boomboxes (maybe jester too)
        private bool musicPresent = false;
        private Vector3 musicDest = Vector3.zero;
        private float musicDist = 9999;
        public void listenForMusic()
        {
            try
            {
                // try to find item dropship, if present, music is playing!
                var go = GameObject.Find("ItemShip");

                musicPresent = false;
                if (go != null)
                {
                    // currently crying case
                    if (currentBehaviourStateIndex == (int)State.Crying)
                    {
                        if (Vector3.Distance(go.transform.position, this.transform.position) < 9f)
                        {
                            musicPresent = true;
                            musicDest = go.transform.position;
                            musicDist = Vector3.Distance(go.transform.position, this.transform.position);
                            return;
                        }
                    }
                    else
                    {
                        // seeking music source case
                        if (Vector3.Distance(go.transform.position, this.transform.position) < 90f)
                        {
                            musicPresent = true;
                            musicDest = go.transform.position;
                            musicDist = Vector3.Distance(go.transform.position, this.transform.position);
                            return;
                        }
                    }
                }

                var boxes = FindObjectsOfType<BoomboxItem>();
                for (int i = 0; i < boxes.Length; i++)
                {
                    var box = boxes[i];

                    // currently crying case
                    if (currentBehaviourStateIndex == (int)State.Crying)
                    {
                        if (Vector3.Distance(box.transform.position, this.transform.position) < 9f && box.isPlayingMusic)
                        {
                            musicPresent = true;
                            musicDest = box.transform.position;
                            musicDist = Vector3.Distance(box.transform.position, this.transform.position);
                            return;
                        }
                    }
                    else
                    {  // seeking music source case
                        if (Vector3.Distance(box.transform.position, this.transform.position) < 34f && box.isPlayingMusic)
                        {
                            musicPresent = true;
                            musicDest = box.transform.position;
                            musicDist = Vector3.Distance(box.transform.position, this.transform.position);
                            return;
                        }
                    }
                }
            }
            catch (Exception e) { Debug.Log(e); }
        }

        int musicDelay = 0;
        public void baseHeadingToMusic()
        {
            targetPlayer = null;

            if (agent.velocity.magnitude > (agent.speed / 4))
            {
                if (RoundManager.Instance.IsServer) { DoAnimationClientRpc(1); }
                setAnimationSpeedClientRpc(agent.velocity.magnitude / walkAnimationCoefficient);
            }
            else if (agent.velocity.magnitude <= (agent.speed / 8))
            {
                if (RoundManager.Instance.IsServer) { DoAnimationClientRpc(0); }
                setAnimationSpeedClientRpc(1);
            }

            SetDestinationToPosition(musicDest);
            if (this.agent.pathStatus == NavMeshPathStatus.PathPartial)
            {
                musicDelay = 300;
                StartSearch(transform.position);
                SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
            }

            if (Vector3.Distance(transform.position, musicDest) < (7.0 + gameObject.transform.localScale.x))
            {
                musicDelay = 300;
                cryTime = (float)(enemyRandom.NextDouble() * 60f + 15f);
                SwitchToBehaviourClientRpc((int)State.Crying);
            }

            if (provokePoints > 0)
            {
                StartSearch(transform.position);
                SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
            }

            if (!musicPresent)
            {
                StartSearch(transform.position);
                SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
            }
        }

        public override void Start()
        {
            base.Start();
            EntityWarp.mapEntrances = UnityEngine.Object.FindObjectsOfType<EntranceTeleport>(false);
            mostRecentPlayer = getNearestPlayer();
            animator = this.gameObject.GetComponent<Animator>();

            if (RoundManager.Instance.IsHost)
            {
                this.DoAnimationClientRpc(0);
            }

            stamina += 60;

            // adjust volume according to config bind
            creatureVoice.volume = Plugin.moaiGlobalMusicVol.Value;
            creatureSFX.volume = Plugin.moaiGlobalMusicVol.Value;
            creatureRoar.volume = Plugin.moaiGlobalMusicVol.Value;
            creatureDeath.volume = Plugin.moaiGlobalMusicVol.Value;
            creatureExtract.volume = Plugin.moaiGlobalMusicVol.Value;
            creatureFlee.volume = Plugin.moaiGlobalMusicVol.Value;
            creatureHit1.volume = Plugin.moaiGlobalMusicVol.Value;
            creatureHit2.volume = Plugin.moaiGlobalMusicVol.Value;
            creatureStare.volume = Plugin.moaiGlobalMusicVol.Value;
            creatureCrying.volume = Plugin.moaiGlobalMusicVol.Value;

            timeSinceHittingLocalPlayer = 0;
            timeSinceNewRandPos = 0;
            positionRandomness = new Vector3(0, 0, 0);
            enemyRandom = new System.Random(StartOfRound.Instance.randomMapSeed + thisEnemyIndex);
            isDeadAnimationDone = false;

            currentBehaviourStateIndex = (int)State.SearchingForPlayer;
            // We make the enemy start searching. This will make it start wandering around.
            StartSearch(transform.position);
            moaiSoundPlayClientRpc("creatureVoice");

            this.enemyHP = 6;

            float newSize = Plugin.moaiGlobalSize.Value;
            // random size alter
            if (enemyRandom.NextDouble() <= Plugin.moaiGlobalSizeVar.Value)
            {
                if (UnityEngine.Random.Range(0.0f, 1.0f) < 0.5f)
                { // small
                    newSize = (1 - UnityEngine.Random.Range(0.0f, 0.95f)) * newSize;
                }
                else
                { // large
                    newSize = (1 + UnityEngine.Random.Range(0.0f, 2.0f)) * newSize;
                }
            }
            modifySizeClientRpc(newSize);
        }

        [ClientRpc]
        public void modifySizeClientRpc(float size)
        {
            float pitchAlter = (float)Math.Pow(size, 0.3);
            gameObject.transform.localScale *= (size * Plugin.moaiGlobalSize.Value);
            gameObject.GetComponent<NavMeshAgent>().height *= size;

            creatureVoice.pitch /= pitchAlter;
            creatureSFX.pitch /= pitchAlter;
            creatureRoar.pitch /= pitchAlter;
            creatureHit1.pitch /= pitchAlter;
            creatureHit2.pitch /= pitchAlter;
            creatureStare.pitch /= pitchAlter;
            creatureExtract.pitch /= pitchAlter;
            creatureLeap.pitch /= pitchAlter;
            creatureDeath.pitch /= pitchAlter;
            creatureFlee.pitch /= pitchAlter;
            creatureCrying.pitch /= pitchAlter;
            timeControl /= pitchAlter;
        }

        public void LateUpdate()
        {
            // post processing weight step
            var localPlayer = RoundManager.Instance.playersManager.localPlayerController;
            float distance = Vector3.Distance(transform.position, localPlayer.transform.position);

            if (distance > 40f)
            {
                postProcessVolume.weight = 0;
            }
            else
            {
                // Apply the calculated weight to the post-processing volume
                postProcessVolume.weight /= 1 + 0.2f * distance * processScalar;
            }

            // detach players attached to mouth
            if (attachedPlayer && Math.Abs(Time.time - attachTime) > 1.2f && RoundManager.Instance.IsHost)
            {
                letGoOfPlayerClientRpc(attachedPlayer.NetworkObject.NetworkObjectId);
            }

            // attachment coordinate change
            if (attachedPlayer && !thrashingCorpse)
            {
                attachedPlayer.transform.position = mouth.position;

                if (enemyRandom.Next() < 0.25f)
                {
                    attachedPlayer.DropBlood();
                    attachedPlayer.AddBloodToBody();
                    attachedPlayer.bloodParticle.Play();
                    attachedPlayer.deadBody.bloodSplashParticle.Play();
                }
            }
            if (attachedPlayer && thrashingCorpse)
            {
                attachedPlayer.deadBody.transform.position = mouth.position;
                if (enemyRandom.Next() < 0.25f)
                {
                    attachedPlayer.deadBody.bloodSplashParticle.Play();
                    attachedPlayer.deadBody.MakeCorpseBloody();
                }
            }
        }

        public override void Update()
        {
            base.Update();

            // death check for traps
            if (!this.isEnemyDead && enemyHP <= 0 && !markDead)
            {
                this.animator.speed = 1;
                base.KillEnemyOnOwnerClient(false);
                this.stopAllSound();
                if (!animator.GetCurrentAnimatorStateInfo(0).IsName("Death") && !animator.GetCurrentAnimatorStateInfo(0).IsName("Exit"))
                {
                    animator.Play("Death");
                }
                isEnemyDead = true;
                enemyHP = 0;
                moaiSoundPlayClientRpc("creatureDeath");
                deadEventClientRpc();
                markDead = true;
            }

            if (isEnemyDead)
            {
                if (!isDeadAnimationDone)
                {
                    this.animator.speed = 1;
                    isDeadAnimationDone = true;
                    stopAllSound();
                    creatureVoice.PlayOneShot(dieSFX);
                }
                return;
            }

            if (targetPlayer != null && targetPlayer.isPlayerDead) { targetPlayer = null; }
            movingTowardsTargetPlayer = targetPlayer != null;

            timeSinceHittingLocalPlayer += Time.deltaTime;
            timeSinceNewRandPos += Time.deltaTime;
            if (targetPlayer != null && PlayerIsTargetable(targetPlayer))
            {
                turnCompass.LookAt(targetPlayer.gameplayCamera.transform.position);
            }

            // comes with stun mechanics!
            // If stunned, will momentarily pause and then
            // flee in a gaseous form
            if (stunNormalizedTimer > 0f && RoundManager.Instance.IsHost)
            {
                stunNormalizedTimer = 0;
                moaiSoundPlayClientRpc("creatureFlee");
                agent.speed = 0f;
                timeSinceFleeStart = Time.time;
                shadowEnable = false;
                awaitingShadowMode = true;
                fleeDestination = Vector3.zero;
                StopSearch(currentSearch);
                setAnimationSpeedClientRpc(1 * timeControl);
                animPlayClientRpc("StandingStun");
                SwitchToBehaviourClientRpc((int)State.Fleeing);
            }

            if (awaitingShadowMode && !shadowEnable && RoundManager.Instance.IsHost)
            {
                agent.speed = 0;
                setAnimationSpeedClientRpc(1 * timeControl);
                if (creatureFlee.time > 2.41f)  // related to the sound duration
                {
                    SetVisibleClientRpc(false);
                    shadowEnable = true;
                }
            }
            else if (currentBehaviourStateIndex != (int)State.Fleeing)
            {
                shadowEnable = false;
                awaitingShadowMode = false;
            }


            // animation speeds
            if (currentBehaviourStateIndex == (int)State.HeadingToEntrance)

                // scale management
                if (this.isOutside && !isOutSwitch)
                {
                    this.transform.localScale = new Vector3(1f, 1f, 1f);
                    isOutSwitch = true;
                }
            if (!this.isOutside && isOutSwitch)
            {
                this.transform.localScale = new Vector3(0.4f, 0.4f, 0.4f);
                isOutSwitch = false;
            }

            // collider management
            if (currentBehaviourStateIndex == (int)State.Leaping)
            {
                if (foursCollider.gameObject.activeInHierarchy) { foursCollider.gameObject.SetActive(false); }
                if (uprightCollider.gameObject.activeInHierarchy) { uprightCollider.gameObject.SetActive(false); }
            }
            else if (currentBehaviourStateIndex == (int)State.Shredding)
            {
                if (foursCollider.gameObject.activeInHierarchy) { foursCollider.gameObject.SetActive(false); }
                if (uprightCollider.gameObject.activeInHierarchy) { uprightCollider.gameObject.SetActive(false); }
            }
            else if (currentBehaviourStateIndex == (int)State.Attacking)
            {
                if (!foursCollider.gameObject.activeInHierarchy) { foursCollider.gameObject.SetActive(true); }
                if (uprightCollider.gameObject.activeInHierarchy) { uprightCollider.gameObject.SetActive(false); }
            }
            else
            {
                if (foursCollider.gameObject.activeInHierarchy) { foursCollider.gameObject.SetActive(false); }
                if (!uprightCollider.gameObject.activeInHierarchy) { uprightCollider.gameObject.SetActive(true); }
            }
        }

        public override void DoAIInterval()
        {
            base.DoAIInterval();
            if (provokePoints > 0)
            {
                provokePoints--;
            }

            if (entranceDelay > 0) { entranceDelay--; }
            if (musicDelay > 0) { musicDelay--; }

            // source update cycle
            if (sourcecycle > 0)
            {
                sourcecycle--;
            }
            else
            {
                sourcecycle = 75;
                unreachableEnemies.Clear();
            }

            if (stamina <= 0)
            {
                recovering = true;
            }
            else if (stamina > 60)
            {
                recovering = false;
            }

            // executes once every second
            if (sourcecycle % 5 == 0)
            {
                var ePack = EntityWarp.findNearestEntrance(this);
                listenForMusic();
                nearestEntrance = ePack.tele;
                nearestEntranceNavPosition = ePack.navPosition;

                if (stamina < 120)
                {
                    stamina += 8;  // a moai regenerates all of its stamina in 10-ish seconds
                }
                mostRecentPlayer = getNearestPlayer();
            }

            if (targetPlayer != null)
            {
                mostRecentPlayer = targetPlayer;
            }

            // force visibility if in bad state
            if (!isVisible && currentBehaviourStateIndex != (int)State.Fleeing)
            {
                SetVisibleClientRpc(true);
            }

            AIInterval();
        }

        public void AIInterval()
        {
            switch (currentBehaviourStateIndex)
            {
                case (int)State.SearchingForPlayer:
                    baseSearchingForPlayer();
                    break;
                case (int)State.HeadingToEntrance:
                    baseHeadingToEntrance();
                    break;
                case (int)State.Staring:
                    baseStaring();
                    break;
                case (int)State.Attacking:
                    baseAttacking(32f);
                    break;
                case (int)State.Shredding:
                    baseShredding();
                    break;
                case (int)State.Fleeing:
                    baseFleeing();
                    break;
                case (int)State.HeadingToMusic:
                    baseHeadingToMusic();
                    break;
                case (int)State.Crying:
                    baseCrying();
                    break;
                default:
                    LogDebug("This Behavior State doesn't exist!");
                    break;
            }
        }

        public void baseFleeing()
        {
            if (shadowEnable == false)
            {
                agent.speed = 0;
            }
            else
            {
                // RUN
                agent.speed = 15 * Plugin.moaiGlobalSpeed.Value;  // running speed was 20f
                if (fleeDestination == Vector3.zero)
                {
                    fleeDestination = pickFleeDestination();
                }
                else
                {
                    targetPlayer = null;
                    SetDestinationToPosition(fleeDestination);
                    if (Vector3.Distance(fleeDestination, transform.position) < (2.0 + gameObject.transform.localScale.x) || (Time.time - timeSinceFleeStart > 9f))
                    {
                        stamina = 120;
                        StartSearch(transform.position);
                        shadowParticles.Stop();
                        shadowEnable = false;
                        awaitingShadowMode = false;
                        targetPlayer = null;
                        SetVisibleClientRpc(true);
                        animPlayClientRpc("Walk");
                        DoAnimationClientRpc(1);
                        SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                    }
                }
            }
        }

        [ClientRpc]
        void SetVisibleClientRpc(bool visible)
        {
            foreach (var renderer in meshRenderRoot.GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = visible;
            }
            isVisible = visible;
            if (!visible)
            {
                shadowParticles.Play();
            }
            else
            {
                shadowParticles.Stop();
            }
        }


        public Vector3 pickFleeDestination()
        {
            var allAINodes = GameObject.FindGameObjectsWithTag("OutsideAINode");
            if (!isOutside)
            {
                allAINodes = GameObject.FindGameObjectsWithTag("AINode");
            }

            List<GameObject> bestAINodes = new List<GameObject>();
            foreach (GameObject Node in allAINodes)
            {
                if (Vector3.Distance(Node.transform.position, transform.position) > 60)
                {
                    bestAINodes.Add(Node);
                    /**
                    bool validNode = true;
                    var scripts = RoundManager.Instance.playersManager.allPlayerScripts;

                    // node should be far enough away from players
                    foreach(PlayerControllerB player in scripts)
                    {
                        if(Vector3.Distance(Node.transform.position, player.gameObject.transform.position) > 30)
                        {
                            bestAINodes.Add(Node);
                        }
                    }
                    **/
                }
            }

            if (bestAINodes.Count > 0)
            {
                return bestAINodes[enemyRandom.Next(0, bestAINodes.Count)].transform.position;
            }
            else
            {
                return allAINodes[enemyRandom.Next(0, allAINodes.Length)].transform.position;
            }
        }

        public void baseShredding()
        {
            if (!creatureHit1.isPlaying && !creatureHit2.isPlaying)
            {
                if (hitRotation == 1)
                {
                    moaiSoundPlayClientRpc("creatureHit1");
                    hitRotation = 2;
                }
                else
                {
                    moaiSoundPlayClientRpc("creatureHit2");
                    hitRotation = 1;
                }
            }
            if (!animator.GetCurrentAnimatorStateInfo(0).IsName("Thrash") && !proceedShred)
            {
                animPlayClientRpc("Thrash");
                DoAnimationClientRpc(6);
                proceedShred = true;
            }

            setAnimationSpeedClientRpc(1);

            if (animator.GetCurrentAnimatorStateInfo(0).normalizedTime >= 0.8 && animator.GetCurrentAnimatorStateInfo(0).IsName("Thrash"))
            {
                attackState = 2;  // continue running! 
                DoAnimationClientRpc(5);
                animPlayClientRpc("Run");
                SwitchToBehaviourClientRpc((int)State.Attacking);
            }
        }

        public void baseStaring()
        {
            agent.speed = 0;

            setAnimationSpeedClientRpc(1);

            if (animator.GetInteger("state") != 2)
            {
                starePoints = 0;
                DoAnimationClientRpc(2);
                animPlayClientRpc("Stare");

                if (!creatureStare.isPlaying)
                {
                    moaiSoundPlayClientRpc("creatureStare");
                }
            }

            if (FoundClosestPlayerInRange((stamina), true))
            {
                starePoints += angerRate / Vector3.Distance(transform.position, targetPlayer.transform.position);
                facePosition(targetPlayer.transform.position);
            }
            else
            {
                starePoints -= 2;
            }

            // check for a finished animation
            if (animator.GetCurrentAnimatorStateInfo(0).IsName("Stare") && animator.GetCurrentAnimatorStateInfo(0).normalizedTime >= 1)
            {
                // have a chance to attack based on points acquired
                if (starePoints > enemyRandom.NextDouble() * 100)
                {
                    // ATTACK
                    attackState = 0;
                    StopSearch(currentSearch);
                    proccedRoar = false;
                    proccedCrouch = false;
                    proccedRun = false;
                    SwitchToBehaviourClientRpc((int)State.Attacking);
                }
                else
                {
                    // back to normal
                    stamina -= 80f;
                    StartSearch(transform.position);
                    SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                }
            }
        }

        public AudioSource creatureCrying;
        public float cryTime = 0f;
        public void baseCrying()
        {
            agent.speed = 0f * Plugin.moaiGlobalSpeed.Value;
            animator.speed = 1f;
            DoAnimationClientRpc(7);

            if (!animator.GetCurrentAnimatorStateInfo(0).IsName("Cry"))
            {
                if (!animator.GetCurrentAnimatorStateInfo(0).IsName("Walk") && !animator.GetCurrentAnimatorStateInfo(0).IsName("Stand"))
                {
                    animPlayClientRpc("Cry");
                }
            }

            if (!creatureCrying.isPlaying) { moaiSoundPlayClientRpc("creatureCrying"); }
            cryTime -= 0.2f;

            if (cryTime <= 0)
            {
                StartSearch(transform.position);
                SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
            }

            if (provokePoints > 0)
            {
                StartSearch(transform.position);
                SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
            }

            if (!musicPresent)
            {
                var ply = getNearestPlayer();
                if (Vector3.Distance(ply.transform.position, musicDest) > 6f)
                {
                    stunNormalizedTimer = 0;
                    moaiSoundPlayClientRpc("creatureFlee");
                    agent.speed = 0f;
                    timeSinceFleeStart = Time.time;
                    shadowEnable = false;
                    awaitingShadowMode = true;
                    fleeDestination = Vector3.zero;
                    StopSearch(currentSearch);
                    setAnimationSpeedClientRpc(1 * timeControl);
                    animPlayClientRpc("StandingStun");
                    SwitchToBehaviourClientRpc((int)State.Fleeing);
                }
                else
                {
                    provokePoints += 100;
                    targetPlayer = ply;

                    stamina = 100;
                    recovering = false;
                    SwitchToBehaviourClientRpc((int)State.Attacking);
                    if (base.IsOwner)
                    {
                        if (this.enemyHP <= 0 && !markDead)
                        {
                            base.KillEnemyOnOwnerClient(false);
                            this.stopAllSound();
                            isEnemyDead = true;
                            moaiSoundPlayClientRpc("creatureDeath");
                            deadEventClientRpc();
                            markDead = true;
                            return;
                        }

                        moaiSoundPlayClientRpc("creatureHit");
                    }
                }
            }
        }

        public void baseSearchingForPlayer(float lineOfSightRange = 28f)
        {
            agent.speed = 6f * Plugin.moaiGlobalSpeed.Value;
            agent.angularSpeed = 120;

            if (agent.velocity.magnitude > (agent.speed / 4))
            {
                if (RoundManager.Instance.IsServer) { DoAnimationClientRpc(1); }
                setAnimationSpeedClientRpc(agent.velocity.magnitude / walkAnimationCoefficient);
            }
            else if (agent.velocity.magnitude <= (agent.speed / 8))
            {
                if (RoundManager.Instance.IsServer) { DoAnimationClientRpc(0); }
                setAnimationSpeedClientRpc(1);
            }

            // sound switch
            if (!creatureVoice.isPlaying)
            {
                moaiSoundPlayClientRpc("creatureVoice");
            }

            // entrance state switch
            updateEntranceChance();
            if (this.enemyRandom.NextDouble() < chanceToLocateEntrance && gameObject.transform.localScale.x <= 2.2f)
            {
                Debug.Log("Soul Devourer: entrance state switch");
                StopSearch(currentSearch);
                SwitchToBehaviourClientRpc((int)State.HeadingToEntrance);
            }

            if ((FoundClosestPlayerInRange(lineOfSightRange, true) && stamina >= 120) || provokePoints > 0)
            {
                StopSearch(currentSearch);
                SwitchToBehaviourClientRpc((int)State.Staring);
                return;
            }
            if (stamina < 100)
            {
                targetPlayer = null;
            }

            // music state switch
            if (musicPresent && musicDelay <= 0)
            {
                SwitchToBehaviourClientRpc((int)State.HeadingToMusic);
            }
        }

        public void baseHeadingToEntrance()
        {
            targetPlayer = null;

            if (agent.velocity.magnitude > (agent.speed / 4))
            {
                if (RoundManager.Instance.IsServer) { DoAnimationClientRpc(1); }
                setAnimationSpeedClientRpc(agent.velocity.magnitude / walkAnimationCoefficient);
            }
            else if (agent.velocity.magnitude <= (agent.speed / 8))
            {
                if (RoundManager.Instance.IsServer) { DoAnimationClientRpc(0); }
                setAnimationSpeedClientRpc(1);
            }

            SetDestinationToPosition(nearestEntranceNavPosition);
            if (this.isOutside != nearestEntrance.isEntranceToBuilding || this.agent.pathStatus == NavMeshPathStatus.PathPartial)
            {
                entranceDelay = 150;
                StartSearch(transform.position);
                SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
            }
            if (Vector3.Distance(transform.position, nearestEntranceNavPosition) < (2.0 + gameObject.transform.localScale.x))
            {
                if (nearestEntrance.isEntranceToBuilding)
                {
                    Debug.Log("SoulDev: Warp in");
                    EntityWarp.SendEnemyInside(this);
                    nearestEntrance.PlayAudioAtTeleportPositions();
                }
                else
                {
                    Debug.Log("SoulDev: Warp out");
                    EntityWarp.SendEnemyOutside(this, true);
                    nearestEntrance.PlayAudioAtTeleportPositions();
                }
                entranceDelay = 150;
                StartSearch(transform.position);
                SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);

            }

            if (provokePoints > 0)
            {
                StartSearch(transform.position);
                SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
            }
        }
        public void baseLeaping()
        {
            SetDestinationToPosition(leapPos);

            if (!creatureLeap.isPlaying && !proceedLeap)
            {
                moaiSoundPlayClientRpc("creatureLeap");
            }

            setAnimationSpeedClientRpc(1);

            if (Time.time - leaptime >= 1f)
            {
                attackState = 2;  // continue running! 
                proccedRun = false;
                if (stamina < 10)
                {
                    stamina = 10;
                }
                DoAnimationClientRpc(5);
                animPlayClientRpc("Run");
                attackState = 2;
                proccedRun = false;
                agent.acceleration = 14.5f * Plugin.moaiGlobalSpeed.Value;
                agent.speed = 14f * Plugin.moaiGlobalSpeed.Value;
                agent.angularSpeed = 145;
                SwitchToBehaviourClientRpc((int)State.Attacking);
            }
        }

        public Vector3 getLeapPos()
        {
            var p = targetPlayer.transform.position;

            for (int i = 0; i < 10; i++)
            {
                var ry = enemyRandom.Next(-10, 10);
                var rx = enemyRandom.Next(-35, 35);
                var rz = enemyRandom.Next(-35, 35);
                Vector3 randPos = new Vector3(p.x + rx, p.y + ry, p.z + rz);

                NavMeshHit hit;
                bool result = NavMesh.SamplePosition(randPos, out hit, 60f, NavMesh.AllAreas);

                var playerDist = Vector3.Distance(p, hit.position);

                if (result && playerDist > 13)
                {
                    return hit.position;
                }
            }
            return targetPlayer.transform.position;
        }

        public void baseAttacking(float range = 22f, float noLOSDivisor = 1.8f)
        {
            if (attackState == 0)
            {
                if (!creatureRoar.isPlaying)
                {
                    moaiSoundPlayClientRpc("creatureRoar");
                }
                if (!animator.GetCurrentAnimatorStateInfo(0).IsName("Roar") && !proccedRoar)
                {
                    animPlayClientRpc("Roar");
                    DoAnimationClientRpc(3);
                    proccedRoar = true;
                }

                setAnimationSpeedClientRpc(1);

                if (animator.GetCurrentAnimatorStateInfo(0).normalizedTime >= 0.8 && animator.GetCurrentAnimatorStateInfo(0).IsName("Roar"))
                {
                    attackState = 1;  // start crouch 
                }
            }

            if ((attackState == 1))
            {
                DoAnimationClientRpc(4);
                setAnimationSpeedClientRpc(2.5f);

                if (!animator.GetCurrentAnimatorStateInfo(0).IsName("Prepare") && !proccedCrouch)
                {
                    animPlayClientRpc("Prepare");
                    DoAnimationClientRpc(3);
                    proccedCrouch = true;
                }

                if (animator.GetCurrentAnimatorStateInfo(0).normalizedTime >= 0.8 && animator.GetCurrentAnimatorStateInfo(0).IsName("Prepare"))
                {
                    attackState = 2;  // start running on all fours
                }
            }

            if (attackState == 2)
            {
                // sound switch 
                if (!creatureSFX.isPlaying) { moaiSoundPlayClientRpc("creatureSFX"); }
                agent.speed = 20f * Plugin.moaiGlobalSpeed.Value;
                agent.acceleration = 14.5f * Plugin.moaiGlobalSpeed.Value;
                agent.angularSpeed = 140;

                setAnimationSpeedClientRpc(agent.velocity.magnitude / runAnimationCoefficient);
                DoAnimationClientRpc(5);

                if (!animator.GetCurrentAnimatorStateInfo(0).IsName("Run") && !proccedRun)
                {
                    animPlayClientRpc("Run");
                    DoAnimationClientRpc(5);
                    proccedRun = true;
                }

                /**
                // chance to do a leap. chance depends on how slow the devourer is moving and
                // how close he is to a player (unless very close)
                if (targetPlayer)
                {
                    float dist = Vector3.Distance(transform.position, targetPlayer.transform.position);
                    float speed = agent.velocity.magnitude;
                    float chance = (baseLeapChance / ((1+speed) * (1+dist)));

                    if (dist > 2 && dist < 12 && speed < 4)
                    {
                        if (enemyRandom.NextDouble() < chance)
                        {
                            proceedLeap = false;
                            agent.speed = 40f * moaiGlobalSpeed.Value;
                            agent.acceleration = 2000f * moaiGlobalSpeed.Value;
                            agent.angularSpeed = 1000;
                            animator.Play("Leap");
                            animPlayClientRpc("Leap");
                            leaptime = Time.time;
                            leapPos = getLeapPos();
                            if (foursCollider.gameObject.activeInHierarchy) { foursCollider.gameObject.SetActive(false); }
                            if (uprightCollider.gameObject.activeInHierarchy) { uprightCollider.gameObject.SetActive(false); }
                            SwitchToBehaviourClientRpc((int)State.Leaping);
                        }
                    }
                }
                **/
            }
            else
            {
                agent.speed = 0 * Plugin.moaiGlobalSpeed.Value;
            }

            updateEntranceChance();

            this.stamina -= 2.8f;  // all stamina (150) is lost in 15 seconds?

            // Keep targetting closest player, unless they are over 20 units away and we can't see them.
            if (!FoundClosestPlayerInRange((stamina) / noLOSDivisor, false) && !FoundClosestPlayerInRange((stamina), true))
            {
                targetPlayer = null;
                starePoints = 0;
                StartSearch(transform.position);
                SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                agent.acceleration = 14.5f * Plugin.moaiGlobalSpeed.Value;
                return;
            }
            StickingInFrontOfPlayer();
        }

        public void updateEntranceChance()
        {
            if (!nearestEntrance)
            {
                return;
            }
            var dist = Vector3.Distance(transform.position, nearestEntrance.transform.position);

            chanceToLocateEntrancePlayerBonus = 1;
            if (mostRecentPlayer)
            {
                if (mostRecentPlayer == this.isOutside)
                {
                    chanceToLocateEntrancePlayerBonus = 1;
                }
                else
                {
                    chanceToLocateEntrancePlayerBonus = 1.5f;
                }
            }
            var m1 = 1;

            if (dist < 20) { m1 = 4; };
            if (dist < 15) { m1 = 6; };
            if (dist < 10) { m1 = 7; };
            if (dist < 5) { m1 = 10; }

            if (nearestEntrance)
            {
                chanceToLocateEntrance = (float)(1 / Math.Pow(dist, 2)) * m1 * chanceToLocateEntrancePlayerBonus - entranceDelay;
            }

        }

        public bool FoundClosestPlayerInRange(float r, bool needLineOfSight)
        {
            if (recovering) { return false; }
            moaiTargetClosestPlayer(range: r, requireLineOfSight: needLineOfSight);
            if (targetPlayer == null) return false;
            return targetPlayer != null;
        }

        public bool moaiTargetClosestPlayer(float range, bool requireLineOfSight)
        {
            if (recovering) { return false; }
            mostOptimalDistance = range;
            PlayerControllerB playerControllerB = targetPlayer;
            targetPlayer = null;
            for (int i = 0; i < StartOfRound.Instance.connectedPlayersAmount + 1; i++)
            {
                if (PlayerIsTargetable(StartOfRound.Instance.allPlayerScripts[i]) && (!requireLineOfSight || CheckLineOfSightForPosition(StartOfRound.Instance.allPlayerScripts[i].gameplayCamera.transform.position, 100, 80)))
                {
                    tempDist = Vector3.Distance(base.transform.position, StartOfRound.Instance.allPlayerScripts[i].transform.position);
                    if (tempDist < mostOptimalDistance)
                    {
                        mostOptimalDistance = tempDist;
                        targetPlayer = StartOfRound.Instance.allPlayerScripts[i];
                    }
                }
            }

            if (targetPlayer != null && playerControllerB != null)
            {
                targetPlayer = playerControllerB;
            }

            return targetPlayer != null;
        }



        public override void HitEnemy(int force = 1, PlayerControllerB playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
        {
            base.HitEnemy(force, playerWhoHit, playHitSFX);
            if (this.isEnemyDead)
            {
                return;
            }

            if (hitID == -1 && playerWhoHit == null)
            {
                return;
            }

            this.enemyHP -= force;

            if (playerWhoHit != null)
            {
                provokePoints += 20 * force;
                targetPlayer = playerWhoHit;
            }
            stamina = 60;
            recovering = false;
            SwitchToBehaviourClientRpc((int)State.Attacking);
            if (base.IsOwner)
            {
                if (this.enemyHP <= 0 && !markDead)
                {
                    base.KillEnemyOnOwnerClient(false);
                    this.stopAllSound();
                    isEnemyDead = true;
                    moaiSoundPlayClientRpc("creatureDeath");
                    deadEventClientRpc();
                    markDead = true;
                    return;
                }

                moaiSoundPlayClientRpc("creatureHit");
            }
        }

        [ClientRpc]
        public void deadEventClientRpc()
        {
            animator.Play("Death");
            isEnemyDead = true;
        }

        [ServerRpc(RequireOwnership = false)]
        public void letGoOfPlayerServerRpc(ulong playerId)
        {
            letGoOfPlayerClientRpc(playerId);
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
            targetPlayer.playerCollider.enabled = true;

            // the player needs to be on a navmesh spot (avoiding all collision bugs)
            NavMeshHit hit;
            var valid = UnityEngine.AI.NavMesh.SamplePosition(targetPlayer.transform.position, out hit, 15f, NavMesh.AllAreas);
            if (valid)
            {
                targetPlayer.transform.position = hit.position;
            }

            attachedPlayer = null;
        }

        void StickingInFrontOfPlayer()
        {
            if (targetPlayer == null || !IsOwner)
            {
                return;
            }

            // Charge into player
            StalkPos = targetPlayer.transform.position;
            SetDestinationToPosition(StalkPos, checkForPath: false);
        }

        public override void OnCollideWithEnemy(Collider other, EnemyAI collidedEnemy = null)
        {
            if (this.timeSinceHittingLocalPlayer < 0.5f || collidedEnemy.isEnemyDead || isEnemyDead)
            {
                return;
            }

            if (collidedEnemy.enemyType == this.enemyType)
            {
                return;
            }
            var nam = collidedEnemy.enemyType.name.ToLower();
            if (nam.Contains("mouth") && nam.Contains("dog"))
            {
                return;  // this doesn't work. Annoying
            }

            if (collidedEnemy.enemyType.enemyName.ToLower().Contains("soul"))
            {
                return;
            }
            this.timeSinceHittingLocalPlayer = 0f;
            collidedEnemy.HitEnemy(1, null, true);
        }


        public override void OnCollideWithPlayer(Collider other)
        {
            if (timeSinceHittingLocalPlayer < 1.7f)
            {
                return;
            }
            PlayerControllerB playerControllerB = MeetsStandardPlayerCollisionConditions(other);

            // thrash cooldown gate
            if (lastThrashTime > Time.time + 0.75F) { return; }

            if (playerControllerB != null)
            {
                // Line-of-sight check to prevent biting through walls
                Vector3 origin = transform.position;
                Vector3 target = playerControllerB.transform.position + Vector3.up * 0.5f; // aim center-mass

                Debug.Log("LOSCHECK");
                if (Physics.Linecast(origin, target, out RaycastHit hit, LayerMask.GetMask("Default", "Room", "Terrain")))
                {
                    if (!hit.collider.transform.IsChildOf(playerControllerB.transform))
                        return; // something blocks view
                }

                timeSinceHittingLocalPlayer = 0f;
                if (playerControllerB.health <= 45)
                {
                    playerControllerB.KillPlayer(playerControllerB.velocityLastFrame, true, CauseOfDeath.Mauling, 0);
                    if (RoundManager.Instance.IsHost)
                    {
                        attachPlayerClientRpc(playerControllerB.NetworkObject.NetworkObjectId, true, 20);
                        lastThrashTime = Time.time;
                    }
                    else
                    {
                        attachPlayerServerRpc(playerControllerB.NetworkObject.NetworkObjectId, true, 20);
                        lastThrashTime = Time.time;
                    }
                }
                else
                {
                    playerControllerB.DamagePlayer(45);
                    if (RoundManager.Instance.IsHost)
                    {
                        attachPlayerClientRpc(playerControllerB.NetworkObject.NetworkObjectId, false, 10);
                        lastThrashTime = Time.time;
                    }
                    else
                    {
                        attachPlayerServerRpc(playerControllerB.NetworkObject.NetworkObjectId, false, 10);
                        lastThrashTime = Time.time;
                    }
                }
            }
        }



        [ServerRpc(RequireOwnership = false)]
        public void attachPlayerServerRpc(ulong uid, bool lastHit, int staminaGrant)
        {
            attachPlayerClientRpc(uid, lastHit, staminaGrant);
        }

        [ClientRpc]
        public void attachPlayerClientRpc(ulong uid, bool lastHit, int staminaGrant)
        {
            stamina += staminaGrant;
            proceedShred = false;
            attachTime = Time.time;

            if (RoundManager.Instance.IsHost) { SwitchToBehaviourClientRpc((int)State.Shredding); }

            RoundManager m = RoundManager.Instance;
            PlayerControllerB[] players = m.playersManager.allPlayerScripts;
            for (int i = 0; i < players.Length; i++)
            {
                PlayerControllerB player = players[i];
                if (player.NetworkObject.NetworkObjectId == uid)
                {
                    shreddedPlayer = player;
                    if (!lastHit)
                    {
                        player.transform.position = mouth.position;
                        player.playerCollider.enabled = false;
                        attachedPlayer = player;
                        thrashingCorpse = false;
                    }
                    else
                    {
                        player.deadBody.transform.position = mouth.position;
                        attachedPlayer = player;
                        thrashingCorpse = true;
                    }
                    return;
                }
            }
        }

        // method to play a sound with a target string id
        // can be overridden in moai variants (thus it is usable in MoaiNormalNet)
        public virtual void playSoundId(String id) { }

        public void stopAllSound()
        {
            // normal creature sounds
            creatureSFX.Stop();
            creatureVoice.Stop();
            creatureRoar.Stop();
            creatureStare.Stop();
            creatureFlee.Stop();
        }

        [ClientRpc]
        public void moaiSoundPlayClientRpc(String soundName)
        {
            switch (soundName)
            {
                case "creatureSFX":
                    stopAllSound();
                    creatureSFX.Play();
                    break;
                case "creatureVoice":
                    stopAllSound();

                    // start time intervals, for variance
                    double[] timeIntervals = [0.0, 0.8244, 11.564, 29.11, 34.491, 37.840, 48.689, 64.518, 89.535, 92.111];
                    int selectedTime = UnityEngine.Random.Range(0, timeIntervals.Length);

                    //Debug.Log("selected time: " + timeIntervals[selectedTime]);
                    creatureVoice.Play();  // time is in seconds
                    creatureVoice.SetScheduledStartTime(timeIntervals[selectedTime]);
                    creatureVoice.time = (float)timeIntervals[selectedTime];
                    break;
                case "creatureRoar":
                    stopAllSound();
                    creatureRoar.Play();
                    break;
                case "creatureHit1":
                    creatureHit1.Play();
                    break;
                case "creatureHit2":
                    creatureHit2.Play();
                    break;
                case "creatureStare":
                    creatureStare.Play();
                    break;
                case "creatureLeap":
                    creatureLeap.Play();
                    break;
                case "creatureDeath":
                    creatureDeath.Play();
                    break;
                case "creatureFlee":
                    stopAllSound();
                    creatureFlee.Play();
                    break;
                case "creatureCrying":
                    stopAllSound();
                    creatureCrying.Play();
                    break;
            }
        }

        [ClientRpc]
        public void setAnimationSpeedClientRpc(float speed)
        {
            this.animator.speed = speed;
        }

        [ClientRpc]
        // note that this is only for rotation animations (cause its moai)
        // these are synced through a network transform
        public void DoAnimationClientRpc(int index)
        {
            if (this.animator) { this.animator.SetInteger("state", index); }
        }

        // directly play an animation
        [ClientRpc]
        public void animPlayClientRpc(String name)
        {
            animator.Play(name);
        }
    }
}

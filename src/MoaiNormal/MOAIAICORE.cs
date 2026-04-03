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
using System.Threading.Tasks;

namespace MoaiEnemy.src.MoaiNormal
{
    // using this to gain more control over the ai system...
    internal abstract class MOAIAICORE : EnemyAI
    {
        protected Animator animator;

        // ThunderMoai vars
        protected float ticksTillThunder = 5; // ticks occur 5 times per second

        // a non negative goodBoy meter means a friendly moai
        // very high values result in very generous acts
        // goodBoy goes down by 1 every AI tick (0.2 seconds).
        // more valuable scrap gives exponentially higher values
        public int goodBoy = -1;
        protected Vector3 guardTarget = Vector3.zero;
        protected float impatience = 0;
        protected float wait = 20;

        // related to entering and exiting entrances
        // updated once every 4-ish seconds
        protected EntranceTeleport nearestEntrance = null;
        public Vector3 nearestEntranceNavPosition = Vector3.zero;
        protected PlayerControllerB mostRecentPlayer = null;
        protected int entranceDelay = 0;  // prevents constant rentry / exit
        protected float chanceToLocateEntrancePlayerBonus = 0;
        protected float chanceToLocateEntrance = 0;

        // updated once every 15 seconds
        protected GrabbableObject[] source;
        public List<GrabbableObject> unreachableItems = new List<GrabbableObject>();
        public List<EnemyAI> unreachableEnemies = new List<EnemyAI>();
        public Vector3 itemNavmeshPosition = Vector3.zero;
        protected static List<DeadBodyInfo> dummyBodies = new List<DeadBodyInfo>();
        protected static List<int> targetedBodies = new List<int>();
        protected static int bdyInc = 0;
        protected int sourcecycle = 75;

        // extra audio sources
        public AudioSource creatureFood;
        public AudioSource creatureEat;
        public AudioSource creatureEatHuman;
        public AudioSource creatureHit;
        public AudioSource creatureDeath;
        public AudioSource creatureBelch;
        public AudioSource slidingBasic;
        public AudioSource slidingWood;
        public AudioSource slidingSnow;
        public AudioSource slidingMetal;
        public AudioSource slidingGravel;
        public AudioSource creatureDig;
        public bool isSliding = false;
        public Transform mouth;
        protected bool eatingScrap = false;
        protected bool eatingHuman = false;
        protected int eatingTimer = -1;

        // stamina mechancis
        protected float stamina = 0; // moai use stamina to chase the player
        protected bool recovering = false; // moai don't chase if they are recovering
        public int provokePoints = 0;

#pragma warning disable 0649
        public Transform turnCompass;
        public Transform attackArea;
#pragma warning restore 0649
        protected float timeSinceHittingLocalPlayer;
        protected float timeSinceNewRandPos;
        protected Vector3 positionRandomness;
        protected Vector3 StalkPos;
        protected System.Random enemyRandom;
        protected bool isDeadAnimationDone;

        public enum State
        {
            SearchingForPlayer,
            Guard,
            StickingInFrontOfEnemy,
            StickingInFrontOfPlayer,
            HeadSwingAttackInProgress,
            HeadingToEntrance,
        }

        public void LogDebug(string text)
        {
#if DEBUG
            Plugin.Logger.LogInfo(text);
#endif
        }

        public void LogProduction(string text)
        {
            Plugin.Logger.LogInfo(text);
        }

        public virtual void setPitches(float pitchAlter)
        {
            // do nothing
        }

        public void destroyAllDummies()
        {
            for (int i = 0; i < dummyBodies.Count; i++)
            {
                destroyDummyClientRpc(getDummyId(dummyBodies[i]));
            }
            dummyBodies.Clear();
        }

        public override void OnDestroy()
        {
            destroyAllDummies();
            base.OnDestroy();
        }


        public void setHalo(bool active)
        {
            var halo = transform.Find("Halo");
            if (halo)
            {
                halo.gameObject.SetActive(active);
            }
            else
            {
                Debug.LogError("MOAI: failed to find Halo!");
            }
        }

        public PlayerControllerB moaiGetNearestPlayer()
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

        public void baseInit()
        {
            source = FindObjectsOfType<GrabbableObject>();
            EntityWarp.mapEntrances = UnityEngine.Object.FindObjectsOfType<EntranceTeleport>(false);
            mostRecentPlayer = moaiGetNearestPlayer();
            animator = this.gameObject.GetComponent<Animator>();
            if (enemyRandom == null) { enemyRandom = new System.Random(); }

            base.Start();
            if (RoundManager.Instance.IsServer)
            {
                this.DoAnimationClientRpc(0);
            }
            else
            {
                // animations are handled strictly through the server
                this.animator.enabled = false;
            }

            if (UnityEngine.Random.Range(0.0f, 1.0f) < Plugin.moaiAngelChance.Value)
            {
                goodBoy = UnityEngine.Random.RandomRangeInt(0, 7000);
                enemyHP += 4;
                moaiSetHaloClientRpc(true);
            }
            else
            {
                moaiSetHaloClientRpc(false);
            }

            // size variant modification
            if (RoundManager.Instance.IsHost && UnityEngine.Random.Range(0.0f, 1.0f) <= moaiGlobalSizeVar.Value)
            {
                float newSize = 1;
                if (UnityEngine.Random.Range(0.0f, 1.0f) < 0.5f)
                { // small
                    newSize = 1 - UnityEngine.Random.Range(0.0f, 0.95f);
                }
                else
                { // large
                    newSize = 1 + UnityEngine.Random.Range(0.0f, 5.0f);
                }

                if (newSize > Plugin.moaiSizeCap.Value)
                {
                    newSize = Plugin.moaiSizeCap.Value;
                }

                if (newSize < 1)
                {
                    var p = (double)newSize;
                    setSizeClientRpc(newSize, (float)Math.Pow(p, 0.3));
                }
                else
                {
                    setSizeClientRpc(newSize, newSize);
                }
            }

            // adjust volume according to config bind
            creatureVoice.volume = moaiGlobalMusicVol.Value;
            creatureSFX.volume = moaiGlobalMusicVol.Value / 1.3f;
            creatureFood.volume = moaiGlobalMusicVol.Value;
            creatureEat.volume = moaiGlobalMusicVol.Value;
            creatureDeath.volume = moaiGlobalMusicVol.Value;
            creatureHit.volume = moaiGlobalMusicVol.Value;
            creatureEatHuman.volume = moaiGlobalMusicVol.Value;
            creatureBelch.volume = moaiGlobalMusicVol.Value;
            slidingBasic.volume = moaiGlobalMusicVol.Value;
            slidingGravel.volume = moaiGlobalMusicVol.Value;
            slidingMetal.volume = moaiGlobalMusicVol.Value;
            slidingSnow.volume = moaiGlobalMusicVol.Value;
            slidingWood.volume = moaiGlobalMusicVol.Value / 1.3f;

            timeSinceHittingLocalPlayer = 0;
            //creatureAnimator.SetTrigger("startWalk");
            timeSinceNewRandPos = 0;
            positionRandomness = new Vector3(0, 0, 0);
            enemyRandom = new System.Random(StartOfRound.Instance.randomMapSeed + thisEnemyIndex);
            isDeadAnimationDone = false;

            // NOTE: Add your behavior states in your enemy script in Unity, where you can configure fun stuff
            // like a voice clip or an sfx clip to play when changing to that specific behavior state.
            currentBehaviourStateIndex = (int)State.SearchingForPlayer;
            // We make the enemy start searching. This will make it start wandering around.
            StartSearch(transform.position);

            moaiSoundPlayClientRpc("creatureBelch");
            moaiSoundPlayClientRpc("creatureVoice");
        }

        public void baseUpdate()
        {
            // death check for traps
            if (this.isEnemyDead && enemyHP > 0)
            {
                this.animator.speed = 1;
                base.KillEnemyOnOwnerClient(false);
                this.stopAllSound();
                animator.SetInteger("state", 3);
                isEnemyDead = true;
                enemyHP = 0;
                moaiSoundPlayClientRpc("creatureDeath");
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
            if (stunNormalizedTimer > 0f)
            {
                agent.speed = 0f;
            }

            if (!isEnemyDead)
            {
                if (eatingTimer > 0)
                {
                    if (RoundManager.Instance.IsServer) { DoAnimationClientRpc(2); }
                    this.animator.speed = 1.5f;
                }
                else if (agent.velocity.magnitude > (agent.speed / 4))
                {
                    if (RoundManager.Instance.IsServer) { DoAnimationClientRpc(1); }
                    this.animator.speed = agent.velocity.magnitude / 3;
                }
                else if (agent.velocity.magnitude <= (agent.speed / 4))
                {
                    if (RoundManager.Instance.IsServer) { DoAnimationClientRpc(0); }
                    this.animator.speed = 1;
                }
            }
        }

        public void baseAIInterval()
        {
            goodBoy -= 1;
            if (provokePoints > 0)
            {
                goodBoy = 0;
                provokePoints--;
            }

            if (entranceDelay > 0) { entranceDelay--; }
            slidingSoundTickClientRpc();

            // source update cycle
            if (sourcecycle > 0)
            {
                sourcecycle--;
            }
            else
            {
                source = FindObjectsOfType<GrabbableObject>();
                sourcecycle = 75;
                unreachableItems.Clear();
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
                nearestEntrance = ePack.tele;
                nearestEntranceNavPosition = ePack.navPosition;

                if (stamina < 120)
                {
                    stamina += 3;  // a moai regenerates all of its stamina in 30 seconds?
                }

                if (currentBehaviourStateIndex == (int)State.Guard || currentBehaviourStateIndex == (int)State.StickingInFrontOfEnemy)
                {
                    mostRecentPlayer = moaiGetNearestPlayer();
                }
            }

            // bug fix
            if (transform.Find("Halo").gameObject.activeSelf && goodBoy <= 0)
            {
                moaiSetHaloClientRpc(false);
            }

            if (targetPlayer != null)
            {
                mostRecentPlayer = targetPlayer;
            }
        }

        public void baseSearchingForPlayer()
        {
            agent.speed = 3f * moaiGlobalSpeed.Value;

            // sound switch
            if (!creatureVoice.isPlaying)
            {
                moaiSoundPlayClientRpc("creatureVoice");
                moaiSoundPlayClientRpc("creatureBelch");
            }

            // good boy state switch
            if (goodBoy > 0)
            {
                StopSearch(currentSearch);
                guardTarget = Vector3.zero;
                SwitchToBehaviourClientRpc((int)State.Guard);
                moaiSetHaloClientRpc(true);
                return;
            }

            // entrance state switch
            updateEntranceChance();
            if (this.enemyRandom.NextDouble() < chanceToLocateEntrance && gameObject.transform.localScale.x <= 2.2f)
            {
                Debug.Log("MOAI: entrance state switch");
                StopSearch(currentSearch);
                SwitchToBehaviourClientRpc((int)State.HeadingToEntrance);
            }

            if (FoundClosestPlayerInRange(28f, true) || provokePoints > 0)
            {
                StopSearch(currentSearch);
                SwitchToBehaviourClientRpc((int)State.StickingInFrontOfPlayer);
                return;
            }

            // object search and state switch;
            if (getObj() && !unreachableItems.Contains(getObj())) { SwitchToBehaviourClientRpc((int)State.HeadSwingAttackInProgress); }
            if (corpseAvailable()) { SwitchToBehaviourClientRpc((int)State.HeadSwingAttackInProgress); }
        }

        public void baseHeadingToEntrance()
        {
            targetPlayer = null;
            //Debug.Log("Heading to Entrance...");
            //Debug.Log(Vector3.Distance(transform.position, nearestEntrance.transform.position));
            SetDestinationToPosition(nearestEntranceNavPosition);
            if (this.isOutside != nearestEntrance.isEntranceToBuilding || this.agent.pathStatus == NavMeshPathStatus.PathPartial)
            {
                //Debug.Log("Entrance is not in navigation zone... Cancelling state");
                if (goodBoy <= 0)
                {
                    entranceDelay = 150;
                    StartSearch(transform.position);
                    SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                }
                else
                {
                    entranceDelay = 20;
                    StopSearch(currentSearch);
                    guardTarget = Vector3.zero;
                    SwitchToBehaviourClientRpc((int)State.Guard);
                }
            }
            if (Vector3.Distance(transform.position, nearestEntranceNavPosition) < (2.0 + gameObject.transform.localScale.x))
            {
                if (nearestEntrance.isEntranceToBuilding)
                {
                    Debug.Log("MOAI: Warp in");
                    EntityWarp.SendEnemyInside(this);
                    nearestEntrance.PlayAudioAtTeleportPositions();
                }
                else
                {
                    Debug.Log("MOAI: Warp out");
                    EntityWarp.SendEnemyOutside(this, true);
                    nearestEntrance.PlayAudioAtTeleportPositions();
                }
                if (goodBoy <= 0)
                {
                    entranceDelay = 150;
                    StartSearch(transform.position);
                    SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                }
                else
                {
                    entranceDelay = 20;
                    StopSearch(currentSearch);
                    guardTarget = Vector3.zero;
                    SwitchToBehaviourClientRpc((int)State.Guard);
                }
            }

            if (provokePoints > 0)
            {
                StartSearch(transform.position);
                SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
            }
        }

        public void baseGuard(bool goldMode = false)
        {
            targetPlayer = null;
            agent.speed = 4f * moaiGlobalSpeed.Value;

            if (guardTarget == Vector3.zero)
            {
                impatience = 0;
                wait = 20;
                guardTarget = pickGuardNode();
            }

            SetDestinationToPosition(guardTarget);

            if (Vector3.Distance(transform.position, guardTarget) < (transform.localScale.magnitude + transform.localScale.magnitude + impatience))
            {
                if (wait <= 0)
                {
                    guardTarget = Vector3.zero;
                }
                else
                {
                    wait--;
                }
            }
            else
            {
                impatience += 0.1f;
            }

            // invalid path cancellation
            if (agent.pathStatus == NavMeshPathStatus.PathPartial || agent.pathStatus == NavMeshPathStatus.PathInvalid)
            {
                guardTarget = Vector3.zero;
            }

            // prevents issues with struggling to reach a destination
            if (impatience == 10)
            {
                guardTarget = Vector3.zero;
            }

            // simply follow the player outside... with a delay
            if (mostRecentPlayer && mostRecentPlayer.isInsideFactory == this.isOutside && (goldMode || entranceDelay <= 0))
            {
                Debug.Log("MOAI: entrance state switch");
                StopSearch(currentSearch);
                SwitchToBehaviourClientRpc((int)State.HeadingToEntrance);
            }

            // sound switch
            if (!creatureVoice.isPlaying && !goldMode)
            {
                moaiSoundPlayClientRpc("creatureVoice");
            }

            // good boy state switch
            if (goodBoy <= 0)
            {
                StartSearch(transform.position);
                SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                moaiSetHaloClientRpc(false);
            }

            // object search and state switch;
            if (ClosestEnemyInRange(28) && !goldMode)
            {
                SwitchToBehaviourClientRpc((int)State.StickingInFrontOfEnemy);
            }
        }

        public void baseStickingInFrontOfEnemy(float maxRange = 28f)
        {
            targetPlayer = null;
            agent.speed = 7f * moaiGlobalSpeed.Value;
            var closestMonster = ClosestEnemyInRange(maxRange);
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

            if (goodBoy <= 0)
            {
                moaiSetHaloClientRpc(false);
                StartSearch(transform.position);
                SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
            }

            if (!closestMonster)
            {
                StopSearch(currentSearch);
                guardTarget = Vector3.zero;
                SwitchToBehaviourClientRpc((int)State.Guard);
                return;
            }

            // Charge into monster
            StalkPos = closestMonster.transform.position;
            SetDestinationToPosition(StalkPos, checkForPath: false);
        }

        public void baseStickingInFrontOfPlayer(float maxRange = 22f)
        {
            agent.speed = 5.3f * moaiGlobalSpeed.Value;
            updateEntranceChance();

            this.stamina -= 1.5f;  // all stamina (150) is lost in 15 seconds?

            // sound switch 
            if (!creatureSFX.isPlaying)
            {
                moaiSoundPlayClientRpc("creatureSFX");
            }

            // good boy state switch
            if (goodBoy > 0)
            {
                StopSearch(currentSearch);
                guardTarget = Vector3.zero;
                SwitchToBehaviourClientRpc((int)State.Guard);
                moaiSetHaloClientRpc(true);
            }

            // Keep targetting closest player, unless they are over 20 units away and we can't see them.
            if (!FoundClosestPlayerInRange(maxRange, false) && !FoundClosestPlayerInRange(maxRange + 6f, true) && provokePoints <= 0)
            {
                targetPlayer = null;
                StartSearch(transform.position);
                SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                return;
            }
            StickingInFrontOfPlayer();
        }

        // create a navmesh position that the moai must reach to consume/grab a corpse
        // this way navigation is much less buggy
        public Vector3 objNavPos(GrabbableObject objTarget)
        {
            if (!objTarget) { return Vector3.zero; }
            else
            {
                NavMeshHit hit;
                var result = NavMesh.SamplePosition(objTarget.transform.position, out hit, 5f, NavMesh.AllAreas);
                if (result) { return hit.position; }
                else { return Vector3.zero; }
            }
        }

        public void baseHeadSwingAttackInProgress(bool transitionOverride = false)
        {
            // sound switch
            if (!eatingHuman && !eatingScrap)
            {
                if (!creatureFood.isPlaying)
                {
                    //Debug.Log("MSOUND: creatureFood");
                    moaiSoundPlayClientRpc("creatureFood");
                }
            }
            else
            {
                if (!creatureEat.isPlaying && eatingScrap)
                {
                    //Debug.Log("MSOUND: creatureEat");
                    moaiSoundPlayClientRpc("creatureEat");
                }
                if (!creatureEatHuman.isPlaying && eatingHuman)
                {
                    //Debug.Log("MSOUND: creatureEatHuman");
                    moaiSoundPlayClientRpc("creatureEatHuman");
                }
                if (eatingTimer > 0)
                {
                    eatingTimer--;
                }
                else if (eatingTimer == 0)
                {
                    GrabbableObject devouredObj = getObj();
                    if (devouredObj)
                    {
                        goodBoy = (int)Math.Pow(devouredObj.scrapValue * 1.5, 1.8);
                        enemyHP += (devouredObj.scrapValue / 10);

                        // destroy locust ai if destroying a hive
                        if (devouredObj.gameObject.name.ToLower().Contains("redlocust"))
                        {
                            var bees = GameObject.FindObjectsOfType<RedLocustBees>();
                            foreach (RedLocustBees b in bees)
                            {
                                if (b.hive.NetworkObjectId == devouredObj.NetworkObjectId)
                                {
                                    b.OnNetworkDespawn();
                                    Destroy(b.gameObject);
                                }
                            }
                        }

                        devouredObj.OnNetworkDespawn();
                        Destroy(devouredObj.NetworkObject);
                        Destroy(devouredObj.propBody);
                        Destroy(devouredObj.gameObject);
                        Destroy(devouredObj);
                    }

                    PlayerControllerB ply2 = getPlayerCorpse();
                    if (ply2)
                    {
                        // 50/50 chance for a real body to cause a conversion
                        if (enemyRandom.NextDouble() < (0.5 * Plugin.soulRarity.Value))
                        {
                            respawnEvent();
                        }
                        destroyBodyClientRpc(ply2.NetworkObject.NetworkObjectId);
                    }

                    DeadBodyInfo dmy2 = getDummyCorpse();
                    if (dmy2)
                    {
                        // there's a 10% chance for a dummy to cause a soul devourer conversion
                        if (enemyRandom.NextDouble() < (0.1 * Plugin.soulRarity.Value))
                        {
                            respawnEvent();
                        }
                        destroyDummyClientRpc(getDummyId(dmy2));
                        moaiSoundPlayClientRpc("stopEatHuman");
                    }
                }
            }

            // consumption
            GrabbableObject obj = getObj();
            PlayerControllerB ply = getPlayerCorpse();
            DeadBodyInfo dmy = getDummyCorpse();

            // for those items / paths that cannot be reached
            if (agent.pathStatus == NavMeshPathStatus.PathPartial || agent.pathStatus == NavMeshPathStatus.PathInvalid)
            {
                unreachableItems.Add(obj);
                eatingHuman = false;
                eatingScrap = false;
                eatingTimer = -1;
                StartSearch(transform.position);
                SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                return;
            }

            if ((obj == null && ply == null && dmy == null) || (!transitionOverride && goodBoy > 0) || provokePoints > 0)
            {
                //Debug.Log("MOAI: Lost Object. Ending obj search.");
                eatingHuman = false;
                eatingScrap = false;
                eatingTimer = -1;
                StartSearch(transform.position);
                SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
            }
            else
            {

                if (ply)
                {
                    //Debug.Log("MOAI: Heading to found Player");
                    targetPlayer = null;
                    targetNode = ply.deadBody.transform;
                    SetDestinationToPosition(ply.deadBody.transform.position);
                    if (Vector3.Distance(transform.position, ply.deadBody.transform.position) < ply.deadBody.transform.localScale.magnitude + transform.localScale.magnitude)
                    {
                        if (!eatingHuman)
                        {
                            Debug.Log("MOAI: Attaching Body to Mouth");
                            if (eatingTimer <= 0)
                            {
                                eatingTimer = 150;
                            }
                            attachBodyClientRpc(ply.NetworkObject.NetworkObjectId);
                            moaiSoundPlayClientRpc("creatureEatHuman");
                        }
                        eatingHuman = true;
                    }
                    else
                    {
                        eatingHuman = false;
                    }
                }
                else if (dmy)
                {
                    targetPlayer = null;
                    targetNode = dmy.transform;
                    agent.speed = Math.Min(8.0f * moaiGlobalSpeed.Value, agent.speed);
                    SetDestinationToPosition(dmy.transform.position);
                    if (Vector3.Distance(transform.position, dmy.transform.position) < dmy.transform.localScale.magnitude + transform.localScale.magnitude)
                    {
                        if (!eatingHuman)
                        {
                            Debug.Log("MOAI: Attaching Dummy to Mouth");
                            if (eatingTimer <= 0)
                            {
                                eatingTimer = 150;
                            }
                            attachDummyClientRpc(getDummyId(dmy));
                            moaiSoundPlayClientRpc("creatureEatHuman");
                        }
                        eatingHuman = true;
                    }
                    else
                    {
                        eatingHuman = false;
                    }
                }
                else if (obj)
                {
                    //Debug.Log("MOAI: Heading to found Scrap");
                    targetPlayer = null;
                    targetNode = obj.transform;
                    Vector3 navDestination = objNavPos(obj);
                    SetDestinationToPosition(navDestination);
                    if (Vector3.Distance(transform.position, navDestination) < obj.transform.localScale.magnitude + transform.localScale.magnitude)
                    {
                        if (obj.IsLocalPlayer)
                        {
                            if (!eatingHuman)
                            {
                                Debug.Log("MOAI: Attaching Body to Mouth");
                                eatingTimer = 150;
                                attachBodyClientRpc(ply.NetworkObject.NetworkObjectId);
                                moaiSoundPlayClientRpc("creatureEatHuman");
                            }
                            eatingHuman = true;
                        }
                        else if (!eatingScrap)
                        {
                            eatingTimer = (int)(obj.scrapValue / 1.8) + 15;
                            moaiSoundPlayClientRpc("creatureEat");
                        }
                        eatingScrap = true;
                    }
                    else
                    {
                        eatingScrap = false;
                    }
                }
            }
            if (!eatingHuman && !eatingScrap)
            {
                eatingTimer = -1;
            }
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

        public bool corpseAvailable()
        {
            return getPlayerCorpse() || getDummyCorpse();
        }

        public DeadBodyInfo getDummyCorpse()
        {
            try
            {
                List<DeadBodyInfo> bodyList = new List<DeadBodyInfo>();
                for (int i = 0; i < dummyBodies.Count; i++)
                {
                    GameObject body = dummyBodies[i].gameObject;
                    var d = 1000f;
                    if (body != null && body.activeInHierarchy == true && body.transform != null)
                    {
                        d = Vector3.Distance(transform.position, body.transform.position);
                    }

                    if (d < 200.0f)
                    {
                        //Debug.Log("found player to eat");
                        bodyList.Add(dummyBodies[i]);
                    }
                }
                if (bodyList.Count > 0)
                {
                    for (int i = 0; i < bodyList.Count; i++)
                    {
                        if (targetedBodies[i] == -1 || targetedBodies[i] == GetInstanceID())
                        {
                            targetedBodies[i] = GetInstanceID();
                            return bodyList[i];
                        }
                    }
                    return bodyList[enemyRandom.Next(0, bodyList.Count)];
                }
            }
            catch (Exception e)
            {
                Debug.Log("MOAI: Exception searching for dummy body. Not an error.");
            }

            return null;
        }

        public PlayerControllerB getPlayerCorpse()
        {
            //Debug.Log("MOAI: Human Food Search");
            // look for human food first
            for (int i = 0; i < RoundManager.Instance.playersManager.allPlayerScripts.Length; i++)
            {
                PlayerControllerB player = RoundManager.Instance.playersManager.allPlayerScripts[i];

                if (player != null && player.name != null && player.transform != null)
                {

                    var d = 1000f;
                    if (player.deadBody != null && player.deadBody.isActiveAndEnabled && !player.deadBody.isInShip)
                    {
                        d = Vector3.Distance(transform.position, player.deadBody.transform.position);
                    }

                    //Debug.Log("MOAI: Human -> " + player.name + " dist - " + d + " dead? " + player.isPlayerDead);
                    if (d < 20.0f && player.deadBody != null && player.deadBody.isActiveAndEnabled && !player.deadBody.isInShip)
                    {
                        //Debug.Log("found player to eat");
                        return player;
                    }
                }
            }

            return null;
        }

        // return null if there are no valid objects to eat.
        // otherwise return a object
        public GrabbableObject getObj()
        {
            // setting override for item consumption
            if (Plugin.moaiConsumeScrap.Value == false && !this.gameObject.name.ToLower().Contains("gold")) { return null; }

            List<GrabbableObject> itemList = new List<GrabbableObject>();
            try
            {
                for (int i = 0; i < source.Length; i++)
                {
                    GrabbableObject obj = source[i];
                    //LogIfDebugBuild(obj.name);

                    if (Vector3.Distance(transform.position, obj.transform.position) < 20.0f && !obj.heldByPlayerOnServer && !obj.isInShipRoom && !unreachableItems.Contains(obj))
                    {
                        //Debug.Log("MOAI: Returning object -> " + obj.name);
                        if (!obj.name.ToLower().Contains("gold"))
                        {
                            itemList.Add(obj);
                        }
                    }
                }

                if (itemList.Count > 0)
                {
                    // prioritize the item with the highest score, which is distance combined with value
                    float highestItemScore = 0;
                    GrabbableObject bestItem = itemList[0];
                    for (int i = 0; i < itemList.Count; i++)
                    {
                        var score = (1 / Vector3.Distance(transform.position, itemList[i].transform.position)) * itemList[i].scrapValue;
                        if (score > highestItemScore)
                        {
                            bestItem = itemList[i];
                            highestItemScore = score;
                        }
                    }
                    return bestItem;
                }
            }
            catch (IndexOutOfRangeException)
            {
                //Debug.Log("MOAI: Refreshing Source -L- ");
                source = FindObjectsOfType<GrabbableObject>();
            }
            catch (NullReferenceException)
            {
                return null;
            }
            return null;   // no food :(
        }

        public Vector3 pickGuardNode()
        {

            //Debug.Log("MOAIGUARD: Picking Guard Node");
            List<GameObject> allGoodNodes = new List<GameObject>();

            foreach (GameObject g in allAINodes)
            {
                for (int i = 0; i < StartOfRound.Instance.allPlayerScripts.Length; i++)
                {
                    Vector3 playerPos = StartOfRound.Instance.allPlayerScripts[i].gameObject.transform.position;
                    //Debug.Log(playerPos);
                    //Debug.Log(g.transform.position);
                    float dist = Vector3.Distance(g.transform.position, playerPos);
                    //Debug.Log("Dist: " + dist);
                    //Debug.Log("dist < " + (23 + this.transform.localScale.x));
                    if (dist < (13 + this.transform.localScale.x) && !StartOfRound.Instance.allPlayerScripts[i].isPlayerDead)
                    {
                        allGoodNodes.Add(g);
                        //Debug.Log("appended to good node -> " + allGoodNodes.Count + " - " + allGoodNodes.ToString());
                    }
                }
            }

            GameObject nodeAnchor = null;  // we generate a location from the anchor
            if (allGoodNodes.Count > 0)
            {
                //Debug.Log("MOAIGUARD: Returning Good Node");
                nodeAnchor = allGoodNodes[UnityEngine.Random.RandomRangeInt(0, allGoodNodes.Count)];
            }
            else
            {
                //Debug.Log("MOAIGUARD: Returning Random Node");
                nodeAnchor = allAINodes[UnityEngine.Random.RandomRangeInt(0, allAINodes.Length)];
            }

            // pick a random position from the anchor
            Vector3 variation = new Vector3((float)(8 - enemyRandom.NextDouble() * 16), 0, (float)(8 - enemyRandom.NextDouble() * 16));
            Vector3 newPos = nodeAnchor.transform.position + variation;
            NavMeshHit hit;
            var result = NavMesh.SamplePosition(newPos, out hit, 24f, NavMesh.AllAreas);

            if (result)
            {
                return hit.position;
            }
            else { return allGoodNodes[UnityEngine.Random.RandomRangeInt(0, allGoodNodes.Count)].transform.position; }
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

        protected EnemyAI ClosestEnemyInRange(float range)
        {
            if (recovering)
            {
                return null;
            }

            var enemies = RoundManager.Instance.SpawnedEnemies;
            var closestDist = range;
            EnemyAI closestEnemy = null;
            for (int i = 0; i < enemies.Count; i++)
            {
                var enemy = enemies[i];

                // only target evil moai from the list
                if (enemy.gameObject.name.ToLower().Contains("moai"))
                {
                    if (enemy.transform.Find("Halo"))
                    {
                        if (!enemy.transform.Find("Halo").gameObject.activeSelf)
                        {
                            var dist = Vector3.Distance(transform.position, enemy.transform.position);
                            if (dist < closestDist && enemy.enemyHP > 0 && !enemy.isEnemyDead && enemy.GetInstanceID() != GetInstanceID() && !unreachableEnemies.Contains(enemy))
                            {
                                closestDist = dist;
                                closestEnemy = enemy;
                            }
                        }
                    }
                }
                else // target enemies in general
                {
                    var dist = Vector3.Distance(transform.position, enemy.transform.position);
                    if (dist < closestDist && enemy.enemyHP > 0 && !enemy.isEnemyDead && !unreachableEnemies.Contains(enemy))
                    {
                        closestDist = dist;
                        closestEnemy = enemy;
                    }
                }
            }
            if (closestEnemy != null && !closestEnemy.isEnemyDead && closestEnemy.enemyHP > 0 && closestEnemy.enemyType.canDie && closestEnemy.gameObject.activeSelf)
            {
                if (!closestEnemy.gameObject.name.ToLower().Contains("locust"))  // dumb locusts
                {
                    return closestEnemy;
                }
            }
            return null;
        }


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
            }
            stamina = 60;
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
            if (collidedEnemy.gameObject.name.ToLower().Contains("moai"))
            {
                // halos don't hit halos, non-halos don't hit non-halos
                if (transform.Find("Halo").gameObject.activeSelf == collidedEnemy.transform.Find("Halo").gameObject.activeSelf)
                {
                    return;
                }
            }
            this.timeSinceHittingLocalPlayer = 0f;
            collidedEnemy.HitEnemy(1, null, true);
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

        // method to play a sound with a target string id
        // can be overridden in moai variants (thus it is usable in MoaiNormalNet)
        public virtual void playSoundId(String id) { }

        public void stopAllSound()
        {
            // normal creature sounds
            creatureSFX.Stop();
            creatureVoice.Stop();
            creatureEat.Stop();
            creatureEatHuman.Stop();
            creatureFood.Stop();
            creatureDig.Stop();
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
                case "creatureFood":
                    creatureSFX.Stop();
                    creatureVoice.Stop();
                    creatureFood.Play();
                    break;
                case "creatureEat":
                    creatureSFX.Stop();
                    creatureVoice.Stop();
                    creatureEat.Play();
                    break;
                case "creatureEatHuman":
                    creatureSFX.Stop();
                    creatureVoice.Stop();
                    creatureEatHuman.Play();
                    break;
                case "stopEatHuman":
                    creatureEatHuman.Stop();
                    break;
                case "creatureHit":
                    creatureHit.Play();
                    break;
                case "creatureDeath":
                    stopAllSound();
                    creatureDeath.Play();
                    break;
                case "creatureBelch":
                    creatureBelch.Play();
                    break;
                case "creatureBlitz":
                    playSoundId("creatureBlitz");
                    break;
                case "creaturePrepare":
                    playSoundId("creaturePrepare");
                    break;
                case "creatureKidnap":
                    playSoundId("creatureKidnap");
                    break;
            }
        }

        [ClientRpc]
        public void slidingSoundTickClientRpc()
        {
            if (isEnemyDead || agent.velocity.magnitude < (agent.speed / 8 + 1))
            {
                if (isSliding)
                {
                    stopSlideSounds();
                    isSliding = false;
                }
                return;
            }

            var slideMaterial = getCurrentMaterialSittingOn();
            switch (slideMaterial)
            {
                default:
                    if (!slidingBasic.isPlaying)
                    {
                        stopSlideSounds();
                        isSliding = true;
                        slidingBasic.Play();
                    }
                    break;
                case "Gravel":
                    if (!slidingGravel.isPlaying)
                    {
                        stopSlideSounds();
                        isSliding = true;
                        slidingGravel.Play();
                    }
                    break;
                case "CatWalk":
                    if (!slidingMetal.isPlaying)
                    {
                        stopSlideSounds();
                        isSliding = true;
                        slidingMetal.Play();
                    }
                    break;
                case "Aluminum":
                    if (!slidingMetal.isPlaying)
                    {
                        stopSlideSounds();
                        isSliding = true;
                        slidingMetal.Play();
                    }
                    break;
                case "Dirt":
                    if (!slidingGravel.isPlaying)
                    {
                        stopSlideSounds();
                        isSliding = true;
                        slidingGravel.Play();
                    }
                    break;
                case "Snow":
                    if (!slidingSnow.isPlaying)
                    {
                        stopSlideSounds();
                        isSliding = true;
                        slidingSnow.Play();
                    }
                    break;
                case "Carpet":
                    if (!slidingSnow.isPlaying)
                    {
                        stopSlideSounds();
                        isSliding = true;
                        slidingSnow.Play();
                    }
                    break;
                case "Wood":
                    if (!slidingWood.isPlaying)
                    {
                        stopSlideSounds();
                        isSliding = true;
                        slidingWood.Play();
                    }
                    break;
                case "None":
                    stopSlideSounds();
                    isSliding = false;
                    break;
            }
        }

        public void stopSlideSounds()
        {
            slidingBasic.Stop();
            slidingGravel.Stop();
            slidingMetal.Stop();
            slidingSnow.Stop();
            slidingWood.Stop();
        }

        public String getCurrentMaterialSittingOn()
        {
            var slidingSurface = "None";
            var interactRay = new Ray(this.transform.position + Vector3.up, -Vector3.up);
            RaycastHit castHit;
            if (Physics.Raycast(interactRay, out castHit, 6f, StartOfRound.Instance.walkableSurfacesMask, QueryTriggerInteraction.Ignore))
            {
                for (int i = 0; i < StartOfRound.Instance.footstepSurfaces.Length; i++)
                {
                    // go through all surfaces
                    if (castHit.collider.CompareTag(StartOfRound.Instance.footstepSurfaces[i].surfaceTag))
                    {
                        slidingSurface = StartOfRound.Instance.footstepSurfaces[i].surfaceTag;
                    }
                }
            }
            //Debug.Log("MOAI: Sliding Surface = " + slidingSurface);
            return slidingSurface;
        }

        public static int getDummyId(DeadBodyInfo dmy)
        {
            return int.Parse(dmy.name.Split("-")[1]);
        }

        [ClientRpc]
        public void createDummyClientRpc(Vector3 targetPos, int dummyId, int attachedLimbTarget)
        {
            GameObject ragdollPrefab = StartOfRound.Instance.playerRagdolls[0];
            GameObject ragdoll = UnityEngine.Object.Instantiate(ragdollPrefab, targetPos, Quaternion.identity);
            var bdy = ragdoll.GetComponent<DeadBodyInfo>();
            bdy.overrideSpawnPosition = true;
            bdy.attachedLimb = bdy.bodyParts[attachedLimbTarget];

            bdy.gameObject.name = "moaidummybody-" + dummyId;
            if (RoundManager.Instance.IsHost)
            {
                dummyBodies.Add(bdy);
                targetedBodies.Add(-1);
            }
        }

        [ClientRpc]
        public void destroyDummyClientRpc(int dummyId)
        {

            GameObject g = GameObject.Find("moaidummybody-" + dummyId);
            DeadBodyInfo dmy = g.GetComponent<DeadBodyInfo>();
            if (RoundManager.Instance.IsHost)
            {
                for (int i = 0; i < dummyBodies.Count; i++)
                {
                    if (dummyBodies[i].GetInstanceID() == dmy.GetInstanceID())
                    {
                        dummyBodies.RemoveAt(i);
                        targetedBodies.RemoveAt(i);
                    }
                }
            }
            Destroy(dmy.gameObject);
        }

        [ClientRpc]
        public void attachDummyClientRpc(int dummyId)
        {
            GameObject g = GameObject.Find("moaidummybody-" + dummyId);
            DeadBodyInfo dmy = g.GetComponent<DeadBodyInfo>();
            dmy.attachedTo = mouth.transform;
        }


        // a respawn event consists of
        // a dig animation and sound
        // replacing a moai with a soul devourer once the animation finishes
        public async void respawnEvent()
        {
            if (!RoundManager.Instance.IsServer) { return; }

            beginDiggingClientRpc();

            int timeout = 20;
            while (!animator.GetCurrentAnimatorStateInfo(0).IsName("Transformation"))
            {
                timeout--;
                if (timeout <= 0) { return; }
                await Task.Delay(100);
            }

            while (animator.GetCurrentAnimatorStateInfo(0).IsName("Transformation"))
            {
                await Task.Delay(500);
            }

            if (isEnemyDead && isDeadAnimationDone)
            {
                await Task.Delay(500);
                replaceWithSoulDevourer();
            }
        }

        // play the digging animation and sound for spawning the soul devourer
        [ClientRpc]
        public void beginDiggingClientRpc()
        {
            animator.Play("Transformation");
            stopAllSound();
            creatureDig.Play();

            // make moai dead to prevent ai ticks and updates from other sources
            enemyHP = 0;
            isEnemyDead = true;
            isDeadAnimationDone = true;
        }

        // delete self and spawn in a Soul Devourer
        public void replaceWithSoulDevourer()
        {
            if (!RoundManager.Instance.IsServer) { return; }

            GameObject go = UnityEngine.Object.Instantiate<GameObject>(SoulDevourer.enemyPrefab, transform.position, transform.rotation);
            go.GetComponent<NetworkObject>().Spawn(true);
            Debug.Log("MOAI: Spawned Soul Devourer at: " + transform.ToString());
            RoundManager.Instance.SpawnedEnemies.Add(go.GetComponent<EnemyAI>());

            Destroy(this.gameObject);
        }

        [ClientRpc]
        // note that this is only for rotation animations (cause its moai)
        // these are synced through a network transform
        public void DoAnimationClientRpc(int index)
        {
            //LogIfDebugBuild($"Animation: {index}");
            if (RoundManager.Instance.IsServer)
            {
                if (this.animator) { this.animator.SetInteger("state", index); }
            }
        }

        [ClientRpc]
        public void moaiSetHaloClientRpc(bool value)
        {
            setHalo(value);
        }

        [ClientRpc]
        public void destroyBodyClientRpc(ulong playernetid)
        {
            for (int i = 0; i < RoundManager.Instance.playersManager.allPlayerScripts.Length; i++)
            {
                PlayerControllerB player = RoundManager.Instance.playersManager.allPlayerScripts[i];

                if (player != null && player.name != null && player.transform != null)
                {
                    if (player.NetworkObject.NetworkObjectId == playernetid)
                    {
                        Debug.Log("MOAI: Successfully destroyed body with id = " + playernetid);
                        player.deadBody.DeactivateBody(false);
                    }
                }
            }
        }

        [ClientRpc]
        public void attachBodyClientRpc(ulong playerid)
        {
            for (int i = 0; i < RoundManager.Instance.playersManager.allPlayerScripts.Length; i++)
            {
                PlayerControllerB player = RoundManager.Instance.playersManager.allPlayerScripts[i];

                if (player != null && player.name != null && player.transform != null)
                {
                    if (player.NetworkObject.NetworkObjectId == playerid)
                    {
                        player.deadBody.attachedLimb = player.deadBody.bodyParts[5];

                        if (mouth)
                        {
                            Debug.Log("MOAI: Successfully attached body to mouth with id = " + playerid);
                            player.deadBody.attachedTo = mouth.transform;
                        }
                        else
                        {
                            Debug.Log("MOAI: Successfully attached body to eye with id = " + playerid);
                            player.deadBody.attachedTo = eye.transform;
                        }
                        player.deadBody.canBeGrabbedBackByPlayers = true;
                    }
                }
            }
        }

        [ClientRpc]
        public void setSizeClientRpc(float size, float pitchAlter)
        {
            gameObject.transform.localScale *= (size * Plugin.moaiGlobalSize.Value);
            gameObject.GetComponent<NavMeshAgent>().height *= size;

            creatureSFX.pitch /= pitchAlter;
            creatureVoice.pitch /= pitchAlter;
            creatureFood.pitch /= pitchAlter;
            creatureEat.pitch /= pitchAlter;
            creatureEatHuman.pitch /= pitchAlter;
            creatureHit.pitch /= pitchAlter;
            creatureDeath.pitch /= pitchAlter;
            creatureBelch.pitch /= pitchAlter;
            setPitches(pitchAlter);
        }

        [ClientRpc]
        public void enableStrikerClientRpc(bool pkg)
        {
            Debug.Log("MOAI: Enabling LightningStriker Obj.");

            GameObject weather = GameObject.Find("TimeAndWeather");

            if (weather == null)
            {
                Debug.LogError("MOAI: Not enabling LightningStriker Obj for Blue Moai: TimeAndWeather not found!");
            }

            // find "Stormy" in weather
            GameObject striker = null;
            for (int i = 0; i < weather.transform.GetChildCount(); i++)
            {
                GameObject g = weather.transform.GetChild(i).gameObject;
                if (g.name.Equals("Stormy"))
                {
                    //Debug.Log("Lethal Chaos: Found Stormy!");
                    striker = g;
                }
            }
            striker.SetActive(true);
            Debug.Log("MOAI: striker successfully enabled.");
        }
    }
}

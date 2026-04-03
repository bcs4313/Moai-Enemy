using System;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using static MoaiEnemy.Plugin;
using MoaiEnemy.src.MoaiGreen;
using System.Drawing;
using static UnityEngine.GraphicsBuffer;
using LethalLib.Modules;
using System.Collections.Generic;
using System.Linq;

namespace MoaiEnemy.src.MoaiNormal
{

    class GreenEnemyAI : MOAIAICORE
    {
        // extra audio sources
        public AudioSource creatureConstruct;
        public AudioSource creatureFinish;

        // GameObject links
        public GameObject laserPoint1;
        public GameObject laserPoint2;
        public LineRenderer lp1r;
        public LineRenderer lp2r;
        public Transform laserTarget1;
        public Transform laserTarget2;
        public GameObject consumptionCircle;
        private static GameObject landminePrefab;
        private static GameObject turretPrefab;

        // map object management
        public static SpawnableOutsideObject[] mapObjects;
        public static int[] mapObjectOptions;
        int nextObject = 0;

        // scanning vars
        double scan1 = 0;
        double scan2 = 0;

        // constructing vars
        int buildTimer = 0;  // goes down 5 times per second
        int constructionTimeLeft = 0;
        float constructImpatience = 0.5f;
        String currentlyBuilding = "None";
        Vector3 constructPosition = Vector3.zero;
        Quaternion constructRotation;
        int constructId = 0;
        PlayerControllerB storedTarget = null;
        GameObject storedTargetEnemy = null;

        // consumption circle management
        protected GameObject consumptionCircleMade = null;
        int turretsLeft = 2;
        int bodiesToMake = 0;

        // plasma projectile firing
        int p_ticksTillFire = 0;
        int p_burstTickRate = 0;
        int p_burstFire = 0;
        int p_awaitBetweenBursts = 0;
        int p_alternatePredict = 0;
        public AudioSource laserFire;
        public Collider col1;
        public Collider col2;

        public List<GameObject> spawnedHazards;

        struct spawnOption
        {
            float chanceToPick;
            bool isOutsideObj;
            GameObject gameObject;
            SpawnableOutsideObject outsideObject;
        }

        public static void getMapObjects()
        {
            //mapObjects = Resources.FindObjectsOfTypeAll<SpawnableOutsideObject>();
        }


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
            Constructing,
            Archery
        }

        public static void findTraps()
        {
            RoundManager m = RoundManager.Instance;
            var traps = m.currentLevel.spawnableMapObjects;
            for (int i = 0; i < traps.Length; i++)
            {
                SpawnableMapObject trap = traps[i];
                if(trap.prefabToSpawn.name.ToLower().Contains("turret"))
                {
                    turretPrefab = trap.prefabToSpawn;
                }
                if(trap.prefabToSpawn.name.ToLower().Contains("mine"))
                {
                    landminePrefab = trap.prefabToSpawn;
                }
            }
        }

        public override void Start()
        {
            baseInit();
            findTraps();
            creatureConstruct.volume = moaiGlobalMusicVol.Value;
            creatureFinish.volume = moaiGlobalMusicVol.Value;
            laserFire.volume = moaiGlobalMusicVol.Value;
            buildTimer = 200; // 40 second build timer 
            spawnedHazards = new List<GameObject>();
        }

        public override void OnDestroy()
        {
            for(int i = 0; i < spawnedHazards.Count; i++)
            {
                if (spawnedHazards[i])
                {
                    Destroy(spawnedHazards[i]);
                }
            }
            base.OnDestroy();
        }

        public override void setPitches(float pitchAlter)
        {
            creatureConstruct.pitch /= pitchAlter;
            creatureFinish.pitch /= pitchAlter;
            laserFire.pitch /= pitchAlter;
        }

        public override void Update()
        {
            base.Update();
            baseUpdate();

            // death trigger 1
            if(isEnemyDead && (creatureConstruct.isPlaying || creatureFinish.isPlaying))
            {
                creatureConstruct.Stop();
                creatureFinish.Stop();
            }

            // death trigger 2
            if(isEnemyDead)
            {
                if (laserPoint1.activeInHierarchy)
                {
                    laserPoint1.SetActive(false);
                    laserPoint2.SetActive(false);
                }
                return;
            }

            // laser activation
            if(currentBehaviourStateIndex == (int)State.Constructing || currentBehaviourStateIndex == (int)State.StickingInFrontOfPlayer || currentBehaviourStateIndex == (int)State.StickingInFrontOfEnemy)
            {
                if (!laserPoint1.activeInHierarchy)
                {
                    laserPoint1.SetActive(true);
                    laserPoint2.SetActive(true);
                }
            }

            // searching for player laser movement
            if (buildTimer > 0 && currentBehaviourStateIndex == (int)State.SearchingForPlayer)
            {
                if (!laserPoint1.activeInHierarchy)
                {
                    laserPoint1.SetActive(true);
                    laserPoint2.SetActive(true);
                }
                Vector3 p1 = new Vector3((float)Math.Sin(scan1) * 4, 0, Math.Abs((float)Math.Sin(scan2) * 8) + 4);
                Vector3 p2 = new Vector3((float)Math.Cos(scan1) * 4, 0, Math.Abs((float)Math.Sin(scan2) * 8) + 4);

                // account for object rotation
                Vector3 rotatedP1 = transform.rotation * p1;
                Vector3 rotatedP2 = transform.rotation * p2;

                laserTarget1.position = transform.position + rotatedP1;
                laserTarget2.position = transform.position + rotatedP2;

                scan1 += enemyRandom.NextDouble() * 0.01 + 0.01;
                scan2 += enemyRandom.NextDouble() * 0.01 + 0.01;
            }

            // laser cancel
            int[] cancelStates = [(int)State.SearchingForPlayer, (int)State.Constructing, (int)State.StickingInFrontOfPlayer, (int)State.StickingInFrontOfEnemy];
            if (!cancelStates.Contains(currentBehaviourStateIndex))
            {
                if (laserPoint1.activeInHierarchy)
                {
                    laserPoint1.SetActive(false);
                    laserPoint2.SetActive(false);
                    lp1r.SetPosition(1, new Vector3(0, 0, 4));
                    lp2r.SetPosition(1, new Vector3(0, 0, 4));
                }
            }

            // HOST ONLY GUARD /////////////////////////////////////////////////////////////////////////
            if (!RoundManager.Instance.IsHost) { return; }

            if(currentBehaviourStateIndex == (int)State.StickingInFrontOfPlayer && storedTarget != null)
            {
                setLaserFocusClientRpc(storedTarget.transform.position);
                facePosition(storedTarget.transform.position);
            }

            if(currentBehaviourStateIndex == (int)State.StickingInFrontOfEnemy && storedTargetEnemy != null)
            {
                setLaserFocusClientRpc(storedTargetEnemy.transform.position);
                facePosition(storedTargetEnemy.transform.position);
            }

            if (buildTimer <= 0 && currentBehaviourStateIndex == (int)State.SearchingForPlayer && provokePoints <= 0)
            {
                playConstructSoundClientRpc();
                constructId = enemyRandom.Next(0, 3);
                SwitchToBehaviourClientRpc((int)State.Constructing);
                pickConstructPosition();
                Debug.Log("MOAI GREEN: CONSTRUCT POSITION -> " + constructPosition.ToString());
                Debug.Log("MOAI GREEN: TRANSFORM POSITION -> " + transform.position.ToString());
                Debug.Log("MOAI GREEN: OFFSET GENERATED -> " + (constructPosition - transform.position).ToString());
            }
            else if(buildTimer <= 0 && currentBehaviourStateIndex == (int)State.Constructing)
            {
                setLaserFocusClientRpc(constructPosition);
            }
        }

        public override void playSoundId(String id)
        {
        }

        [ClientRpc]
        public void stopAllSoundsClientRpc()
        {
            base.stopAllSound();
            creatureConstruct.Stop();
            creatureFinish.Stop();
        }

        public void firePlasmaProjectile(Vector3 pos, float spread, bool predictPosition, Vector3 targetVelocity)
        {

            if(predictPosition)
            {
                pos = PredictTargetPosition(pos, targetVelocity, 20);  // projectile speed is 20
            }

            Vector3 directionToTarget = pos - mouth.transform.position + new Vector3((float)(spread/2 - (enemyRandom.NextDouble() * spread)), spread/2 - (float)(enemyRandom.NextDouble() * spread), spread / 2 - (float)(enemyRandom.NextDouble() * spread));
            Quaternion spawnRotation = Quaternion.identity;

            // If directionToTarget is not zero, rotate to face target
            if (directionToTarget != Vector3.zero)
            {
                Quaternion targetRotation = Quaternion.LookRotation(directionToTarget);

                // Apply the rotation to the object's transform, preserving current pitch and roll
                spawnRotation = Quaternion.Euler(targetRotation.eulerAngles.x, targetRotation.eulerAngles.y, targetRotation.eulerAngles.z);
            }

            GameObject gameObject = UnityEngine.Object.Instantiate(Plugin.plasmaProjectile, (this.mouth.position + transform.forward * 2), spawnRotation);
            gameObject.SetActive(value: true);
            gameObject.GetComponent<NetworkObject>().Spawn();
            gameObject.GetComponent<PlasmaBall>().owner = this.GetInstanceID();
            playLaserFireClientRpc();

            // prevent collision with self
            Physics.IgnoreCollision(col1, gameObject.GetComponent<SphereCollider>());
            Physics.IgnoreCollision(col2, gameObject.GetComponent<SphereCollider>());
        }

        [ClientRpc]
        private void playLaserFireClientRpc()
        {
            laserFire.Play();
        }

        [ClientRpc]
        private void playConstructFinishClientRpc()
        {
            creatureConstruct.Stop();
            creatureFinish.Play();
        }

        [ClientRpc]
        private void playConstructSoundClientRpc()
        {
            creatureConstruct.Play();
        }
        

        private Vector3 PredictTargetPosition(Vector3 targetPosition, Vector3 targetVelocity, float projectileSpeed)
        {
            Vector3 directionToTarget = targetPosition - transform.position;
            float distanceToTarget = directionToTarget.magnitude;

            float timeToReachTarget = distanceToTarget / projectileSpeed;
            Vector3 predictedPosition = targetPosition + targetVelocity * timeToReachTarget;

            return predictedPosition;
        }

        public override void DoAIInterval()
        { 
            if(consumptionCircleMade && bodiesToMake > 0)
            {
                if(enemyRandom.NextDouble() < 0.1)
                {
                    createDummyClientRpc(consumptionCircleMade.transform.position, bdyInc, enemyRandom.Next(0, 11));
                    bdyInc++;
                    bodiesToMake--;
                }
            }
            base.DoAIInterval();
            baseAIInterval();
            buildTimer--;

            switch (currentBehaviourStateIndex)
            {
                case (int)State.SearchingForPlayer:
                    baseSearchingForPlayer();
                    setLaserColorClientRpc(1, 1, 1);
                    agent.speed = 4.2f * moaiGlobalSpeed.Value;
                    break;
                case (int)State.Constructing:
                    agent.speed = 0 * moaiGlobalSpeed.Value;

                    if (currentlyBuilding.Equals("None"))
                    {
                        switch (constructId)
                        {
                            case 0:
                                setLaserColorClientRpc(1, 0, 0);
                                if (turretsLeft > 0)
                                {
                                    if (NavMeshSpaceChecker.CanPlacePrefab("Turret", constructPosition))
                                    {
                                        currentlyBuilding = "Turret";
                                        constructionTimeLeft = 50;
                                        constructImpatience = 0.5f;
                                    }
                                    else
                                    {
                                        constructPosition = NavMeshSpaceChecker.GetRandomNavMeshPoint(constructImpatience, constructPosition);
                                        constructImpatience += 0.1f;
                                        facePosition(constructPosition);
                                    }
                                }
                                else
                                {
                                    constructId = enemyRandom.Next(0, 3);
                                }
                                break;
                            case 1:
                                setLaserColorClientRpc(1, 1, 0);
                                if (NavMeshSpaceChecker.CanPlacePrefab("Mine", constructPosition))
                                {
                                    currentlyBuilding = "Mine";
                                    constructionTimeLeft = 25;
                                    constructImpatience = 0.5f;
                                }
                                else
                                {
                                    constructPosition = NavMeshSpaceChecker.GetRandomNavMeshPoint(constructImpatience, constructPosition);
                                    constructImpatience += 0.1f;
                                    facePosition(constructPosition);
                                }
                                break;
                            case 2:
                                if (!consumptionCircleMade)
                                {
                                    setLaserColorClientRpc(0, 0, 1);
                                    if (NavMeshSpaceChecker.CanPlacePrefab("Circle", constructPosition))  // there can only be one consumption circle per green moai
                                    {
                                        currentlyBuilding = "Circle";
                                        constructionTimeLeft = 75;
                                        constructImpatience = 0.5f;
                                    }
                                    else
                                    {
                                        constructPosition = NavMeshSpaceChecker.GetRandomNavMeshPoint(constructImpatience, constructPosition);
                                        constructImpatience += 0.1f;
                                        facePosition(constructPosition);

                                    }
                                }
                                else
                                {
                                    constructId = enemyRandom.Next(0, 3);
                                }
                                break;
                            case 3:
                                setLaserColorClientRpc(255f / 255f, 0, 192f / 255f);
                                if (NavMeshSpaceChecker.CanPlacePrefab("MapObject", constructPosition, mapObjects[enemyRandom.Next(0, mapObjects.Length)]))  // there can only be one consumption circle per green moai
                                {
                                    currentlyBuilding = "MapObject";
                                    constructionTimeLeft = 25;
                                    constructImpatience = 0.5f;
                                }
                                else
                                {
                                    constructPosition = NavMeshSpaceChecker.GetRandomNavMeshPoint(constructImpatience, constructPosition);
                                    constructImpatience += 0.1f;
                                    facePosition(constructPosition);
                                }
                                break;
                        }

                        if(constructImpatience >= 20)
                        {
                            setLaserColorClientRpc(1, 1, 1);
                            SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                            buildTimer = 100;
                            currentlyBuilding = "None";
                            creatureConstruct.Stop();
                        }

                    }
                    else
                    {
                        constructionTimeLeft--;
                        Debug.Log("MOAI GREEN: currently building - " + currentlyBuilding);
                        if (constructionTimeLeft <= 0)
                        {
                            switch(currentlyBuilding)
                            {
                                case "Turret":
                                    spawnTurret();
                                    break;
                                case "Mine":
                                    spawnMine();
                                    break;
                                case "Circle":
                                    spawnCircle();
                                    break;
                            }

                            setLaserColorClientRpc(1, 1, 1);
                            currentlyBuilding = "None";
                            buildTimer = 200;
                            playConstructFinishClientRpc();
                            SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                        }
                        facePosition(constructPosition);
                    }
                    break;
                case (int)State.HeadingToEntrance:
                    baseHeadingToEntrance();
                    agent.speed = 4.2f * moaiGlobalSpeed.Value;
                    break;
                case (int)State.Guard:
                    baseGuard();
                    agent.speed = 4.2f * moaiGlobalSpeed.Value;
                    break;
                case (int)State.StickingInFrontOfEnemy:
                    baseStickingInFrontOfEnemy();
                    agent.speed = 4.2f * moaiGlobalSpeed.Value;

                    storedTargetEnemy = ClosestEnemyInRange(28f).gameObject;
                    if(!storedTargetEnemy)
                    {
                        break;
                    }

                    setLaserColorClientRpc(93f / 255f, 24f / 255f, 140f / 255f);

                    // basically the archery state
                    if (Vector3.Distance(this.transform.position, storedTargetEnemy.transform.position) < 19)
                    {
                        agent.speed = 4.2f * moaiGlobalSpeed.Value;
                        StalkPos = generateFleeingPosition();
                        SetDestinationToPosition(StalkPos, false);
                    }
                    else if (Vector3.Distance(this.transform.position, storedTargetEnemy.transform.position) < 22)
                    {
                        agent.speed = 0f;
                    }
                    else
                    {
                        agent.speed = 4.2f * moaiGlobalSpeed.Value;
                    }

                    if (p_burstFire <= 0)
                    {
                        p_awaitBetweenBursts = enemyRandom.Next(20, 24); // 4 second to 5 second await between bursts
                        p_burstTickRate = enemyRandom.Next(0, 2);  // 0.2-0.4 second tickrate
                        p_burstFire = enemyRandom.Next(3, 11); // burst of 3 to 10 plasma balls
                        p_alternatePredict++;
                    }

                    if (p_awaitBetweenBursts <= 0)
                    {
                        p_ticksTillFire--;
                        if (p_ticksTillFire <= 0 && p_burstFire > 0)
                        {
                            firePlasmaProjectile(storedTargetEnemy.transform.position, (p_burstFire * 2) - 1, false, Vector3.zero);
                            p_burstFire--;
                            p_ticksTillFire = p_burstTickRate;
                        }
                    }
                    else
                    {
                        p_awaitBetweenBursts--;
                    }

                    targetPlayer = null;



                    break;
                case (int)State.StickingInFrontOfPlayer:
                    buildTimer = 0;
                    baseStickingInFrontOfPlayer(32f);

                    setLaserColorClientRpc(93f / 255f, 24f / 255f, 140f / 255f);

                    if (targetPlayer != null)
                    {
                        storedTarget = targetPlayer;
                    }
                    else
                    {
                        stamina -= 10;
                    }

                    if(storedTarget == null)
                    {
                        targetPlayer = null;
                        StartSearch(transform.position);
                        SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                    }

                    // basically the archery state
                    if (Vector3.Distance(this.transform.position, storedTarget.transform.position) < 14)
                    {
                        agent.speed = 2f * moaiGlobalSpeed.Value;
                        StalkPos = generateFleeingPosition();
                        SetDestinationToPosition(StalkPos, false);
                    }
                    else if(Vector3.Distance(this.transform.position, storedTarget.transform.position) < 18)
                    {
                        agent.speed = 0f;
                    }
                    else
                    {
                        agent.speed = 4f * moaiGlobalSpeed.Value;
                    }
                    
                    if (p_burstFire <= 0)
                    {
                        p_awaitBetweenBursts = enemyRandom.Next(20, 24); // 1.4 second to 5 second await between bursts
                        p_burstTickRate = enemyRandom.Next(0, 2);  // 0.2-0.4 second tickrate
                        p_burstFire = enemyRandom.Next(3, 11); // burst of 3 to 10 plasma balls
                        p_alternatePredict++;
                    }

                    if (p_awaitBetweenBursts <= 0)
                    {
                        p_ticksTillFire--;
                        if (p_ticksTillFire <= 0 && p_burstFire > 0)
                        {
                            firePlasmaProjectile(storedTarget.transform.position, (p_burstFire*2)-1, true, storedTarget.gameObject.GetComponent<Rigidbody>().velocity);
                            p_burstFire--;
                            p_ticksTillFire = p_burstTickRate;
                        }
                    }
                    else
                    {
                        p_awaitBetweenBursts--;
                    }

                    targetPlayer = null;
                    break;     
                case (int)State.HeadSwingAttackInProgress:
                    baseHeadSwingAttackInProgress();
                    agent.speed = 4.2f * moaiGlobalSpeed.Value;
                    break;
                default:
                    LogDebug("This Behavior State doesn't exist!");
                    break;
            }
        }

        public Vector3 generateFleeingPosition()
        {
            Vector3 directionAwayFromPlayer = (transform.position - targetPlayer.transform.position).normalized;
            Vector3 runAwayPosition = transform.position + directionAwayFromPlayer * 5;

            // Sample the NavMesh to find a valid position
            NavMeshHit hit;
            if (NavMesh.SamplePosition(runAwayPosition, out hit, 15f, NavMesh.AllAreas))
            {
                return hit.position;
            }
            else
            {
                return this.transform.position;
            }
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

        // return vector.zero if the object doesn't fit
        public bool pickConstructPosition()
        {
            int choice = enemyRandom.Next(0, 2);

            Vector3 projectedPosition = Vector3.zero;
            if (choice == 1)
            {
                projectedPosition = laserTarget1.position;
            }
            else
            {
                projectedPosition = laserTarget2.position;
            }



            NavMeshHit hit;
            var spawned = NavMesh.SamplePosition(projectedPosition, out hit, 20, NavMesh.AllAreas);

            if (spawned)
            {
                constructPosition = hit.position;
                constructRotation = Quaternion.FromToRotation(Vector3.up, hit.normal);
                Debug.Log("MOAI GREEN: selected position -> " + constructPosition);
            }
            else
            {
                Debug.Log("MOAI GREEN: construct position select fail. position is still -> " + constructPosition);
            }

            return spawned;
        }

        [ClientRpc]
        public void setLaserColorClientRpc(float r, float g, float b)
        {
            lp1r.GetComponent<LineRenderer>().material.SetColor("_EmissiveColor", new UnityEngine.Color(r, g, b, 1) * 150);
            lp2r.GetComponent<LineRenderer>().material.SetColor("_EmissiveColor", new UnityEngine.Color(r, g, b, 1) * 150);
        }

        [ClientRpc]
        public void setLaserFocusClientRpc(Vector3 pos)
        {
            laserTarget1.position = pos;
            laserTarget2.position = pos;
        }

        public void spawnMine()
        {
            RoundManager m = RoundManager.Instance;
            GameObject gameObject = UnityEngine.Object.Instantiate<GameObject>(landminePrefab, constructPosition, constructRotation);
            gameObject.GetComponent<NetworkObject>().Spawn(true);
            Debug.Log("MOAI GREEN: Spawned Mine at: " + constructPosition.ToString());
            spawnedHazards.Add(gameObject);
        }

        public void spawnTurret()
        {
            turretsLeft--;
            RoundManager m = RoundManager.Instance;
            GameObject gameObject = UnityEngine.Object.Instantiate<GameObject>(turretPrefab, constructPosition, constructRotation);
            gameObject.GetComponent<NetworkObject>().Spawn(true);
            Debug.Log("MOAI GREEN: Spawned Turret at: " + constructPosition.ToString());
            spawnedHazards.Add(gameObject);
        }

        public void spawnCircle()
        {
            if (consumptionCircleMade == null && RoundManager.Instance.IsHost)
            {
                consumptionCircleMade = UnityEngine.Object.Instantiate<GameObject>(Plugin.consumptionCircle, constructPosition, constructRotation);
                consumptionCircleMade.GetComponent<NetworkObject>().Spawn(true);

                bodiesToMake = enemyRandom.Next(3, 15);
                Debug.Log("MOAI GREEN: Spawned Circle at: " + constructPosition.ToString());
                spawnedHazards.Add(consumptionCircleMade);
            }
        }

        public override void HitEnemy(int force = 1, PlayerControllerB playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
        {
            base.HitEnemy(force, playerWhoHit, playHitSFX);
            if (this.isEnemyDead)
            {
                agent.speed = 4;
                stopAllSound();
                stopAllSoundsClientRpc();
                return;
            }

            if (playerWhoHit != null)
            {
                if (currentBehaviourStateIndex != (int)State.StickingInFrontOfPlayer)
                {
                    stopAllSound();
                    stopAllSoundsClientRpc();
                    provokePoints += 5 * force;
                    stamina = 60;
                    recovering = false;
                    buildTimer = 100;

                    targetPlayer = playerWhoHit;
                    storedTarget = targetPlayer;

                    constructionTimeLeft = 0;
                    constructImpatience = 0.5f;
                    currentlyBuilding = "None";
                    constructPosition = Vector3.zero;

                    StopSearch(currentSearch);
                    p_awaitBetweenBursts = enemyRandom.Next(10, 16); // 2 second to 3 second await between bursts
                    p_burstTickRate = enemyRandom.Next(0, 2);  // 0.2-0.4 second tickrate
                    p_burstFire = enemyRandom.Next(3, 11); // burst of 3 to 10 plasma balls
                    p_alternatePredict++;
                    SwitchToBehaviourClientRpc((int)State.StickingInFrontOfPlayer);
                }
            }
        }
    }
}
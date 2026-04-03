using System;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using static MoaiEnemy.Plugin;
using static MoaiEnemy.src.MoaiNormal.MoaiNormalNet;
using System.Collections.Generic;
using System.Reflection;
using static UnityEngine.UIElements.UIR.Implementation.UIRStylePainter;
using MoaiEnemy;
using LethalLib.Modules;

namespace MoaiEnemy.src.MoaiNormal
{

    // MoaiEnemyAI Inherits from MOAIAICORE, which controls all of its basic functions.
    // The red variant will also inherit MOAIAICORE to keep default behavior, and then 'inject' its own behaviors in AI Interval.

    class MoaiEnemyAI : MOAIAICORE
    {
        // vehicle vars
        // current commands include:
        // Untamed: The moai can't be a vehicle. Must be tamed by being an angel that is fed a key or gum gum.
        // Tamed: The moai can serve as a vehicle.
        // Drive: The moai is currently being ridden by the player.
        public String currentCommand = "Untamed";
        public GameObject triggerLinkGameObject;
        public VehicleInteract vehicleController;

        public AudioSource VehicleIgnition;
        public AudioSource moaiInteract;
        public ParticleSystem vehicleSmoke;

        // logical variables
        public float acceleration = 4f;
        public float decceleration = 4.5f;
        public float vehicleSpeed = 20f;
        public float vehicleMaxSpeed;
        public float speedRatio = 0;
        public float vehicleForwardDest = 0.7f;
        public float vehicleHorizontalDest = 0.3f;
        public float speedClamped;
        public Vector3 vehicleInputFeed;  // based on which local player is controlling the moai. Connected by ServerRpc
        public double nextVehicleInputSendTime = 0.0f;
        public Vector3 meshLinkVector = Vector3.zero;
        public bool meshLinkLock = false;
        public float meshLinkThreshold = 5.0f;
        public float meshLinkOuterThreshold = 7.0f;
        public float meshLinkFinishedThreshold = 0.1f;
        public float navigationHeightThreshold = 2f;
        public OffMeshLink[] offMeshLinks;

        public override void Start()
        {
            baseInit();
        }

        public override void Update()
        {
            base.Update();
            baseUpdate();

            if (triggerLinkGameObject && RoundManager.Instance.IsHost)
            {
                if (goodBoy > 0)
                {
                    if (!triggerLinkGameObject.activeInHierarchy) { 
                        triggerLinkEnableClientRpc();
                    }
                }
                else
                {

                    if (triggerLinkGameObject.activeInHierarchy)
                    {
                        triggerLinkDisableClientRpc();
                    }
                }
            }


            // vehicle speed logic
            if (vehicleController && vehicleController.currentlyRiding)
            {
                updateVehicleSpeed();
                animator.speed = 0;
            }

            if (vehicleController)
            {
                if (currentCommand.Equals("Drive"))
                {
                    if (creatureFood.isPlaying) { creatureFood.Stop(); }
                    if (creatureSFX.isPlaying) { creatureSFX.Stop(); }
                    if (creatureEat.isPlaying) { creatureEat.Stop(); }
                    if (creatureEatHuman.isPlaying) { creatureEatHuman.Stop(); }

                    var localPlayer = StartOfRound.Instance.localPlayerController;
                    vehicleController.currentlyRiding.transform.position = mouth.transform.position;
                    if(vehicleController.currentlyRiding == localPlayer && Time.time > nextVehicleInputSendTime)
                    {
                        nextVehicleInputSendTime = Time.time + 0.15f;
                        vehicleInputFeedServerRpc(localPlayer.moveInputVector * 3, vehicleSpeed, speedClamped);
                        if(localPlayer.IsHost)
                        {
                            vehicleInputFeedClientRpc(vehicleSpeed, speedClamped); 
                        }
                    }
                }

                if(goodBoy <= 0 && vehicleController.currentlyRiding != null)
                {
                    vehicleController.detachPlayerToMouthClientRpc(vehicleController.currentlyRiding.NetworkObjectId);
                }
            }

            switch (currentBehaviourStateIndex)
            {
                case (int)State.SearchingForPlayer:
                    break;

                case (int)State.StickingInFrontOfPlayer:
                    if (!isEnemyDead && enemyHP > 0)
                    {
                        thunderTick();
                    }
                    break;
            };
        }

        [ServerRpc(RequireOwnership = false)]
        public void vehicleInputFeedServerRpc(Vector3 movementVector, float _vehicleSpeed, float _speedClamped)
        {
            vehicleInputFeed = movementVector;
            vehicleSpeed = _vehicleSpeed;
            speedClamped = _speedClamped;
        }

        [ClientRpc]
        public void vehicleInputFeedClientRpc(float _vehicleSpeed, float _speedClamped)
        {
            vehicleSpeed = _vehicleSpeed;
            speedClamped = _speedClamped;
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

        public void updateVehicleSpeed()
        {
            var gas = Math.Clamp((vehicleController.currentlyRiding.moveInputVector * 5).y, -1, 1);  // forward input from player

            if (gas < 0)
            {
                vehicleSpeed = Math.Clamp(vehicleSpeed - (decceleration * 3 * Time.deltaTime), 0, vehicleMaxSpeed);
            }
            if (gas < 0.5)
            {
                vehicleSpeed = Math.Clamp(vehicleSpeed - (decceleration * Time.deltaTime), 0, vehicleMaxSpeed);
            }
            else
            {
                vehicleSpeed = Math.Clamp(vehicleSpeed + (acceleration * gas * Time.deltaTime), 0, vehicleMaxSpeed);
            }

            speedClamped = Mathf.Lerp(speedClamped, vehicleSpeed, Time.deltaTime);
        }

        public override void setPitches(float pitchAlter)
        {
            if (moaiInteract != null)
            {
                moaiInteract.pitch /= pitchAlter;
            }
        }

        [ClientRpc]
        public void smokeDisableClientRpc()
        {
            vehicleSmoke.Stop();
        }

        [ClientRpc]
        public void smokeEnableClientRpc()
        {
            vehicleSmoke.Play();
        }

        // handles traversing navMeshLinks when they are encountered.
        public OffMeshLink findValidMeshLink(float distance)
        {
            for(int i = 0; i < offMeshLinks.Length; i++)
            {
                var link = offMeshLinks[i];
                if (Vector3.Distance(transform.position, link.startTransform.position) < distance || Vector3.Distance(transform.position, link.endTransform.position) < distance)
                {
                    return link;
                }
            }

            return null;
        }

        // sample a vehicle vector. Penalize the vector scope every time it fails to meet the height threshold
        // this prevents the moai from picking weird navigation coordinates way below itself.
        // it also prevents yo-yoing between nav mesh links
        public Vector3 sampleVehicleVector(Vector3 targetPos, Vector3 v, Vector3 m, float heightThreshold)
        {
            Plugin.LogDebug("sampleVehicleVector");
            int attempts = 5;
            var baseSample = v + (this.transform.forward * vehicleForwardDest * vehicleSpeed) + (this.transform.right * Math.Sign(m.x) * vehicleHorizontalDest * vehicleSpeed);
            var sample = baseSample;

            int i = 2;
            while ((Math.Abs(sampleHit(sample).y - sample.y) > heightThreshold) && attempts != 0)
            {
                Plugin.LogDebug("Sample = " + sample.ToString() + " - attempt " + (5-attempts));
                sample = v + (this.transform.forward * vehicleForwardDest * vehicleSpeed)/i + ((this.transform.right * Math.Sign(m.x) * vehicleHorizontalDest * vehicleSpeed))/i;
                attempts--;
                i++;
            }

            if(attempts == 0)
            {
                Plugin.LogDebug("Returning Base Sample: " + baseSample.ToString());
                return baseSample;
            }
            else
            {
                Plugin.LogDebug("Returning Sample: " + sample.ToString());
                return sample;
            }
        }

        public Vector3 sampleHit(Vector3 position)
        {
            NavMeshHit hit;
            var result = NavMesh.SamplePosition(position, out hit, 20f, NavMesh.AllAreas);
            if (result)
            {
                return hit.position;
            }
            return Vector3.zero;
        }

        public override void DoAIInterval()
        {
            if (isEnemyDead)
            {
                return;
            };
            base.DoAIInterval();
            baseAIInterval();

            if (vehicleController && vehicleController.currentlyRiding)
            {
                if(offMeshLinks == null)
                {
                    offMeshLinks = UnityEngine.Object.FindObjectsOfType<OffMeshLink>();
                }

                if (offMeshLinks != null && offMeshLinks.Length == 0)
                {
                    offMeshLinks = UnityEngine.Object.FindObjectsOfType<OffMeshLink>();
                }

                agent.speed = vehicleSpeed * moaiGlobalSpeed.Value;
                agent.acceleration = vehicleSpeed * moaiGlobalSpeed.Value;

                // riding logic
                Vector3 v = this.transform.position;
                Vector2 m = vehicleInputFeed * 3;
                Vector3 targetPos = Vector3.zero;
                var meshLink = findValidMeshLink(meshLinkThreshold);

                if (!findValidMeshLink(meshLinkOuterThreshold))
                {
                    meshLinkLock = false;
                }

                if (meshLink && meshLinkVector == Vector3.zero && !meshLinkLock)
                {
                    var link = meshLink;

                    // set targetPos to the farthest position in link
                    if(Vector3.Distance(link.startTransform.position, transform.position) > Vector3.Distance(link.endTransform.position, transform.position))
                    {
                        targetPos = link.startTransform.position;
                    }
                    else
                    {
                        targetPos = link.endTransform.position;
                    }
                    meshLinkVector = targetPos;
                }
                else if(meshLinkVector != Vector3.zero)
                {
                    targetPos = meshLinkVector;
                    if(agent.remainingDistance < meshLinkFinishedThreshold)
                    {
                        meshLinkVector = Vector3.zero;
                        meshLinkLock = true;
                    }
                }
                else
                {
                    targetPos = sampleVehicleVector(targetPos, v, m, navigationHeightThreshold);
                }

                SetDestinationToPosition(targetPos);

                if (!vehicleSmoke.isPlaying)
                {
                    smokeEnableClientRpc();
                }
            }
            else
            {
                meshLinkVector = Vector3.zero;

                // mesh link unlocker #2
                if (meshLinkLock)
                {
                    var meshLink = findValidMeshLink(meshLinkOuterThreshold);
                    if (!meshLink)
                    {
                        meshLinkLock = false;
                    }
                }

                if (vehicleSmoke && vehicleSmoke.isPlaying)
                {
                    smokeDisableClientRpc();
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
                        if(goodBoy > 0 && currentCommand.Equals("Tamed"))
                        {
                            agent.speed = 0;
                        }
                        else
                        {
                            baseGuard();
                        }

                        break;
                    case (int)State.StickingInFrontOfEnemy:
                        baseStickingInFrontOfEnemy();
                        break;
                    case (int)State.StickingInFrontOfPlayer:
                        baseStickingInFrontOfPlayer();
                        break;

                    case (int)State.HeadSwingAttackInProgress:
                        baseHeadSwingAttackInProgress();
                        break;

                    default:
                        LogDebug("This Behavior State doesn't exist!");
                        break;
                }
            }
        }

        public void thunderReset()
        {
            RoundManager m = RoundManager.Instance;

            if (!gameObject.name.Contains("Blue") || isEnemyDead)
            {
                return;
            }

            if (targetPlayer == null || ticksTillThunder > 0)
            {
                return;
            }

            //LogIfDebugBuild("MOAI: spawning LBolt");
            ticksTillThunder = 10 + Math.Min((float)Math.Pow(Vector3.Distance(transform.position, targetPlayer.transform.position), 1.75), 180);
            if(ticksTillThunder < 35) { ticksTillThunder = 35; }
            Vector3 position = serverPosition;
            position.y += (float)(enemyRandom.NextDouble() * ticksTillThunder * 0.2 + 4 * this.gameObject.transform.localScale.x) * Math.Sign(enemyRandom.Next(-100, 100));
            position.x += (float)(enemyRandom.NextDouble() * ticksTillThunder * 0.2 + 4 * this.gameObject.transform.localScale.x) * Math.Sign(enemyRandom.Next(-100, 100));

            GameObject weather = GameObject.Find("TimeAndWeather");

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
            if (striker != null)
            {
                // change to include warning

                if(!striker.activeSelf)
                {
                    enableStrikerClientRpc(true);
                }
                m.LightningStrikeServerRpc(position);
                //m.ShowStaticElectricityWarningClientRpc
            }
            else
            {
                Debug.LogError("Lethal Chaos: Failed to find Stormy Weather container (LBolt)!");
            }
        }

        public void thunderTick()
        {
            if (currentBehaviourStateIndex == (int)State.StickingInFrontOfPlayer)
            {
                ticksTillThunder -= 1;
                if (ticksTillThunder <= 0)
                {
                    thunderReset();
                }
            }
        }

    }
}
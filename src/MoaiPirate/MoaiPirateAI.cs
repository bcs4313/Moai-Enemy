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
            SearchingForPlayer,  // same as normal ai except with a randomized timer before it goes on the ship
            Guard,
            StickingInFrontOfEnemy,
            StickingInFrontOfPlayer,
            HeadSwingAttackInProgress,
            HeadingToEntrance,
            //define custom below
            ShipPatrolling,  // simply patrolling with the ship. MoaiPirateShip code handles most of this. If the ship chose to land (25% chance per ai node), go to searchingforplayer.
            ShipAggressive,  // Spotted a player, flying towards a player, potentially to unload cannon shots or to come down and shoot with its gun
            ShipPlundering,  // stealing an enemy with a grappling hook or a car. Prefers cars
            HeadingToShip  // heading towards the ship
        }

        public override void Start()
        {
            baseInit();

            if (RoundManager.Instance.IsHost)
            {
                NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 20f, NavMesh.AllAreas);
                GameObject GO = Instantiate(Plugin.PirateShip, hit.position, transform.rotation);
                GO.transform.localScale = transform.localScale;
                GO.GetComponent<NetworkObject>().Spawn();
                ship = GO.GetComponent<MoaiPirateShip>();
            }
        }

        bool notifiedClientsOfShip = false;
        public override void Update()
        {
            base.Update();
            baseUpdate();

            if (triggerLinkGameObject && RoundManager.Instance.IsHost)
            {
                if (goodBoy > 0)
                {
                    if (!triggerLinkGameObject.activeInHierarchy)
                    {
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

            switch (currentBehaviourStateIndex)
            {
                case (int)State.SearchingForPlayer:
                    break;

                case (int)State.StickingInFrontOfPlayer:
                    break;
            };

            // notify clients of ship
            if (RoundManager.Instance.IsHost)
            {
                if (ship && ship.NetworkObject && ship.NetworkObject.IsSpawned && notifiedClientsOfShip == false)
                {
                    notifiedClientsOfShip = true;
                    ship.SetCaptainClientRpc(NetworkObjectId);
                    NotifyShipClientRpc(ship.NetworkObjectId);
                }
            }

            // ship boarding
            if(boardedShip)
            {
                transform.position = ship.WheelPoint.transform.position;
                transform.rotation = ship.WheelPoint.transform.rotation;
            }
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


        float timeLeftPatrollingOffShip = 0f;  // timer for how long the moai patrols off the ship
        public static float shipSightRange = 25f;  // how far away a moai can see a player while driving the ship
        public override void DoAIInterval()
        {
            if (isEnemyDead || !RoundManager.Instance.IsHost)
            {
                return;
            };
            base.DoAIInterval();
            baseAIInterval();

            agent.acceleration = 8 * moaiGlobalSpeed.Value;
            switch (currentBehaviourStateIndex)
            {
                case (int)State.SearchingForPlayer:  // patrol state
                    baseSearchingForPlayer();

                    if(timeLeftPatrollingOffShip <= 0)
                    {
                        timeLeftPatrollingOffShip = UnityEngine.Random.Range(5f, 26f);
                        SwitchToBehaviourClientRpc((int)State.HeadingToShip);
                        targetPlayer = null;
                        SetDestinationToPosition(GetWheelDestination());
                        StopSearch(currentSearch);
                        return;
                    }
                    timeLeftPatrollingOffShip -= 0.2f;
                    break;
                case (int)State.HeadingToEntrance:  // heading inside factory
                    SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);  // automatically switch back, this enemy is outside only
                    break;
                case (int)State.Guard:  // angel guard phase
                    if (goodBoy > 0 && currentCommand.Equals("Tamed"))
                    {
                        agent.speed = 0;
                    }
                    else
                    {
                        baseGuard();
                    }
                    break;
                case (int)State.StickingInFrontOfEnemy:  // angel attacking enemy
                    baseStickingInFrontOfEnemy();
                    break;
                case (int)State.StickingInFrontOfPlayer:  // attacking phase
                    baseStickingInFrontOfPlayer();
                    break;
                case (int)State.HeadSwingAttackInProgress:  // eating phase
                    baseHeadSwingAttackInProgress();
                    break;
                case (int)State.HeadingToShip:
                    if (agent.destination == Vector3.zero || !agent.hasPath)
                    {
                        SetDestinationToPosition(GetWheelDestination());
                        try
                        {
                            if (currentSearch != null)
                            {
                                StopSearch(currentSearch);
                            }
                        }
                        catch(Exception e) { Debug.LogError(e); }
                    }
                    
                    // completion case
                    if(agent.remainingDistance <= 2f)
                    {
                        // snap to position
                        SnapToWheelClientRpc(true);
                        SwitchToBehaviourClientRpc((int)State.ShipPatrolling);
                        ship.InitPhaseRising();
                        timeLeftPatrollingOffShip = UnityEngine.Random.Range(15f, 40f); 
                        return;
                    }
                    break;
                case (int)State.ShipPatrolling:
                    // exit condition 1: ship landed
                    if(ship.phase.Equals("landed"))
                    {
                        Debug.Log("Pirate Moai: De ship has completed the trip. Looking for vitums yaaarg");
                        SnapToWheelClientRpc(false);
                        SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                        StartSearch(transform.position);
                        return;
                    }

                    // exit condition 2: spotting a player, begins lowering of the ship
                    if(FoundClosestPlayerInRange(shipSightRange, false))
                    {
                        Debug.Log("Pirate Moai: Lowering de ship. I have spotted ye player yaaarg");
                        if(!ship.phase.Equals("lowering"))
                        {
                            ship.InitPhaseLowering();
                        }
                    }

                    break;
                case (int)State.ShipAggressive:
                    // TO BE IMPLEMENTED
                    break;
                default:
                    LogDebug("This Behavior State doesn't exist!");
                    break;
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
                transform.parent = null;
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
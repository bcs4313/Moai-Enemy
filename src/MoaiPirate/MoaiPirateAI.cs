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
                        triggerLinkEnableClientRpc();
                }
                else
                {
                    if (triggerLinkGameObject.activeInHierarchy)
                        triggerLinkDisableClientRpc();
                }
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

            agent.acceleration = 8 * moaiGlobalSpeed.Value;

            switch (currentBehaviourStateIndex)
            {
                case (int)State.SearchingForPlayer:
                    baseSearchingForPlayer();

                    if (timeLeftPatrollingOffShip <= 0)
                    {
                        timeLeftPatrollingOffShip = UnityEngine.Random.Range(5f, 20f);
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
                    if(ship.isGrappling) { return; }

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
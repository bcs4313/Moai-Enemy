using MoaiEnemy.src.MoaiNormal;
using System;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

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

    internal class MoaiPirateShip : NetworkBehaviour
    {
        public NavMeshAgent agent;
        public String phase = "landed";

        private MoaiPirateAI captain = null;

        // moai attachment points
        // moai can oscillate between these points
        // as he is "looking for treasure"
        public Transform MainDeck;
        public Transform CrowsNest;
        public Transform PoopDeck;
        public Transform Bow;
        public Transform WheelPoint;  // the moai must be here in the traveling phase

        // put ship audio sources here:

        public float yLevel = 0f; // manually controlled Y level, nav agent does not control this.
        public float targetYLevel = 0f;  // ship eases to this y level over time
        public void Update()
        {
            if(captain == null) { return; }
            if(!RoundManager.Instance.IsHost) { return; }  // host only logic

            switch(phase)
            {
                case "landed":
                    if(agent.enabled) { agent.enabled = false; }
                    break;
                case "rising":
                    if (agent.enabled) { agent.enabled = false; }

                    // completion condition: reach target Y level
                    if (Math.Abs(yLevel - targetYLevel) < 1)
                    {
                        InitPhaseTraveling(Vector3.zero);
                    }
                    break;
                case "lowering":
                    if (agent.enabled) { agent.enabled = false; }

                    // completion condition: reach target Y level
                    if (Math.Abs(yLevel - targetYLevel) < 1)
                    {
                        InitPhaseLanded();
                    }
                    break;
                case "traveling":
                    if (!agent.enabled) { agent.enabled = true; }

                    // completion condition: reach dest
                    Vector3 adjustedPos = new Vector3(transform.position.x, 0, transform.position.z);
                    Vector3 adjustedDest = new Vector3(agent.destination.x, 0, agent.destination.z);
                    if (Vector3.Distance(adjustedPos, adjustedDest) < 3)
                    {
                        InitPhaseLowering();
                    }
                    break;
            }

            // Easing of yLevel
            yLevel = Mathf.Lerp(yLevel, targetYLevel, 0.1f);

            // agent based navigation
            if (agent.enabled)
            {
                agent.updatePosition = false;
                Vector3 nextPos = agent.nextPosition;
                nextPos.y = yLevel;
                transform.position = nextPos;

            }
            else
            {
                transform.position = new Vector3(transform.position.x, yLevel, transform.position.z);
            }
        }
        
        public void InitPhaseLanded()
        {
            phase = "landed";
        }

        public void InitPhaseRising()
        {
            phase = "rising";
            targetYLevel = transform.position.y + UnityEngine.Random.Range(30f, 80f);
        }

        public void InitPhaseLowering()
        {
            phase = "lowering";
            RoundManager m = RoundManager.Instance;
            Physics.Raycast(transform.position, Vector3.down, out RaycastHit hitInfo, 500f, LayerMask.GetMask("Default", "Room", "Terrain", "Colliders"));

            if (hitInfo.collider != null)
            {
                targetYLevel = hitInfo.point.y;
            }
            else
            {
                Debug.Log("Moai Pirate Ship: Failed to find a raycast point to land on. Navigating elsewhere...");
                InitPhaseTraveling(Vector3.zero);
            }

        }

        // picks out a random destination from a list of outside AI nodes
        public GameObject FindDestination()
        {
            RoundManager m = RoundManager.Instance;
            GameObject[] outNodes = m.outsideAINodes;
            var selectedNode = outNodes[UnityEngine.Random.Range(0, outNodes.Length)];
            return selectedNode;
        }

        // nav mesh agent will control the travel on the x and z axis, y is ignored
        // if destination is Vector3.zero, the ship picks a random spot
        public void InitPhaseTraveling(Vector3 destination)
        {
            phase = "traveling";
            if(destination == Vector3.zero)
            {
                destination = FindDestination().transform.position;
            }
            agent.SetDestination(destination);
        }

        public void SetCaptain(MoaiPirateAI pirate)
        {
            captain = pirate;
        }
    }
}

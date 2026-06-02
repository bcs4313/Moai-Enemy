using System;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
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

        public void Update()
        {

        }
        
        public void InitPhaseLanded()
        {

        }

        public void InitPhaseRising()
        {

        }

        public void InitPhaseLowering()
        {

        }

        public void InitPhaseTraveling()
        {

        }
    }
}

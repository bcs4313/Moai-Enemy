using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using static MoaiEnemy.Plugin;
using LethalLib.Modules;
using GameNetcodeStuff;
using Unity.Netcode;
using System.Threading.Tasks;

namespace MoaiEnemy.src.MoaiNormal
{
    public class QuantumPad : NetworkBehaviour
    {
        public EnemyAI owner;  // auto assigned

        public AudioSource padChargingSound; // controls the activation of the pad
        public AudioSource padActivatedSound; // controls the activation of the pad
        public GameObject PadParticles;  // pre explosion
        public GameObject PadExplosion;  // post explosion

        // time before the pad goes off, allowing the moai to grab a player
        protected float timer = 10f;

        protected float spawnTime = 0;

        void Start()
        {
            if(!padChargingSound.isPlaying)
            {
                padChargingSound.Play();
            }
            spawnTime = Time.time;
            padActivatedSound.volume *= Plugin.moaiGlobalMusicVol.Value * 1.6f;
            padChargingSound.volume *= Plugin.moaiGlobalMusicVol.Value * 1.6f;
        }

        void FixedUpdate()
        {
            if(!RoundManager.Instance.IsHost)
            {
                return;
            }

            // if we are past a certain point in the sound byte,
            // spawn an explosion and toss any too close players in
            // the moai's grasp (sound plays on awake)
            if((Time.time - spawnTime) > 2.3f)
            {
                // only activate if charging 
                if(PadParticles.activeInHierarchy)
                {
                    padExplosionClientRpc();
                    teleportPlayersInRing();
                    endCycle();
                }
            }
        }

        [ClientRpc]
        public void padExplosionClientRpc()
        {
            PadParticles.SetActive(false);
            PadExplosion.SetActive(true);  // has a stored explosion sound inside
            padActivatedSound.Play();
        }

        public async void endCycle()
        {
            await Task.Delay(4000);
            Destroy(this.gameObject);
        }

        public void teleportPlayersInRing()
        {
            if (!RoundManager.Instance.IsHost)
            {
                return;
            }

            RoundManager m = RoundManager.Instance;
            PlayerControllerB[] players = m.playersManager.allPlayerScripts;
            List<PlayerControllerB> playerOptions = new List<PlayerControllerB>();
            for (int i = 0; i < players.Length; i++)
            {
                PlayerControllerB ply = players[i];
                var p_pos = ply.transform.position;
                var t_pos = transform.position;
                if (Vector3.Distance(p_pos, t_pos) < 8)
                {
                    playerOptions.Add(ply);
                }
            }

            if(playerOptions.Count > 0)
            {
                System.Random r = new System.Random();
                var randomPlayer = playerOptions[r.Next(0, playerOptions.Count)];

                // slight jank here
                PurpleEnemyAI trueOwner = (PurpleEnemyAI)owner;

                if (trueOwner.notHoldingSomething())
                {
                    trueOwner.grabPlayerClientRpc(randomPlayer.NetworkObject.NetworkObjectId);
                }

                Landmine.SpawnExplosion(transform.position, false, 0, 0, 0, 20, null, true);
            }
        }
    }
}
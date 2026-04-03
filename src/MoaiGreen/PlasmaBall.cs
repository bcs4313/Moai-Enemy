using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using LethalLib.Modules;
using GameNetcodeStuff;
using Unity.Netcode;
using System.Numerics;

namespace MoaiEnemy.src.MoaiNormal
{
    public class PlasmaBall : NetworkBehaviour
    {

        public Gradient ballColor;          //Sets the Ball Color (Grandient)
        public Gradient trailColor;         //Sets the Trail Color (Grandient)
        public float speed;                 //Controls the speed of the ball
        [Range(0, 10)]
        public float moveDelay;             //The amount of seconds to wait to move
        public GameObject explosionEffect;  //The effect to play when the ball collides

        private bool canMove = false;       //The move status of the Ball;
        private Animator anim;
        private List<ParticleSystem> ballPS;
        private List<ParticleSystem> trailPS;
        float creationTime = -1;
        float explosionTime = -1;
        public AudioSource laserHit;
        private List<GameObject> explosions;
        public int owner;

        void Start()
        {
            GetRequiredComponents();
            SetParticleColors();

            //Updates the Animation Multiplier to fit the movement speed
            if (anim != null) anim.SetFloat("speedMultiplier", speed);
            Invoke("MovementStatus", moveDelay);

            laserHit.volume = Plugin.moaiGlobalMusicVol.Value;

            creationTime = Time.time;
            explosions = new List<GameObject>();
            if (RoundManager.Instance.IsHost)
            {
                this.GetComponent<Rigidbody>().velocity = transform.forward * speed;
            }

#if UNITY_EDITOR   //This will run only in the Unity Editor (Won't be compiled)                                                                
		RestartParticleSystems (); 	
#endif
        }

        //Controlls the movement of the PlasmaBall. Its in a FixedUpdate because we are using physics.
        void FixedUpdate()
        {
            if (!RoundManager.Instance.IsHost)
            {
                return;
            }

            if (explosionTime != -1 && (Time.time - explosionTime > 3) || (Time.time - creationTime > 30))
            {
                for (int i = 0; i < explosions.Count; i++)
                {
                    Destroy(explosions[i]);
                }
                Destroy(this.gameObject);
            }
        }

        //The logic when a ball collides (as trigger) with a RigidBody.
        void OnCollisionEnter(UnityEngine.Collision collision)
        {
            if (!RoundManager.Instance.IsHost)
            {
                return;
            }

            var hit = collision.gameObject;
            if (collision.gameObject)
            {
                //Plugin.LogDebug("GREEN MOAI PlasmaBall: GO hit -> " + hit.name);
                //Plugin.LogDebug(hit.GetType().ToString());
                spawnExplosionClientRpc();

                var enemyAI = enemyParentWalk(hit);
                if (enemyAI && enemyAI.GetInstanceID() != owner)
                {
                    //Plugin.LogDebug("GREEN MOAI PlasmaBall: enemyAI HIT -- " + enemyAI.name);
                    enemyAI.HitEnemy(1, null, true);
                }

                // player has Cube and PlayerPhysicsBox collisions, both must be accounted for.
                var player = playerParentWalk(hit);
                if (player)
                {
                    //Plugin.LogDebug("GREEN MOAI PlasmaBall: player HIT -- " + player.playerUsername);
                    dmgPlayerClientRpc(player.NetworkObjectId);
                }
            }
            else if (collision.collider)
            {
                spawnExplosionClientRpc();
            }
        }

        [ClientRpc]
        private void dmgPlayerClientRpc(ulong nid)
        {
            PlayerControllerB[] scripts = RoundManager.Instance.playersManager.allPlayerScripts;

            for (int i = 0; i < scripts.Length; i++)
            {
                PlayerControllerB player = scripts[i];
                if (player.NetworkObjectId == nid)
                {
                    player.DamagePlayer(20);
                }
            }
        }

        [ClientRpc]
        void spawnExplosionClientRpc()
        {
            canMove = false;
            explosionTime = Time.time;
            GameObject go = Instantiate(explosionEffect) as GameObject;
            go.transform.position = transform.position;
            if (RoundManager.Instance.IsHost) { explosions.Add(go); }
            laserHit.Play();
        }

        // goes up the parent tree until it finds player or null
        public PlayerControllerB playerParentWalk(GameObject leaf)
        {
            while (leaf != null && leaf.GetComponent<PlayerControllerB>() == null)
            {
                if (leaf.transform.parent && leaf.transform.parent.gameObject)
                {
                    leaf = leaf.transform.parent.gameObject;
                }
                else
                {
                    leaf = null;
                }
            }

            if (leaf && leaf.GetComponent<PlayerControllerB>())
            {
                return leaf.GetComponent<PlayerControllerB>();
            }

            return null;
        }


        // goes up the enemy tree until it finds player or null
        public EnemyAI enemyParentWalk(GameObject leaf)
        {
            while (leaf != null && leaf.GetComponent<EnemyAI>() == null)
            {
                if (leaf.transform.parent && leaf.transform.parent.gameObject)
                {
                    leaf = leaf.transform.parent.gameObject;
                }
                else
                {
                    leaf = null;
                }
            }

            if (leaf && leaf.GetComponent<EnemyAI>())
            {
                return leaf.GetComponent<EnemyAI>();
            }

            return null;
        }

        //Get the components references
        void GetRequiredComponents()
        {

            //Save all the Particle System components references inside the array
            ParticleSystem[] ps = GetComponentsInChildren<ParticleSystem>();
            anim = GetComponentInChildren<Animator>();

            //Custom error to alert the user
            if (ps == null)
            {
                Debug.LogError("Missing Particle System: No Particle System was found inside" + gameObject.ToString());
                return;
            }
            ballPS = new List<ParticleSystem>();    //Create a new list
            trailPS = new List<ParticleSystem>();

            //Divides the particle systems in two Lists. One for the Ball and other for the Trail. 
            for (int i = 0; i < ps.Length; i++)
            {
                if (ps[i].gameObject.name == "Ball") ballPS.Add(ps[i]);
                else if (ps[i].gameObject.name == "Trail") trailPS.Add(ps[i]);
            }
        }

        //Set the same particle Color Over Lifetime (Gradient) to every child Particle System.
        void SetParticleColors()
        {
            for (int i = 0; i < ballPS.Count; i++)
            {
                var psColorOverTime = ballPS[i].colorOverLifetime;
                psColorOverTime.color = new ParticleSystem.MinMaxGradient(ballColor);
            }
            for (int i = 0; i < trailPS.Count; i++)
            {
                var psColorOverTime = trailPS[i].colorOverLifetime;
                psColorOverTime.color = new ParticleSystem.MinMaxGradient(trailColor);
            }
        }

        //Allows the movement on the FixedUpdate
        void MovementStatus()
        {
            canMove = true;
        }

#if UNITY_EDITOR
	[Header("Debug: ")]
	[Range (0f,5f)]
	public float previewEffect; //Debug slider to preview the effect

	void RestartParticleSystems (){
		for (int i = 0; i < ballPS.Count; i++){
			ballPS[i].Simulate (0f,true,true);
			ballPS [i].Play ();
		}
		for (int i = 0; i < trailPS.Count; i++){
			trailPS[i].Simulate (0f,true,true);
			trailPS[i].Play ();
		}
	}

	void OnValidate(){
		GetRequiredComponents ();
		SetParticleColors ();
		for (int i = 0; i < ballPS.Count; i++){
			ballPS[i].Simulate (previewEffect,true,true);
		}
		for (int i = 0; i < trailPS.Count; i++){
			trailPS[i].Simulate (previewEffect,true,true);
		}
	}
#endif
    }
}
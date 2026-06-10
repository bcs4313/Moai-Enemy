using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using LethalLib.Modules;
using GameNetcodeStuff;
using Unity.Netcode;
using System.Numerics;

namespace MoaiEnemy.src.MoaiNormal
{
    public class CannonBall : NetworkBehaviour
    {
        public float speed;                 //Controls the speed of the ball
        [Range(0, 10)]
        public float moveDelay;             //The amount of seconds to wait to move

        private Animator anim;
        float creationTime = -1;
        float explosionTime = -1;

        void Start()
        {
            //Updates the Animation Multiplier to fit the movement speed
            if (anim != null) anim.SetFloat("speedMultiplier", speed);
            Invoke("MovementStatus", moveDelay);

            creationTime = Time.time;
            if (RoundManager.Instance.IsHost)
            {
                this.GetComponent<Rigidbody>().velocity = transform.forward * speed;
            }
        }

        //Controlls the movement of the PlasmaBall. Its in a FixedUpdate because we are using physics.
        void FixedUpdate()
        {
            if (!RoundManager.Instance.IsHost)
            {
                return;
            }

            if (explosionTime != -1 && (Time.time - explosionTime > 3) || (Time.time - creationTime > 10))
            {
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

            if (collision.collider)
            {
                spawnExplosionClientRpc();
            }
        }

        [ClientRpc]
        void spawnExplosionClientRpc()
        {
            // landmine stats: Landmine.SpawnExplosion(base.transform.position + Vector3.up, false, 5.7f, 6f, 50, 0f, null, false);
            // old bird stats: Landmine.SpawnExplosion(explosionPosition - forwardRotation * 0.1f, true, 1f, 7f, 30, 65f, this.explosionPrefab, false);
            Landmine.SpawnExplosion(transform.position, true, 2.5f, 5.7f, 33, 80f);  // 33 dmg, stronger force than old birds. easier to insta kill, harder to partially be hit
            Destroy(this);
        }
    }
}
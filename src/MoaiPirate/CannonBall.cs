using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using LethalLib.Modules;
using GameNetcodeStuff;
using Unity.Netcode;
using MoaiEnemy.src.MoaiPirate;

namespace MoaiEnemy.src.MoaiNormal
{
    public class CannonBall : NetworkBehaviour
    {
        public float speed;
        [Range(0, 10)]
        public float moveDelay;

        float creationTime = -1;
        float explosionTime = -1;
        MoaiPirateAI owner;

        bool moving = false;

        void Start()
        {
            creationTime = Time.time;
            Invoke("StartMoving", moveDelay);
        }
        
        void StartMoving()
        {
            moving = true;
        }

        void Update()
        {
            if (!RoundManager.Instance.IsHost) return;

            if (explosionTime != -1 && (Time.time - explosionTime > 3) || (Time.time - creationTime > 10))
            {
                Destroy(gameObject);
                return;
            }

            if (moving)
            {
                transform.position += transform.forward * speed * Time.deltaTime;
            }
        }

        public void SetOwner(GameObject pirGO)
        {
            owner = pirGO.GetComponent<MoaiPirateAI>();
        }

        void OnCollisionEnter(Collision collision)
        {
            if (!RoundManager.Instance.IsHost) return;

            if (collision.collider && !shipWalk(collision.gameObject))
            {
                explosionTime = Time.time;
                spawnExplosionClientRpc();
            }
        }

        public bool shipWalk(GameObject leaf)
        {
            while (leaf != null && leaf.GetComponent<MoaiPirateShip>() == null && leaf.GetComponent<MoaiPirateAI>() == null)
            {
                if (leaf.transform.parent && leaf.transform.parent.gameObject)
                    leaf = leaf.transform.parent.gameObject;
                else
                    leaf = null;
            }

            if (leaf && leaf.GetComponent<MoaiPirateShip>()) return true;
            if (leaf && leaf.GetComponent<MoaiPirateAI>()) return true;

            if (owner && UnityEngine.Vector3.Distance(owner.transform.position, this.transform.position) <= 6.6f)
                return true;

            return false;
        }

        [ClientRpc]
        void spawnExplosionClientRpc()
        {
            Landmine.SpawnExplosion(transform.position, true, 3.3f, 5.5f, 33, 30f);
            Destroy(gameObject);
        }
    }
}

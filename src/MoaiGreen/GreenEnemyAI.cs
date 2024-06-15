using System;
using System.Collections;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UIElements;
using static MoaiEnemy.Plugin;
using System.Threading.Tasks;
using System.Linq;
using static MoaiEnemy.src.MoaiNormal.MoaiNormalNet;
using LethalLib.Modules;

namespace MoaiEnemy.src.MoaiNormal
{

    class GreenEnemyAI : MOAIAICORE
    {
        // extra audio sources
        public AudioSource creaturePrepare;
        public AudioSource creatureBlitz;
        public AudioSource creatureKidnap;
        public GameObject flameEffect;
        public GameObject swirlEffect;

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

        public override void Start()
        {
            baseInit();
            creatureBlitz.volume = moaiGlobalMusicVol.Value;
            creaturePrepare.volume = moaiGlobalMusicVol.Value;
            creatureKidnap.volume = moaiGlobalMusicVol.Value;
            flameEffect.SetActive(false);
        }

        public override void setPitches(float pitchAlter)
        {
            creatureBlitz.pitch /= pitchAlter;
            creaturePrepare.pitch /= pitchAlter;
            creatureKidnap.pitch /= pitchAlter;
        }

        public override void Update()
        {
            base.Update();
            baseUpdate();

            switch (currentBehaviourStateIndex)
            {
                case (int)State.Preparing:
                    if (!flameEffect.activeInHierarchy) { flameEffect.SetActive(true); }
                    break;
                case (int)State.Blitz:
                    if (!flameEffect.activeInHierarchy) { flameEffect.SetActive(true); }
                    break;
                default:
                    if (flameEffect.activeInHierarchy) { flameEffect.SetActive(false); }
                    break;
             }
        }

        public override void playSoundId(String id)
        {
            switch (id)
            {
                case "creatureBlitz":
                    stopAllSound();
                    creatureBlitz.Play();
                    break;
                case "creaturePrepare":
                    stopAllSound();
                    creaturePrepare.Play();
                    break;
                case "creatureKidnap":
                    stopAllSound();
                    creatureKidnap.Play();
                    break;
            }
        }

        public bool playerIsAlone(PlayerControllerB player)
        {
            RoundManager m = RoundManager.Instance;
            var team = RoundManager.Instance.playersManager.allPlayerScripts;
            for (int i = 0; i < team.Length; i++)
            {
                var p = team[i];
                if(p.playerClientId != player.playerClientId)
                {
                    // test distance
                    if(Vector3.Distance(p.transform.position, player.transform.position) < 30)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        public bool playerOnRock(PlayerControllerB player)
        {
            var slidingSurface = "None";
            var interactRay = new Ray(player.transform.position + Vector3.up, -Vector3.up);
            RaycastHit castHit;
            if (Physics.Raycast(interactRay, out castHit, 6f, StartOfRound.Instance.walkableSurfacesMask, QueryTriggerInteraction.Ignore))
            {
                for (int i = 0; i < StartOfRound.Instance.footstepSurfaces.Length; i++)
                {
                    // go through all surfaces
                    if (castHit.collider.CompareTag(StartOfRound.Instance.footstepSurfaces[i].surfaceTag))
                    {
                        slidingSurface = StartOfRound.Instance.footstepSurfaces[i].surfaceTag;
                    }
                }
            }
            switch (slidingSurface)
            {
                default:
                    return false;
                case "Rock":
                    return true;
                case "Concrete":
                    return true;
            }
        }

        public bool playerIsDefenseless(PlayerControllerB player)
        {
            var items = player.ItemSlots;

            if(player.carryWeight >= 1.38)
            {
                return false;
            }

            for(int i = 0; i < items.Length; i++)
            {
                GrabbableObject item = items[i];
                if (item && item.itemProperties && item.itemProperties.isDefensiveWeapon)
                {
                    return false;
                }
            }
            return true;
        }

        public override void DoAIInterval()
        {
            if (isEnemyDead)
            {
                return;
            };
            base.DoAIInterval();
            baseAIInterval();

            switch (currentBehaviourStateIndex)
            {
                case (int)State.SearchingForPlayer:
                    baseSearchingForPlayer();
                    break;
                case (int)State.HeadingToEntrance:
                    baseHeadingToEntrance();
                    break;
                case (int)State.Guard:
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
                default:
                    LogIfDebugBuild("This Behavior State doesn't exist!");
                    break;
            }
        }

        [ClientRpc]
        public void spawnExplosionClientRpc()
        {
            Landmine.SpawnExplosion(transform.position + UnityEngine.Vector3.up, true, 5.7f, 6.4f);
        }

        [ClientRpc]
        public void attachPlayerClientRpc(ulong playerId, bool healPlayer = false)
        {
            RoundManager m = RoundManager.Instance;
            PlayerControllerB[] players = m.playersManager.allPlayerScripts;
            for(int i = 0; i < players.Length; i++)
            {
                PlayerControllerB player = players[i];
                if (player.NetworkObject.NetworkObjectId == playerId)
                {
                    player.transform.position = playerGrabPoint.position;
                    player.transform.parent = playerGrabPoint;
                    player.playerCollider.enabled = false;
                    if(healPlayer) { player.DamagePlayer(-30); }
                    return;
                }
            }
        }

        [ClientRpc]
        public void toggleSwirlEffectClientRpc(bool value)
        {
            swirlEffect.SetActive(value);
        }

        public override void HitEnemy(int force = 1, PlayerControllerB playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
        {
            base.HitEnemy(force, playerWhoHit, playHitSFX);
            if (this.isEnemyDead)
            {
                return;
            }

            if (playerWhoHit != null)
            {
                provokePoints += 20 * force;
                stamina = 60;
                anger = Math.Max(100, anger + force * 50);
            }
        }
    }
}
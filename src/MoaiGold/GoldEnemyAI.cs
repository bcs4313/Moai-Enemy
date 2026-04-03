using GameNetcodeStuff;
using MoaiEnemy.src.MoaiGold;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using static MoaiEnemy.Plugin;
using static MoaiEnemy.src.MoaiNormal.MoaiNormalNet;

namespace MoaiEnemy.src.MoaiNormal
{

    class GoldEnemyAI : MOAIAICORE
    {
        public string currentCommand = "Guard";
        public string searchStatus = "Searching";
        public GrabbableObject holdingItem = null;
        public PlayerControllerB holdingCorpse = null;
        public GoldInteract interactNode;

        public AudioSource creatureUh;
        public AudioSource creatureInteract;
        public AudioSource creatureTalk;

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
            Scavenging,
            PickingUpItem,
            ReportingToPlayer
        }

        public override void Start()
        {
            baseInit();
            creatureUh.volume = moaiGlobalMusicVol.Value;
            creatureInteract.volume = moaiGlobalMusicVol.Value;
            creatureTalk.volume = moaiGlobalMusicVol.Value;

        }

        public override void setPitches(float pitchAlter)
        {
            creatureUh.pitch /= pitchAlter;
            creatureInteract.pitch /= pitchAlter;
            creatureTalk.pitch /= pitchAlter;
        }

        public override void Update()
        {
            base.Update();
            baseUpdate();

            // death trigger 1
            if(isEnemyDead /*&& (creatureConstruct.isPlaying || creatureFinish.isPlaying) */)
            {
                creatureUh.Stop();
                //creatureConstruct.Stop();
                //creatureFinish.Stop();
            }
        }

        [ClientRpc]
        public void stopAllSoundsClientRpc()
        {
            base.stopAllSound();
            //creatureConstruct.Stop();
            //creatureFinish.Stop();
        }

        public override void DoAIInterval()
        {
            if (provokePoints > 0) { goodBoy = 0; }
            else { goodBoy = 1000; } // friendly unless provoked :)

            base.DoAIInterval();
            baseAIInterval();

            agent.speed = 5f * moaiGlobalSpeed.Value;

            if(currentCommand.Equals("Done") && !creatureUh.isPlaying)
            {
                playUhSoundClientRpc();
            }
            else if(!currentCommand.Equals("Done"))
            {
                stopUhSoundClientRpc();
            }

            // change gold interact tooltip
            if(currentCommand.Equals("Loot"))
            {
                if (!interactNode.triggerLink.hoverTip.Equals("Command to Guard: [LMB]"))
                {
                    updateToolTipClientRpc("Command to Guard: [LMB]");
                }
            }
            else if(currentCommand.Equals("Done"))
            {
                if (!interactNode.triggerLink.hoverTip.Equals("Command to Drop Item [LMB]"))
                {
                    updateToolTipClientRpc("Command to Drop Item [LMB]");
                }
            }
            else
            {
                if (!interactNode.triggerLink.hoverTip.Equals("Command to Loot Factory: [LMB]"))
                {
                    updateToolTipClientRpc("Command to Loot Factory: [LMB]");
                }
            }

            if (currentCommand.Equals("Guard") && isOutside == mostRecentPlayer.isInsideFactory)
            {
                SwitchToBehaviourClientRpc((int)State.HeadingToEntrance);
            }

            switch (currentBehaviourStateIndex)
            {
                case (int)State.SearchingForPlayer:
                    baseSearchingForPlayer();
                    break;
                case (int)State.HeadingToEntrance:
                    if (!creatureVoice.isPlaying)
                    {
                        moaiSoundPlayClientRpc("creatureVoice");
                    }

                    if (searchStatus.Equals("Searching") && isOutside)
                    {
                        baseHeadingToEntrance();
                    }
                    else if (searchStatus.Equals("Returning") && isOutside == mostRecentPlayer.isInsideFactory)
                    {
                        baseHeadingToEntrance();
                    }
                    else if(currentCommand.Equals("Done") && isOutside == mostRecentPlayer.isInsideFactory)
                    {
                        baseHeadingToEntrance();
                    }
                    else if (currentCommand.Equals("Guard") && isOutside == mostRecentPlayer.isInsideFactory)
                    {
                        baseHeadingToEntrance();
                    }
                    else
                    {
                        SwitchToBehaviourClientRpc((int)State.Guard);
                    }
                    break;
                case (int)State.Guard:
                    if(!creatureVoice.isPlaying)
                    {
                        moaiSoundPlayClientRpc("creatureVoice");
                    }

                    if(currentCommand.Equals("Done"))
                    {
                        baseGuard(true);
                    }
                    else
                    {
                        baseGuard(false);
                    }

                    // looter transition
                    if(currentCommand.Equals("Loot"))
                    {
                        if (this.isOutside)
                        {
                            SwitchToBehaviourClientRpc((int)State.HeadingToEntrance);
                        }
                        else
                        {
                            SwitchToBehaviourClientRpc((int)State.Scavenging);
                        }
                    }
                    break;
                case (int)State.Scavenging:
                    if (!creatureVoice.isPlaying)
                    {
                        moaiSoundPlayClientRpc("creatureVoice");
                    }

                    targetPlayer = null;
                    agent.speed = 4f * moaiGlobalSpeed.Value;

                    if (guardTarget == Vector3.zero)
                    {
                        impatience = 0;
                        wait = 10;
                        guardTarget = pickLootSearchNode().transform.position;
                    }

                    SetDestinationToPosition(guardTarget);

                    if (Vector3.Distance(transform.position, guardTarget) < (transform.localScale.magnitude + transform.localScale.magnitude + impatience))
                    {
                        if (wait <= 0)
                        {
                            guardTarget = Vector3.zero;
                        }
                        else
                        {
                            wait--;
                        }
                    }
                    else
                    {
                        impatience += 0.25f;  // low paitence
                    }

                    // invalid path cancellation
                    if(agent.pathStatus == NavMeshPathStatus.PathPartial || agent.pathStatus == NavMeshPathStatus.PathInvalid)
                    {
                        guardTarget = Vector3.zero;
                    }

                    // prevents issues with struggling to reach a destination
                    if (impatience == 10)
                    {
                        guardTarget = Vector3.zero;
                    }

                    // called when the moai finds an item
                    if ((getObj() || corpseAvailable()))
                    {
                        Debug.Log("MOAI: found item to loot");
                        StopSearch(currentSearch);
                        impatience = 0;
                        SwitchToBehaviourClientRpc((int)State.PickingUpItem);
                        return;
                    }

                    // sound switch
                    if (!creatureVoice.isPlaying)
                    {
                        moaiSoundPlayClientRpc("creatureVoice");
                    }

                    // good boy state switch
                    if (goodBoy <= 0)
                    {
                        StartSearch(transform.position);
                        SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                        moaiSetHaloClientRpc(false);
                        return;
                    }

                    // object search and state switch;
                    if (ClosestEnemyInRange(5))
                    {
                        SwitchToBehaviourClientRpc((int)State.StickingInFrontOfEnemy);
                    }
                    break;
                case (int)State.PickingUpItem:
                    eatingScrap = false;
                    eatingTimer = -1;
                    // cancel condition 1
                    if (goodBoy <= 0)
                    {
                        StartSearch(transform.position);
                        SwitchToBehaviourClientRpc((int)State.SearchingForPlayer);
                        moaiSetHaloClientRpc(false);
                        return;
                    }

                    // cancel condition 2
                    if (ClosestEnemyInRange(5))
                    {
                        SwitchToBehaviourClientRpc((int)State.StickingInFrontOfEnemy);
                        return;
                    }

                    // play food sound
                    if (!creatureFood.isPlaying)
                    {
                        //Debug.Log("MSOUND: creatureFood");
                        moaiSoundPlayClientRpc("creatureFood");
                    }

                    // base call for getting an item
                    // bools eatingHuman and eatingScrap indicate if the moai has something.
                    baseHeadSwingAttackInProgress(true);

                    // consumption
                    GrabbableObject obj = getObj();
                    PlayerControllerB ply = getPlayerCorpse();

                    impatience += 1;
                    if (impatience > 100)
                    {
                        unreachableItems.Add(obj);
                    }

                    if (eatingHuman)
                    {
                        searchStatus = "Returning";
                        SwitchToBehaviourClientRpc((int)State.HeadingToEntrance);
                        currentCommand = "Done";
                        creatureEat.Stop();
                        creatureEatHuman.Stop();
                        holdingCorpse = ply;
                        eatingScrap = false;
                        eatingTimer = -1;
                        impatience = 0;
                        return;
                    }

                    if(eatingScrap)
                    {
                        attachItemClientRpc(obj.NetworkObject.NetworkObjectId);
                        searchStatus = "Returning";
                        SwitchToBehaviourClientRpc((int)State.HeadingToEntrance);
                        currentCommand = "Done";
                        creatureEat.Stop();
                        creatureEatHuman.Stop();
                        holdingItem = obj;
                        eatingScrap = false;
                        eatingTimer = -1;
                        impatience = 0;
                        return;
                    }
                    break;
                case (int)State.StickingInFrontOfEnemy:
                    baseStickingInFrontOfEnemy();
                    break;
                case (int)State.StickingInFrontOfPlayer:
                    baseStickingInFrontOfPlayer(32f);
                    break;     
                case (int)State.HeadSwingAttackInProgress:
                    baseHeadSwingAttackInProgress();
                    break;
                default:
                    LogDebug("This Behavior State doesn't exist!");
                    break;
            }
        }

        [ClientRpc]
        public void attachItemClientRpc(ulong netid)
        {
            var items = FindObjectsOfType<GrabbableObject>();
            for (int i = 0; i < items.Length; i++)
            {
                GrabbableObject obj = items[i];

                if (obj != null && obj.name != null && obj.transform != null)
                {
                    if (obj.NetworkObject.NetworkObjectId == netid)
                    {
                        obj.isHeldByEnemy = true;
                        obj.isHeld = true;
                        obj.hasBeenHeld = true;
                        obj.EnablePhysics(false);
                        obj.transform.parent = mouth.transform;
                        obj.transform.localPosition = new Vector3(0, 0, 0);  // center to mouth
                    }
                }
            }
        }

        [ClientRpc]
        public void detachItemClientRpc(ulong netid)
        {
            var items = FindObjectsOfType<GrabbableObject>();
            for (int i = 0; i < items.Length; i++)
            {
                GrabbableObject obj = items[i];

                if (obj != null && obj.name != null && obj.transform != null)
                {
                    if (obj.NetworkObject.NetworkObjectId == netid)
                    {
                        Debug.Log("Detaching item from gold moai: " + obj.name);
                        obj.parentObject = null;
                        obj.transform.parent = null;
                        obj.EnablePhysics(true);
                        obj.startFallingPosition = obj.transform.position;
                        obj.targetFloorPosition = sampleObjFall(obj);
                        obj.DiscardItemFromEnemy();
                        obj.isHeldByEnemy = false;
                        obj.isHeld = false;
                    }
                }
            }
        }

        [ClientRpc]
        public void updateToolTipClientRpc(string tip)
        {
            interactNode.triggerLink.hoverTip = tip;
        }

        public Vector3 sampleObjFall(GrabbableObject objTarget)
        {
            if (!objTarget) { return Vector3.zero; }
            else
            {
                NavMeshHit hit;
                var result = NavMesh.SamplePosition(objTarget.transform.position, out hit, 15f, NavMesh.AllAreas);
                if (result) { return hit.position; }
                else { return Vector3.zero; }
            }
        }

        [ClientRpc]
        public void playUhSoundClientRpc()
        {
            stopAllSound();
            creatureUh.Play();
        }

        [ClientRpc]
        public void stopUhSoundClientRpc()
        {
            creatureUh.Stop();
        }

        public GameObject pickLootSearchNode()
        {
            Debug.Log("MOAIGUARD: Picking Loot Node");
            return allAINodes[UnityEngine.Random.RandomRangeInt(0, allAINodes.Length)];
        }

        public Vector3 generateFleeingPosition()
        {
            Vector3 directionAwayFromPlayer = (transform.position - targetPlayer.transform.position).normalized;
            Vector3 runAwayPosition = transform.position + directionAwayFromPlayer * 5;

            // Sample the NavMesh to find a valid position
            NavMeshHit hit;
            if (NavMesh.SamplePosition(runAwayPosition, out hit, 15f, NavMesh.AllAreas))
            {
                return hit.position;
            }
            else
            {
                return this.transform.position;
            }
        }

        public void facePosition(Vector3 pos)
        {
            Vector3 directionToTarget = pos - transform.position;
            directionToTarget.y = 0f; // Ignore vertical difference

            // If directionToTarget is not zero, rotate to face target
            if (directionToTarget != Vector3.zero)
            {
                // Calculate the rotation to face the target only in the Y-axis (yaw)
                Quaternion targetRotation = Quaternion.LookRotation(directionToTarget);

                // Apply the rotation to the object's transform, preserving current pitch and roll
                transform.rotation = Quaternion.Euler(0f, targetRotation.eulerAngles.y, 0f);
            }
        }

        public override void HitEnemy(int force = 1, PlayerControllerB playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
        {
            base.HitEnemy(force, playerWhoHit, playHitSFX);
            if (this.isEnemyDead)
            {
                return;
            }
            this.enemyHP -= force;

            if (playerWhoHit != null)
            {
                provokePoints += 40 * force;
                targetPlayer = playerWhoHit;
            }
            stamina = 60;
            recovering = false;
            if (base.IsOwner)
            {
                if (this.enemyHP <= 0)
                {
                    base.KillEnemyOnOwnerClient(false);
                    this.stopAllSound();
                    animator.SetInteger("state", 3);
                    isEnemyDead = true;
                    moaiSoundPlayClientRpc("creatureDeath");
                    return;
                }

                moaiSoundPlayClientRpc("creatureHit");
            }
        }
    }
}
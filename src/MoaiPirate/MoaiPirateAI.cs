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

namespace MoaiEnemy.src.MoaiNormal
{

    // MoaiEnemyAI Inherits from MOAIAICORE, which controls all of its basic functions.
    // The red variant will also inherit MOAIAICORE to keep default behavior, and then 'inject' its own behaviors in AI Interval.

    // pirate AI writeup

    class MoaiPirateAI : MOAIAICORE
    {
        public String currentCommand = "Untamed";
        public GameObject triggerLinkGameObject;

        public override void Start()
        {
            baseInit();
        }

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

        public override void DoAIInterval()
        {
            if (isEnemyDead)
            {
                return;
            };
            base.DoAIInterval();
            baseAIInterval();

            agent.acceleration = 8 * moaiGlobalSpeed.Value;
            switch (currentBehaviourStateIndex)
            {
                case (int)State.SearchingForPlayer:
                    baseSearchingForPlayer();
                    break;
                case (int)State.HeadingToEntrance:
                    baseHeadingToEntrance();
                    break;
                case (int)State.Guard:
                    if (goodBoy > 0 && currentCommand.Equals("Tamed"))
                    {
                        agent.speed = 0;
                    }
                    else
                    {
                        baseGuard();
                    }
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
                    LogDebug("This Behavior State doesn't exist!");
                    break;
            }
        }

    }
}
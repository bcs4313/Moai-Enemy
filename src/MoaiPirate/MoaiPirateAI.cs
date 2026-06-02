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
                    if (!isEnemyDead && enemyHP > 0)
                    {
                        thunderTick();
                    }
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

        public void thunderReset()
        {
            RoundManager m = RoundManager.Instance;

            if (!gameObject.name.Contains("Blue") || isEnemyDead)
            {
                return;
            }

            if (targetPlayer == null || ticksTillThunder > 0)
            {
                return;
            }

            ticksTillThunder = 10 + Math.Min((float)Math.Pow(Vector3.Distance(transform.position, targetPlayer.transform.position), 1.75), 180);
            if (ticksTillThunder < 35) { ticksTillThunder = 35; }
            Vector3 position = serverPosition;
            position.y += (float)(enemyRandom.NextDouble() * ticksTillThunder * 0.2 + 4 * this.gameObject.transform.localScale.x) * Math.Sign(enemyRandom.Next(-100, 100));
            position.x += (float)(enemyRandom.NextDouble() * ticksTillThunder * 0.2 + 4 * this.gameObject.transform.localScale.x) * Math.Sign(enemyRandom.Next(-100, 100));

            GameObject weather = GameObject.Find("TimeAndWeather");

            GameObject striker = null;
            for (int i = 0; i < weather.transform.GetChildCount(); i++)
            {
                GameObject g = weather.transform.GetChild(i).gameObject;
                if (g.name.Equals("Stormy"))
                {
                    striker = g;
                }
            }
            if (striker != null)
            {
                if (!striker.activeSelf)
                {
                    enableStrikerClientRpc(true);
                }
                m.LightningStrikeServerRpc(position);
            }
            else
            {
                Debug.LogError("Lethal Chaos: Failed to find Stormy Weather container (LBolt)!");
            }
        }

        public void thunderTick()
        {
            if (currentBehaviourStateIndex == (int)State.StickingInFrontOfPlayer)
            {
                ticksTillThunder -= 1;
                if (ticksTillThunder <= 0)
                {
                    thunderReset();
                }
            }
        }

    }
}
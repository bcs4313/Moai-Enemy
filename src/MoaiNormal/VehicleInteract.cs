using GameNetcodeStuff;
using LethalLib.Modules;
using MoaiEnemy.src.MoaiNormal;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;

namespace MoaiEnemy.src.MoaiNormal
{
    class VehicleInteract : NetworkBehaviour
    {
        public MoaiEnemyAI moai;
        public InteractTrigger triggerLink;
        public PlayerControllerB currentlyRiding;

        public void interactAction(PlayerControllerB playerTarget)
        {
            commandMoaiServerRpc(playerTarget.NetworkObjectId);
        }

        [ServerRpc(RequireOwnership = false)]
        public void commandMoaiServerRpc(ulong playerid)
        {
            if(gameObject.name.Contains("Blue")) { return;  }  // thunder moai don't use this interaction

            // get player from id
            PlayerControllerB targetPlayer = getPlayer(playerid);


            if (targetPlayer == null)
            {
                UnityEngine.Debug.LogError("Moai Enemy: Could not find target player for interaction! Cancelling interaction.");
                return;
            }


            switch (moai.currentCommand)
            {
                case "Untamed":
                    if(yoinkItem(targetPlayer))
                    {
                        changeToTamedClientRpc();
                    }
                    break;
                case "Tamed":
                    attachPlayerToMouthClientRpc(targetPlayer.NetworkObjectId);
                    break;
                case "Drive":
                    detachPlayerToMouthClientRpc(targetPlayer.NetworkObjectId);
                    break;
            }
        }

        [ClientRpc]
        public void changeToTamedClientRpc()
        {
            moai.currentCommand = "Tamed";
            triggerLink.hoverTip = "Drive Moai";
        }

        [ClientRpc]
        public void attachPlayerToMouthClientRpc(ulong playerid)
        {

            // get player from id
            PlayerControllerB targetPlayer = getPlayer(playerid);
            if(targetPlayer)
            {
                currentlyRiding = targetPlayer;
                targetPlayer.transform.parent = moai.mouth;
                moai.currentCommand = "Drive";
                triggerLink.hoverTip = "Dismount Moai";
                targetPlayer.playerCollider.enabled = false;
                moai.VehicleIgnition.Play();
            }
        }

        [ClientRpc]
        public void detachPlayerToMouthClientRpc(ulong playerid)
        {
            // get player from id
            PlayerControllerB targetPlayer = getPlayer(playerid);
            if (targetPlayer)
            {
                currentlyRiding = null;
                targetPlayer.transform.parent = null;
                moai.currentCommand = "Tamed";
                triggerLink.hoverTip = "Drive Moai"; ;
                targetPlayer.playerCollider.enabled = true;
                moai.VehicleIgnition.Stop();
            }
        }

        public PlayerControllerB getPlayer(ulong playerid)
        {

            // get player from id
            var scripts = RoundManager.Instance.playersManager.allPlayerScripts;
            PlayerControllerB targetPlayer = null;
            for (int i = 0; i < scripts.Length; i++)
            {
                var player = scripts[i];
                if (player.NetworkObjectId == playerid)
                {
                    targetPlayer = player;
                }
            }
            return targetPlayer;
        }

        // checks target player to see if they have the target item (key or gum gum)
        // if they do, remove item across clients. Then return true in this method.
        public bool yoinkItem(PlayerControllerB player)
        {
            var inventory = player.ItemSlots;

            GrabbableObject keyHeld = null;

            // prioritize a held object
            foreach(GrabbableObject obj in inventory)
            {
                if (!obj) { continue;  }
                if(!obj.gameObject) { continue; } 
            
                if(obj.gameObject.name.ToLower().Contains("key") || obj.name.ToLower().Contains("key"))
                {
                    if(!keyHeld)
                    {
                        keyHeld = obj;
                    }
                    else if(!obj.isPocketed)
                    {
                        keyHeld = obj;
                    }
                }
            }

            if(keyHeld)
            {
                takeItemClientRpc(player.NetworkObjectId, keyHeld.NetworkObjectId);
                return true;
            }
            return false;
        }

        [ClientRpc]
        public void takeItemClientRpc(ulong playerid, ulong itemid)
        {
            // get player from id
            PlayerControllerB targetPlayer = getPlayer(playerid);

            if (!targetPlayer)
            {
                UnityEngine.Debug.LogError("Moai Enemy: Could not find target player for item removal! Cancelling item removal.");
                return;
            }

            var inventory = targetPlayer.ItemSlots;
            for (int i = 0; i < inventory.Length; i++)
            {
                GrabbableObject obj = inventory[i];
                if (!obj) { continue; }
                if (!obj.gameObject) { continue; }

                if (obj.NetworkObjectId == itemid)
                {
                    if(!obj.isPocketed)
                    {
                        UnityEngine.Debug.Log("Moai Enemy: Despawned Held Object (Key)");
                        targetPlayer.DespawnHeldObject();
                    }
                    else
                    {
                        UnityEngine.Debug.Log("Moai Enemy: Despawned Object in Slot (Key)");
                        targetPlayer.DestroyItemInSlotAndSync(i);
                        HUDManager.Instance.itemSlotIcons[i].enabled = false;
                    }
                    return;
                }
            }

            UnityEngine.Debug.LogError("Moai Enemy: Could not find target item for removal (player was found)! Cancelling item removal.");
        }
    }
}

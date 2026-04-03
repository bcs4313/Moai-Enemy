using MoaiEnemy.src.MoaiNormal;
using System;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;

namespace MoaiEnemy.src.MoaiGold
{
    class GoldInteract : NetworkBehaviour
    {
        public GoldEnemyAI moai;
        public InteractTrigger triggerLink;

        public void interactAction()
        {
            commandMoaiServerRpc();
        }

        [ServerRpc(RequireOwnership = false)]
        public void commandMoaiServerRpc()
        {
            switch (moai.currentCommand)
            {
                case "Guard":
                    moai.searchStatus = "Searching";
                    moai.currentCommand = "Loot";

                    break;
                case "Loot":
                    moai.searchStatus = "Searching";
                    moai.currentCommand = "Guard";
                    moai.SwitchToBehaviourClientRpc((int)GoldEnemyAI.State.Guard);
                    moai.stopAllSoundsClientRpc();
                    break;
                case "Done":
                    moai.searchStatus = "Searching";
                    moai.currentCommand = "Guard";
                    moai.stopAllSoundsClientRpc();
                    moai.SwitchToBehaviourClientRpc((int)GoldEnemyAI.State.Guard);
                    if (moai.holdingItem)
                    {
                        moai.detachItemClientRpc(moai.holdingItem.NetworkObject.NetworkObjectId);
                    }
                    break;
            }
        }
    }
}

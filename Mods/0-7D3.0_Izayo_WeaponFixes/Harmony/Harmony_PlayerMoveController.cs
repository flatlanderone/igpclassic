using HarmonyLib;
using UnityEngine;

[HarmonyPatch(typeof(PlayerMoveController), nameof(PlayerMoveController.Update))]
public static class HideWaterSourceActivatePrompt
{
    private const int WaterBlockTypeId = 240;

    private static void Postfix(PlayerMoveController __instance)
    {
        if (__instance == null || __instance.entityPlayerLocal == null || __instance.playerUI == null)
            return;

        if (!ShouldHideWaterSourcePrompt(__instance))
            return;

        if (string.IsNullOrEmpty(__instance.strTextLabelPointingTo))
            return;

        XUiC_InteractionPrompt.SetText(__instance.playerUI, null);
        __instance.strTextLabelPointingTo = string.Empty;
    }

    private static bool ShouldHideWaterSourcePrompt(PlayerMoveController controller)
    {
        EntityPlayerLocal player = controller.entityPlayerLocal;
        if (!player.IsAlive())
            return false;

        if (!player.IsMoveStateStill())
            return false;

        if (player.IsSwimming() && player.cameraTransform != null && player.cameraTransform.up.y >= 0.7f)
            return false;

        Inventory inventory = player.inventory;
        if (inventory == null || inventory.holdingItem == null || inventory.holdingItemData == null)
            return false;

        if (controller.InteractName != "lblContextActionDrink")
            return false;

        ItemClass heldItem = inventory.holdingItem;
        if (heldItem.Actions == null || heldItem.Actions.Length <= 2)
            return false;

        if (!(heldItem.Actions[2] is ItemActionEat eatAction))
            return false;

        var actionDataList = inventory.holdingItemData.actionData;
        if (actionDataList == null || actionDataList.Count <= 2 || actionDataList[2] == null)
            return false;

        if (eatAction.ConditionBlockTypes == null || !eatAction.ConditionBlockTypes.Contains(WaterBlockTypeId))
            return false;

        return eatAction.CanInteract(actionDataList[2]) == "lblContextActionDrink";
    }
}

using HarmonyLib;
using UnityEngine;

// IZY prefabs show/hide FPV_GROUP and 3rdPV_GROUP from onSelfHoldingItemCreated (see the
// "SHOW HIDE FPV TPV" blocks in the weapon pack items.xml). In 3.2 a 1st/3rd person switch
// ended in Inventory.ForceHoldingItemUpdate(), which rebuilt the held model and re-fired that
// event. In 3.4 EModelBase.SwitchModelAndView ends in EntityAlive.OnAvatarRebuilt() ->
// Hand.Reconcile(), which does nothing when the equipped item is unchanged, so the event never
// fires and the groups keep the state they got at equip time (4 arms in 3rd person, invisible
// gun/hands when going back to 1st). Fire the event ourselves at the same point.
public static class RefireHoldingItemCreatedOnViewSwitch
{
    private static readonly FastTags<TagGroup.Global> IzyTag = FastTags<TagGroup.Global>.Parse("IZY,IZYG,CustomController");   // v1 packs use IZY, v2 packs IZYG / CustomController

    [HarmonyPatch(typeof(EntityAlive), nameof(EntityAlive.OnAvatarRebuilt))]
    private static class Patch_EntityAlive_OnAvatarRebuilt
    {
        private static void Postfix(EntityAlive __instance)
        {
            if (!(__instance is EntityPlayerLocal player) || player.inventory == null)
                return;

            if (player.PlayerUI != null && player.PlayerUI.windowManager.IsWindowOpen("character"))
                return;

            ItemClass itemClass = player.inventory.holdingItem;
            ItemValue itemValue = player.inventory.holdingItemItemValue;
            Transform held = player.inventory.GetHoldingItemTransform();
            if (itemClass == null || itemValue == null || held == null || !itemClass.HasAnyTags(IzyTag))
                return;

            player.MinEventContext.Self = player;
            player.MinEventContext.ItemValue = itemValue;
            player.MinEventContext.Transform = held;
            itemValue.FireEvent(MinEventTypes.onSelfHoldingItemCreated, player.MinEventContext);
        }
    }
}

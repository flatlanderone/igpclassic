using HarmonyLib;

// Switching toolbelt slots goes through PlayerMoveController.swapItem -> EntityPlayerLocal.CancelInventoryActions.
// That coroutine asks the held weapon to cancel its reload and then waits (while IsReloading()) until the reload
// state clears. Vanilla weapon animators clear it as soon as the cancel is requested; a custom weapon model with
// its own reload animation keeps the state set until its animation has played to the end, so the slot switch is
// stuck behind the reload. Cancel the reload right away, with the same call the game uses when the player dies,
// so the switch doesn't have to wait for the custom animation.
public static class CancelReloadOnSlotSwitch
{
    private static readonly FastTags<TagGroup.Global> CustomTags = FastTags<TagGroup.Global>.Parse("IZY,IZYG,CustomController");

    [HarmonyPatch(typeof(PlayerMoveController), "swapItem")]
    private static class Patch_SwapItem
    {
        private static void Prefix(PlayerMoveController __instance)
        {
            EntityPlayerLocal player = __instance.entityPlayerLocal;
            ItemClass held = player?.inventory?.holdingItem;
            if (held == null || !held.HasAnyTags(CustomTags))
                return;

            if (player.IsReloading())
                player.CancelInventoryActionsNow(holsterWeapon: true);
        }
    }
}

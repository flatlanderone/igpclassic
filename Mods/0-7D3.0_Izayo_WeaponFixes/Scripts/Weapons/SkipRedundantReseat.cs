using HarmonyLib;
using UnityEngine;

// In 3.4 Hand.Reconcile() re-seats the held model whenever the toolbelt slot changes, and firing changes the slot
// (ammo is used). The check it uses compares the model's parent with avatarController.GetRightHandTransform(), which
// for the player rig returns the model itself, so it never matches and the model is seated again after every shot.
// Seating calls SetInRightHand, which assigns the weapon's Animator controller again and restarts its state
// machine, so custom weapons (the M2 Browning "host" animation) replay their start-up pose on every shot and drop
// out of view while firing. 3.2 only did this when the held item changed.
//
// Skip a seat that is not part of an equip when the model already sits on the hand joint or on the camera anchor.
public static class SkipRedundantReseat
{
    private static readonly FastTags<TagGroup.Global> CustomTags = FastTags<TagGroup.Global>.Parse("IZY,IZYG,CustomController,IZYCL");

    private static bool inEquip;

    // True when the last SeatModel call was skipped; the other SeatModel postfixes use it to avoid redoing work.
    public static bool LastSeatSkipped { get; private set; }

    [HarmonyPatch(typeof(Hand), nameof(Hand.Equip))]
    private static class Patch_Equip
    {
        private static void Prefix()
        {
            inEquip = true;
        }

        private static void Finalizer()
        {
            inEquip = false;
        }
    }

    [HarmonyPatch(typeof(ItemInventoryData), nameof(ItemInventoryData.SeatModel))]
    private static class Patch_SeatModel
    {
        private static bool Prefix(ItemInventoryData __instance)
        {
            LastSeatSkipped = false;

            if (inEquip || !(__instance.holdingEntity is EntityPlayerLocal))
                return true;

            ItemClass item = __instance.item;
            Transform model = __instance.model;
            if (item == null || model == null || !item.HasAnyTags(CustomTags))
                return true;

            Transform parent = model.parent;
            if (parent == null || (parent.name != "RightWeapon" && parent.name != "IZY_FPVAnchor"))
                return true;

            LastSeatSkipped = true;
            return false;
        }
    }
}

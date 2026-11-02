using HarmonyLib;
using UnityEngine;

namespace IZY
{
    // Re-parents the held IZYCL weapon to an anchor on the camera in first person, so it ignores the vanilla hand
    // movement. 3.2 ran this from Inventory.updateHoldingItem, which 3.4 removed. The 3.4 equivalents are the end of
    // an equip (EntityPlayerLocal.OnHoldingItemChanged, after the item XML has switched FPV_GROUP on) and the end of
    // a view switch (EntityAlive.OnAvatarRebuilt). On a switch the avatar controller has already put the weapon back
    // on the hand, so in third person nothing more is needed.
    public static class AttachWeaponToCamera
    {
        private static readonly FastTags<TagGroup.Global> IzyTag = FastTags<TagGroup.Global>.GetTag("IZYCL");
        private static GameObject anchorObject;

        private static Transform lastMoved;

        // Cheap per-frame check: only does the work when the held IZYCL weapon is no longer under the anchor.
        public static void ApplyIfDetached()
        {
            if (anchorObject == null) return;

            EntityPlayerLocal player = GameManager.Instance?.World?.GetPrimaryPlayer();
            if (player == null || !player.bFirstPersonView) return;

            Transform held = player.inventory?.GetHoldingItemTransform();
            ItemClass heldItem = player.inventory?.holdingItem;
            if (held == null || heldItem == null || !heldItem.HasAnyTags(IzyTag)) return;

            bool anchored = lastMoved != null && lastMoved.parent == anchorObject.transform && held.IsChildOf(lastMoved);
            if (!anchored)
                Apply();
        }

        public static void Apply()
        {
            EntityPlayerLocal player = GameManager.Instance?.World?.GetPrimaryPlayer();
            if (player == null || !player.bFirstPersonView) return;

            Inventory inventory = player.inventory;
            ItemClass heldItem = inventory?.holdingItem;
            Transform held = inventory?.GetHoldingItemTransform();
            Transform cameraTransform = player.cameraTransform;
            if (heldItem == null || held == null || cameraTransform == null) return;

            if (!heldItem.HasAnyTags(IzyTag)) return;

            // FPV_GROUP sits directly under the weapon root; the root is what gets moved. Looked up on the held
            // item itself (including while FPV_GROUP is still inactive) instead of searching the whole scene.
            Transform fpvGroup = GameUtils.FindDeepChild(held, "FPV_GROUP");
            if (fpvGroup == null) return;

            Transform objectToMove = fpvGroup.parent;
            if (objectToMove == null) return;

            if (anchorObject == null)
            {
                anchorObject = GameObject.Find("IZY_FPVAnchor");
                if (anchorObject == null)
                    anchorObject = new GameObject("IZY_FPVAnchor");
            }

            Transform anchor = anchorObject.transform;
            anchor.SetParent(cameraTransform, false);

            lastMoved = objectToMove;
            objectToMove.SetParent(anchor, false);
            objectToMove.localPosition = Vector3.zero;
            objectToMove.localRotation = Quaternion.identity;
        }
    }

    [HarmonyPatch(typeof(EntityPlayerLocal), nameof(EntityPlayerLocal.OnHoldingItemChanged))]
    public class OnHoldingItemChangedPatch
    {
        static void Postfix()
        {
            AttachWeaponToCamera.Apply();
        }
    }

    [HarmonyPatch(typeof(EntityAlive), nameof(EntityAlive.OnAvatarRebuilt))]
    public class OnAvatarRebuiltPatch
    {
        static void Postfix(EntityAlive __instance)
        {
            if (__instance is EntityPlayerLocal)
                AttachWeaponToCamera.Apply();
        }
    }

    // In 3.4 any change to the held stack (firing uses ammo, which marks the toolbelt slot as changed) makes
    // Hand.Reconcile() seat the model on the hand again, which pulls the weapon off the camera anchor. 3.2 re-ran
    // this patch after every held item update. Re-anchor right after each seat.
    [HarmonyPatch(typeof(ItemInventoryData), nameof(ItemInventoryData.SeatModel))]
    public class SeatModelPatch
    {
        static void Postfix(ItemInventoryData __instance)
        {
            if (__instance.holdingEntity is EntityPlayerLocal && !SkipRedundantReseat.LastSeatSkipped)
                AttachWeaponToCamera.Apply();
        }
    }

    // Safety net for any other path that moves the weapon back to the hand while it is held in first person.
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.Update))]
    public class KeepAnchoredPatch
    {
        static void Postfix()
        {
            AttachWeaponToCamera.ApplyIfDetached();
        }
    }
}

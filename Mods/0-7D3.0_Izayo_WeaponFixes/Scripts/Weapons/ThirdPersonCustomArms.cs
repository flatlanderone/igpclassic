using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// In 3rd person the IZY prefab arms (inside FPV_GROUP) hold the weapon correctly, while the base-game arms
// play the vanilla hold pose of the weapon's HoldType (e.g. 67 = junk turret). With the "Custom" option this
// shows FPV_GROUP, hides the weapon's 3rdPV_GROUP guns, and collapses the player's arm skinning so the base
// arms are invisible (the real bones are untouched, so the weapon still follows the hand). Anything that is
// not an IZY weapon, the "Base" option and 1st person all restore the stock look.
public static class ThirdPersonCustomArms
{
    // Always starts on the base arms; the izyarms console command toggles it for the current session.
    public static bool UseCustomArms = false;

    // The XML events fire after SeatModel and again on reload etc., so re-apply a few frames later.
    private static readonly int[] DelaysInFrames = { 1, 5, 30, 90 };
    private static readonly List<int> Pending = new List<int>();

    private static readonly FastTags<TagGroup.Global> IzyTag = FastTags<TagGroup.Global>.Parse("IZY,IZYG,CustomController");   // v1 packs use IZY, v2 packs IZYG / CustomController

    private static readonly Dictionary<SkinnedMeshRenderer, Transform[]> OriginalBones = new Dictionary<SkinnedMeshRenderer, Transform[]>();
    private static Transform rightHider;
    private static Transform leftHider;

    public static void Schedule()
    {
        foreach (int delay in DelaysInFrames)
            Pending.Add(Time.frameCount + delay);
    }

    private static void Apply()
    {
        var player = GameManager.Instance?.World?.GetPrimaryPlayer();
        if (player == null || player.inventory == null) return;

        ItemClass itemClass = player.inventory.holdingItem;
        Transform held = player.inventory.GetHoldingItemTransform();
        Transform root = player.emodel?.avatarController?.GetActiveModelRoot();

        bool isIzy = root != null && held != null && itemClass != null && itemClass.HasAnyTags(IzyTag);
        Transform fpvGroup = isIzy ? FindFirst(held, "FPV_GROUP", "0_FPV") : null;
        Transform tpvGroup = isIzy ? FindFirst(held, "3rdPV_GROUP", "0_3rdPV") : null;
        bool fpv = player.bFirstPersonView;

        if (isIzy && fpv)
        {
            HideRigArms(root);
            RetriggerGloveBuffs(player);
        }

        if (fpvGroup == null || fpv || !UseCustomArms)
        {
            RestoreArms();

            // Put the IZY weapon back to the stock XML state for the current view: FPV_GROUP in 1st person,
            // 3rdPV_GROUP (weapon on the base arms) in 3rd person.
            if (fpvGroup != null) fpvGroup.gameObject.SetActive(fpv);
            if (tpvGroup != null) tpvGroup.gameObject.SetActive(!fpv);
            return;
        }

        fpvGroup.gameObject.SetActive(true);
        if (tpvGroup != null) tpvGroup.gameObject.SetActive(false);

        HideBaseArms(root, held);
    }

    // v1 packs name the groups FPV_GROUP / 3rdPV_GROUP, v2 packs 0_FPV / 0_3rdPV.
    private static Transform FindFirst(Transform held, params string[] names)
    {
        foreach (string name in names)
        {
            Transform t = GameUtils.FindDeepChild(held, name);
            if (t != null) return t;
        }
        return null;
    }

    private static void HideBaseArms(Transform root, Transform held)
    {
        Transform rightArm = GameUtils.FindDeepChild(root, "RightArm");
        Transform leftArm = GameUtils.FindDeepChild(root, "LeftArm");
        if (rightArm == null || leftArm == null) return;

        var rightBones = new HashSet<Transform>(rightArm.GetComponentsInChildren<Transform>(true));
        var leftBones = new HashSet<Transform>(leftArm.GetComponentsInChildren<Transform>(true));

        // A collapsed vertex lands on the hider bone's position, so each hider sits on its own shoulder joint
        // (and follows it); a hider at the model origin drags blended torso vertices down towards the feet.
        if (rightHider == null) rightHider = CreateHider(rightArm);
        if (leftHider == null) leftHider = CreateHider(leftArm);

        foreach (SkinnedMeshRenderer smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (OriginalBones.ContainsKey(smr) || smr.transform.IsChildOf(held)) continue;

            Transform[] bones = smr.bones;
            Transform[] swapped = null;
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null) continue;

                Transform hider = rightBones.Contains(bones[i]) ? rightHider : leftBones.Contains(bones[i]) ? leftHider : null;
                if (hider == null) continue;

                if (swapped == null) swapped = (Transform[])bones.Clone();
                swapped[i] = hider;
            }

            if (swapped == null) continue;
            OriginalBones[smr] = bones;
            smr.bones = swapped;
        }
    }

    // After a 3rd to 1st person switch the first-person rig's "hands" is switched back on a few frames after the
    // equip XML hid it (the first-person arms body animator re-shows its parts), leaving a base-game glove in view
    // next to the IZY prefab arms. Hide the same transforms the IZY item XML hides on equip.
    private static void HideRigArms(Transform root)
    {
        if (root == null) return;

        foreach (string name in RigArmNames)
        {
            Transform part = GameUtils.FindDeepChild(root, name);
            if (part != null && part.gameObject.activeSelf)
                part.gameObject.SetActive(false);
        }
    }

    private static readonly string[] RigArmNames = { "hands_A", "hands", "body" };

    // The custom gloves (Combat Sandbox "Custom Gloves" option) hide the player's own hands and show the glove
    // meshes from IZYGlovesEffect / RefreshHand / WearingArmor*Leather. Their effects only run from the buff's
    // start event and its update tick, so a freshly built held model keeps its default (no gloves) state until
    // the next tick. Fire the start event of the buffs the player already has right now. (Removing and re-adding
    // them does not work: the add is ignored while the remove is still pending, and the buff just disappears.)
    private static void RetriggerGloveBuffs(EntityPlayerLocal player)
    {
        ItemValue held = player.inventory.holdingItemItemValue;
        if (held == null || held.IsEmpty()) return;

        player.MinEventContext.Self = player;
        player.MinEventContext.ItemValue = held;

        foreach (string buffName in GloveBuffs)
        {
            if (BuffManager.GetBuff(buffName) == null) continue;   // option is Off, the buffs are not defined
            if (!player.Buffs.HasBuff(buffName))
                player.Buffs.AddBuff(buffName);
        }

        foreach (string buffName in GloveBuffs.Concat(ArmorGloveBuffs))
        {
            BuffClass buffClass = BuffManager.GetBuff(buffName);
            if (buffClass == null || !player.Buffs.HasBuff(buffName)) continue;

            player.Buffs.FireEvent(MinEventTypes.onSelfBuffStart, buffClass, player.MinEventContext);
        }

        // v2 packs: the IZY_Race_* buffs show the skin-tone arm and hand meshes only from their onSelfBuffUpdate effect,
        // which runs every 0.5 s, so after an equip the weapon appears before its arms. Run that update now.
        foreach (BuffValue buff in player.Buffs.ActiveBuffs.ToList())
        {
            BuffClass buffClass = buff?.BuffClass;
            if (buffClass != null && buffClass.Name.StartsWith("IZY_Race_", StringComparison.OrdinalIgnoreCase))
                player.Buffs.FireEvent(MinEventTypes.onSelfBuffUpdate, buffClass, player.MinEventContext);
        }
    }

    private static readonly string[] GloveBuffs = { "IZYGlovesEffect", "RefreshHand" };
    private static readonly string[] ArmorGloveBuffs = { "WearingArmorBrownLeather", "WearingArmorBlackLeather", "WearingArmorDarkLeather" };

    private static Transform CreateHider(Transform arm)
    {
        Transform hider = new GameObject("IZY_ArmHider").transform;
        hider.SetParent(arm.parent, false);
        hider.localPosition = arm.localPosition;
        hider.localRotation = arm.localRotation;
        hider.localScale = Vector3.zero;
        return hider;
    }

    // Used by the "izyarms" console command.
    public static void SetUseCustomArms(bool value)
    {
        UseCustomArms = value;
        Schedule();
    }

    private static void RestoreArms()
    {
        if (OriginalBones.Count == 0) return;

        foreach (var pair in OriginalBones)
        {
            if (pair.Key != null)
                pair.Key.bones = pair.Value;
        }
        OriginalBones.Clear();
    }

    [HarmonyPatch(typeof(ItemInventoryData), nameof(ItemInventoryData.SeatModel))]
    private static class Patch_SeatModel
    {
        private static void Postfix(ItemInventoryData __instance)
        {
            if (__instance.holdingEntity is EntityPlayerLocal && !SkipRedundantReseat.LastSeatSkipped)
                Schedule();
        }
    }

    // On load the glove buffs are added from buffStatusCheck01 (onSelfEnteredGame) before the held IZY item and the
    // armor buffs exist, so their start effects find nothing to show; they only catch up on a later tick.
    // Keep re-applying for a few seconds after the player spawns.
    private static readonly int[] SpawnDelaysInFrames = { 1, 5, 30, 60, 120, 240, 480 };

    [HarmonyPatch(typeof(EntityPlayerLocal), nameof(EntityPlayerLocal.AfterPlayerRespawn))]
    private static class Patch_AfterPlayerRespawn
    {
        private static void Postfix()
        {
            foreach (int delay in SpawnDelaysInFrames)
                Pending.Add(Time.frameCount + delay);
        }
    }

    [HarmonyPatch(typeof(EntityAlive), nameof(EntityAlive.OnAvatarRebuilt))]
    private static class Patch_OnAvatarRebuilt
    {
        private static void Postfix(EntityAlive __instance)
        {
            if (__instance is EntityPlayerLocal)
                Schedule();
        }
    }

    [HarmonyPatch(typeof(GameManager), nameof(GameManager.Update))]
    private static class Patch_GameManager_Update
    {
        private static void Postfix()
        {
            if (Pending.Count == 0) return;

            bool due = false;
            for (int i = Pending.Count - 1; i >= 0; i--)
            {
                if (Time.frameCount < Pending[i]) continue;
                Pending.RemoveAt(i);
                due = true;
            }
            if (due) Apply();
        }
    }
}

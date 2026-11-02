using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

public static class RestoreCustomWeaponAnimatorController
{
    // Tag from item XML: Tags="IZY,..."
    private static readonly FastTags<TagGroup.Global> IzyTag = FastTags<TagGroup.Global>.Parse("IZY,IZYG,CustomController");   // v1 packs use IZY, v2 packs IZYG / CustomController

    private static bool HoldingItemHasIzyTag(EntityAlive entity)
    {
        var itemClass = entity?.inventory?.holdingItem; // ItemClass
        if (itemClass == null) return false;
        return itemClass.HasAnyTags(IzyTag);
    }

    private static Animator[] FindAllAnimators(Transform root)
    {
        if (root == null) return Array.Empty<Animator>();
        return root.GetComponentsInChildren<Animator>(true) ?? Array.Empty<Animator>();
    }

    private struct CaptureState
    {
        // For every Animator under _transform, store the controller it had in Prefix.
        public List<(int animInstanceId, RuntimeAnimatorController controller)> Captured;
    }

    // If vanilla overwrites after Postfix (or later), we can re-apply once on next frame.
    private static readonly Dictionary<int, List<(int animInstanceId, RuntimeAnimatorController controller)>> PendingReapply
        = new Dictionary<int, List<(int, RuntimeAnimatorController)>>();

    private static CaptureState CaptureControllers(Transform _transform)
    {
        var state = new CaptureState
        {
            Captured = new List<(int, RuntimeAnimatorController)>()
        };

        var anims = FindAllAnimators(_transform);
        for (int i = 0; i < anims.Length; i++)
        {
            var a = anims[i];
            if (a == null) continue;

            state.Captured.Add((a.GetInstanceID(), a.runtimeAnimatorController));
        }

        return state;
    }

    private static void RestoreAllCapturedControllers(Transform heldRoot, CaptureState state)
    {
        if (heldRoot == null) return;
        if (state.Captured == null || state.Captured.Count == 0) return;

        var anims = FindAllAnimators(heldRoot);
        if (anims.Length == 0) return;

        for (int i = 0; i < anims.Length; i++)
        {
            var anim = anims[i];
            if (anim == null) continue;

            // Find matching captured record by animator instance id if possible.
            RuntimeAnimatorController want = null;
            for (int c = 0; c < state.Captured.Count; c++)
            {
                if (state.Captured[c].animInstanceId == anim.GetInstanceID())
                {
                    want = state.Captured[c].controller;
                    break;
                }
            }

            // If instance id changed (rare), fallback by index if safe.
            if (want == null && i < state.Captured.Count)
                want = state.Captured[i].controller;

            // If still null, skip.
            if (want == null) continue;

            if (anim.runtimeAnimatorController == want) continue;

            anim.runtimeAnimatorController = want;
            anim.Rebind();
            anim.Update(0f);
        }
    }

    private static void QueueReapply(Transform _transform, CaptureState state)
    {
        if (_transform == null || state.Captured == null || state.Captured.Count == 0) return;

        var list = new List<(int, RuntimeAnimatorController)>();
        for (int i = 0; i < state.Captured.Count; i++)
            list.Add((state.Captured[i].animInstanceId, state.Captured[i].controller));

        PendingReapply[_transform.GetInstanceID()] = list;
    }

    // --------------------------------------------------------------------
    // AvatarMultiBodyController (FPV + modern avatar setup)
    // --------------------------------------------------------------------
    [HarmonyPatch(typeof(AvatarMultiBodyController), nameof(AvatarMultiBodyController.SetInRightHand))]
    private static class Patch_AvatarMultiBodyController_SetInRightHand
    {
        private static void Prefix(Transform _transform, ref CaptureState __state)
        {
            __state = CaptureControllers(_transform);
        }

        private static void Postfix(AvatarMultiBodyController __instance, Transform _transform, CaptureState __state)
        {
            var ent = __instance?.entity;
            if (ent == null) return;

            if (!HoldingItemHasIzyTag(ent))
                return;

            RestoreAllCapturedControllers(_transform, __state);

            // Queue one re-apply next frame in case vanilla assigns again after Postfix.
            // Key by transform instance id.
            QueueReapply(_transform, __state);
        }
    }

    // --------------------------------------------------------------------
    // LegacyAvatarController (third-person / legacy path)
    // --------------------------------------------------------------------
    [HarmonyPatch(typeof(LegacyAvatarController), nameof(LegacyAvatarController.SetInRightHand))]
    private static class Patch_LegacyAvatarController_SetInRightHand
    {
        private static void Prefix(Transform _transform, ref CaptureState __state)
        {
            __state = CaptureControllers(_transform);
        }

        private static void Postfix(LegacyAvatarController __instance, Transform _transform, CaptureState __state)
        {
            var ent = __instance?.Entity;
            if (ent == null) return;

            if (!HoldingItemHasIzyTag(ent))
                return;

            RestoreAllCapturedControllers(_transform, __state);

            QueueReapply(_transform, __state);
        }
    }

    // --------------------------------------------------------------------
    // One-shot next-frame reapply (covers "vanilla overwrote after Postfix")
    // --------------------------------------------------------------------
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.Update))]
    private static class Patch_GameManager_Update_OneShotReapply
    {
        private static void Postfix()
        {
            if (PendingReapply.Count == 0) return;

            // Copy keys to avoid modifying while iterating.
            var keys = new List<int>(PendingReapply.Keys);

            for (int k = 0; k < keys.Count; k++)
            {
                int rootId = keys[k];
                var capturedList = PendingReapply[rootId];

                // Find live Transform by instance id is not possible directly,
                // so we reapply only to animators we can still find via Resources.
                // (Expensive, but it only runs once per queued reapply.)
                var anims = UnityEngine.Resources.FindObjectsOfTypeAll<Animator>();

                for (int i = 0; i < anims.Length; i++)
                {
                    var a = anims[i];
                    if (a == null) continue;

                    // If the animator belongs to a hierarchy whose root has the instance id, match it.
                    // We walk up parents and compare instance ids.
                    var t = a.transform;
                    while (t != null)
                    {
                        if (t.GetInstanceID() == rootId)
                        {
                            // Find matching captured controller by animator instance id.
                            for (int c = 0; c < capturedList.Count; c++)
                            {
                                if (capturedList[c].animInstanceId == a.GetInstanceID())
                                {
                                    var want = capturedList[c].controller;
                                    if (want != null && a.runtimeAnimatorController != want)
                                    {
                                        a.runtimeAnimatorController = want;
                                        a.Rebind();
                                        a.Update(0f);
                                    }
                                    break;
                                }
                            }
                            break;
                        }

                        t = t.parent;
                    }
                }

                PendingReapply.Remove(rootId);
            }
        }
    }
}

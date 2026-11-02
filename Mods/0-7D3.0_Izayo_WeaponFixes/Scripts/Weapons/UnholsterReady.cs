using HarmonyLib;
using UnityEngine;

// The weapon pack blocks firing for a fixed time after a weapon is equipped: every weapon adds IZYUnholster_Delay
// (1 s), some add IZYUnholster_Delay_Long (1.8 s), and the fire actions require that buff to be absent. The wait
// is the same for every weapon, but each weapon's equip animation has its own length, so a weapon whose animation
// is short makes you wait for nothing.
//
// Keep the pack's timers as the upper limit and end them early: after an equip (or a view switch, which re-fires
// the equip events), watch the weapon's animator and remove the delay buffs as soon as its equip animation is
// over, that is when the state machine has left the (non-looping) equip state for a looping one.
public static class UnholsterReady
{
    private static readonly string[] DelayBuffs = { "IZYUnholster_Delay", "IZYUnholster_Delay_Long" };

    private const float MaxWatchSeconds = 3f;      // longer than the longest pack delay (1.8 s)
    private const int MinFramesBeforeCheck = 2;    // let the animator start the equip state first

    private static bool watching;
    private static int startFrame;
    private static float startTime;

    private static void Begin()
    {
        watching = true;
        startFrame = Time.frameCount;
        startTime = Time.time;
    }

    private static bool AnyDelayBuff(EntityPlayerLocal player)
    {
        foreach (string name in DelayBuffs)
        {
            if (player.Buffs.HasBuff(name))
                return true;
        }
        return false;
    }

    private static bool EquipAnimationFinished(Animator anim)
    {
        if (anim == null || !anim.isActiveAndEnabled || anim.runtimeAnimatorController == null)
            return true;   // nothing to wait for

        if (anim.IsInTransition(0))
            return false;

        AnimatorStateInfo info = anim.GetCurrentAnimatorStateInfo(0);
        return info.loop || info.normalizedTime >= 1f;
    }

    [HarmonyPatch(typeof(Hand), nameof(Hand.Equip))]
    private static class Patch_Equip
    {
        private static void Postfix(Hand __instance)
        {
            Begin();
        }
    }

    // A 3rd/1st person switch re-fires the equip events, which add the delay buff again.
    [HarmonyPatch(typeof(EntityAlive), nameof(EntityAlive.OnAvatarRebuilt))]
    private static class Patch_OnAvatarRebuilt
    {
        private static void Postfix(EntityAlive __instance)
        {
            if (__instance is EntityPlayerLocal)
                Begin();
        }
    }

    [HarmonyPatch(typeof(GameManager), nameof(GameManager.Update))]
    private static class Patch_GameManager_Update
    {
        private static void Postfix()
        {
            if (!watching) return;

            var player = GameManager.Instance?.World?.GetPrimaryPlayer();
            if (player == null || Time.time - startTime > MaxWatchSeconds)
            {
                watching = false;
                return;
            }

            if (Time.frameCount - startFrame < MinFramesBeforeCheck) return;

            if (!AnyDelayBuff(player))
            {
                // Nothing to end. Keep looking for a short while in case the buff is added a frame later.
                if (Time.frameCount - startFrame > 10)
                    watching = false;
                return;
            }

            Transform held = player.inventory?.GetHoldingItemTransform();
            Animator anim = held != null ? held.GetComponentInChildren<Animator>(true) : null;
            if (!EquipAnimationFinished(anim)) return;

            foreach (string name in DelayBuffs)
            {
                if (player.Buffs.HasBuff(name))
                    player.Buffs.RemoveBuff(name);
            }


            watching = false;
        }
    }
}

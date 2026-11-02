using HarmonyLib;

// The pack gives the fire actions of its weapons requirements: the IZYUnholster_Delay buff must be absent, and
// (for most weapons) RoundsInMagazine must be above 0. When a requirement fails, ItemClass.ExecuteAction shows
// "You cannot use that at this time." and plays the denied sound on every click. The weapon should still not fire,
// but the message is not wanted in these two cases: while the unholster delay is running, and with an empty magazine.
public static class SuppressDeniedTooltip
{
    private static readonly string[] DelayBuffs = { "IZYUnholster_Delay", "IZYUnholster_Delay_Long" };

    private static readonly FastTags<TagGroup.Global> CustomTags = FastTags<TagGroup.Global>.Parse("IZY,IZYG,CustomController");

    private const string DeniedTooltipKey = "ttCannotUseAtThisTime";

    private static bool HoldingEmptyCustomRangedWeapon(EntityPlayerLocal player)
    {
        ItemClass held = player.inventory?.holdingItem;
        if (held == null || !held.HasAnyTags(CustomTags) || !(held.Actions[0] is ItemActionRanged))
            return false;

        ItemValue value = player.inventory.holdingItemItemValue;
        return value != null && value.Meta <= 0;
    }

    [HarmonyPatch(typeof(GameManager), nameof(GameManager.ShowTooltip),
        new[] { typeof(EntityPlayerLocal), typeof(string), typeof(string[]), typeof(string), typeof(ToolTipEvent), typeof(bool), typeof(bool), typeof(float) })]
    private static class Patch_ShowTooltip
    {
        // Every ShowTooltip overload ends up in this one, so a single patch covers all callers.
        private static bool Prefix(EntityPlayerLocal _player, string _text)
        {
            if (_player == null || _text == null)
                return true;

            if (_text != DeniedTooltipKey && _text != Localization.Get(DeniedTooltipKey))
                return true;

            foreach (string buff in DelayBuffs)
            {
                if (_player.Buffs.HasBuff(buff))
                    return false;   // still unholstering: no message and no denied sound
            }

            if (HoldingEmptyCustomRangedWeapon(_player))
                return false;       // empty magazine: no message and no denied sound

            return true;
        }
    }
}

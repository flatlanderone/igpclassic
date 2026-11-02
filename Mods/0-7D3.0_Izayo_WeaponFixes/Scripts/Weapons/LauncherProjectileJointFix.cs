using HarmonyLib;

// In 3.4 ItemActionDataLauncher looks up the "ProjectileJoint" transform in its constructor, but the action data
// is built (ItemInventoryData.Build) before the held model exists (BuildModel on equip), so projectileJointT stays
// null. Loaded rockets then parent to the held item root instead of the joint and show up floating in front of the
// launcher. 3.2 built the model first. Look the joint up again whenever a projectile is created.
public static class LauncherProjectileJointFix
{
    [HarmonyPatch(typeof(ItemActionLauncher), nameof(ItemActionLauncher.instantiateProjectile))]
    private static class Patch_InstantiateProjectile
    {
        private static void Prefix(ItemActionData _actionData)
        {
            var data = _actionData as ItemActionLauncher.ItemActionDataLauncher;
            var model = data?.invData?.model;
            if (model == null) return;

            var joint = model.FindInChilds("ProjectileJoint");
            if (joint != null)
                data.projectileJointT = joint;
        }
    }
}

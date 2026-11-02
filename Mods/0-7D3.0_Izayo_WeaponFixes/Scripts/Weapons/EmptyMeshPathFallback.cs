using HarmonyLib;

// Some IZY items declare an empty mesh (for example DropMeshfile value="" on the RB Glock pistols). 3.2 treated a
// mesh that could not be loaded as "use the leather placeholder"; 3.4's ItemClass.CloneModel gets a null handle for
// an empty path and throws a NullReferenceException every tick (dropping such an item spams the log and the item
// never gets a model). Give holdable items with an empty mesh path the same placeholder 3.4 already uses for a
// mesh that fails to load.
public static class EmptyMeshPathFallback
{
    private const string PlaceholderMesh = "@:Other/Items/Crafting/leather.fbx";

    [HarmonyPatch(typeof(ItemClass), "GetMeshfilePath")]
    private static class Patch_GetMeshfilePath
    {
        private static void Postfix(ItemClass __instance, ref string __result)
        {
            if (string.IsNullOrEmpty(__result) && __instance.CanHold())
                __result = PlaceholderMesh;
        }
    }
}

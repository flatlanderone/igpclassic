using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

// The first equip of an IZY weapon has to load its prefab from the big resource bundle. Instantiate each IZY
// weapon in the toolbelt once after spawning and keep the inactive instance parked, so the asset is already
// loaded when the weapon is really equipped. Set ENABLED to false and rebuild to turn it off.
public static class PreloadToolbeltModels
{
    private const bool ENABLED = true;

    // Wait for the world to settle, then spread the loads out one per interval so the preload itself doesn't hitch.
    private const int FirstDelayInFrames = 120;
    private const int FramesBetweenLoads = 30;

    private static readonly FastTags<TagGroup.Global> IzyTag = FastTags<TagGroup.Global>.Parse("IZY,IZYG,CustomController");   // v1 packs use IZY, v2 packs IZYG / CustomController

    private static readonly List<AssetManagement.InstanceHandle<GameObject>> Parked = new List<AssetManagement.InstanceHandle<GameObject>>();
    private static readonly HashSet<int> Preloaded = new HashSet<int>();
    private static readonly Queue<int> Queue = new Queue<int>();
    private static int nextFrame = -1;

    private static void Begin(EntityPlayerLocal player)
    {
        foreach (var handle in Parked)
        {
            if (handle.Instance != null)
                handle.Destroy();
        }
        Parked.Clear();
        Preloaded.Clear();
        Queue.Clear();

        if (!ENABLED || player.inventory == null) return;

        for (int i = 0; i < player.inventory.Length; i++)
        {
            ItemStack stack = player.inventory.GetItem(i);
            ItemClass itemClass = stack?.itemValue?.ItemClass;
            if (itemClass == null || !itemClass.CanHold() || !itemClass.HasAnyTags(IzyTag)) continue;
            if (Preloaded.Add(itemClass.Id))
                Queue.Enqueue(i);
        }

        nextFrame = Time.frameCount + FirstDelayInFrames;
    }

    private static void LoadNext()
    {
        var player = GameManager.Instance?.World?.GetPrimaryPlayer();
        if (player == null || player.inventory == null)
        {
            Queue.Clear();
            return;
        }

        int slot = Queue.Dequeue();
        ItemStack stack = player.inventory.GetItem(slot);
        ItemClass itemClass = stack?.itemValue?.ItemClass;
        if (itemClass != null)
        {
            var handle = itemClass.CloneModel(stack.itemValue, null, BlockShape.MeshPurpose.Hold);
            if (handle.Instance != null)
            {
                handle.Instance.SetActive(false);
                Parked.Add(handle);
            }
        }

        nextFrame = Queue.Count > 0 ? Time.frameCount + FramesBetweenLoads : -1;
    }

    [HarmonyPatch(typeof(EntityPlayerLocal), nameof(EntityPlayerLocal.AfterPlayerRespawn))]
    private static class Patch_AfterPlayerRespawn
    {
        private static void Postfix(EntityPlayerLocal __instance)
        {
            Begin(__instance);
        }
    }

    [HarmonyPatch(typeof(GameManager), nameof(GameManager.Update))]
    private static class Patch_GameManager_Update
    {
        private static void Postfix()
        {
            if (nextFrame < 0 || Queue.Count == 0 || Time.frameCount < nextFrame) return;
            LoadNext();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using UnityEngine;
using HLib = HarmonyLib;

namespace Harmony
{
    public class IzayoUtilsInit : IModApi
    {
        public void InitMod(Mod _modInstance)
        {
            // --- Harmony setup ---
            var asm = Assembly.GetExecutingAssembly();
            var harmonyId = GetType().FullName;
            var harmony = new HLib.Harmony(harmonyId);
            harmony.PatchAll(asm);
        }
    }
}

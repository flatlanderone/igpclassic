using System;
using System.Globalization;
using System.Xml.Linq;
using UnityEngine;
using UnityEngine.Scripting;

// Sets a parameter on an animator from a triggered_effect. Based on Rebirth's AnimatorSetIntSDX, extended to int,
// float, bool and trigger parameters and to the held weapon's own animator, so item modifiers can drive the
// animator of the weapon prefab they are installed on.
//
//   <triggered_effect trigger="onSelfEquipStart" action="AnimatorSetParameter, IzayoUtils"
//                     property="HoldStyle" type="int" value="2"/>
//
//   property  Name of the animator parameter.
//   type      int (default), float, bool or trigger. Animator parameters have no string type.
//   value     A number for int and float, true/false (or 1/0) for bool, nothing for trigger. A value starting with
//             "@" reads that cvar from the target instead, for example value="@myCvar".
//   scope     weapon (default): writes only to the animator on the held weapon model, on this client only. A
//             parameter that animator doesn't have (or has with another type) is skipped quietly.
//             player: goes through the avatar controller, as Rebirth's action did. The controller writes the value
//             into every body animator of the entity (first- and third-person rigs) and also into the held weapon's
//             animator, and sends the change to other players. A body animator without that parameter can log a
//             "parameter does not exist" warning.
//   target    Optional, like any targeted action (default is the entity the event runs on).
//
// The same action can also show or hide parts of the weapon from the value it sets, so a part follows the parameter:
//   transform         Comma separated names of parts to show or hide, for example transform="Scope8x,ScopeRail".
//   parent_transform  Where to look for them. Default #HeldItemRoot, the weapon being held.
//   show_when         When the parts are shown. For a bool: true (default) or false. For an int or float: the value
//                     that shows them. Without it, the parts are shown for any value other than 0 and hidden for 0.
//                     Not used with type="trigger".
// The parts are set whenever this action runs, whether or not the animator has the parameter. They are not watched:
// if the parameter is changed some other way later, the parts only follow the next time this action runs.
[Preserve]
public class MinEventActionAnimatorSetParameter : MinEventActionTargetedBase
{
    private enum ParameterType
    {
        Int,
        Float,
        Bool,
        Trigger
    }

    private enum ParameterScope
    {
        Weapon,
        Player
    }

    private string property;
    private ParameterType type = ParameterType.Int;
    private ParameterScope scope = ParameterScope.Weapon;
    private string rawValue = string.Empty;

    private bool cvarRef;
    private string refCvarName = string.Empty;
    private float value;

    private string[] partNames = Array.Empty<string>();
    private string parentTransform = "#HeldItemRoot";
    private string showWhenRaw = string.Empty;
    private bool showWhenSet;
    private float showWhenValue;

    public override void Execute(MinEventParams _params)
    {
        if (string.IsNullOrEmpty(property)) return;

        for (int i = 0; i < targets.Count; i++)
        {
            EntityAlive target = targets[i];
            if (target == null || target.emodel == null || target.emodel.avatarController == null) continue;

            AvatarController controller = target.emodel.avatarController;
            float v = cvarRef ? target.Buffs.GetCustomVar(refCvarName) : value;

            if (scope == ParameterScope.Player)
                SetOnPlayer(controller, v);
            else
                SetOnWeapon(HeldWeaponAnimator(controller), v);

            if (partNames.Length > 0 && type != ParameterType.Trigger)
                ApplyPartVisibility(target, v);
        }
    }

    private bool PartsShownFor(float v)
    {
        if (!showWhenSet)
            return v != 0f;

        if (type == ParameterType.Bool)
            return (v != 0f) == (showWhenValue != 0f);

        return Mathf.Abs(v - showWhenValue) < 0.0001f;
    }

    private void ApplyPartVisibility(EntityAlive target, float v)
    {
        Transform parent = parentTransform.Equals("#HeldItemRoot", StringComparison.OrdinalIgnoreCase)
            ? target.inventory?.GetHoldingItemTransform()
            : (parentTransform.Length > 0 ? GameUtils.FindDeepChildActive(target.RootTransform, parentTransform) : target.RootTransform);
        if (parent == null) return;

        bool shown = PartsShownFor(v);
        foreach (string name in partNames)
        {
            Transform part = GameUtils.FindDeepChild(parent, name);
            if (part == null) continue;

            part.gameObject.SetActive(shown);
            LightManager.LightChanged(part.position + Origin.position);
        }
    }

    private void SetOnPlayer(AvatarController controller, float v)
    {
        switch (type)
        {
            case ParameterType.Int:
                controller.UpdateInt(property, (int)v, true);
                break;
            case ParameterType.Float:
                controller.UpdateFloat(property, v, true);
                break;
            case ParameterType.Bool:
                controller.UpdateBool(property, v != 0f, true);
                break;
            case ParameterType.Trigger:
                controller.TriggerEvent(property);
                break;
        }
    }

    private static Animator HeldWeaponAnimator(AvatarController controller)
    {
        if (controller is AvatarMultiBodyController multiBody)
            return multiBody.HeldItemAnimator;
        if (controller is LegacyAvatarController legacy)
            return legacy.rightHandAnimator;
        return null;
    }

    private void SetOnWeapon(Animator animator, float v)
    {
        if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null) return;

        AnimatorControllerParameterType wanted = type switch
        {
            ParameterType.Float => AnimatorControllerParameterType.Float,
            ParameterType.Bool => AnimatorControllerParameterType.Bool,
            ParameterType.Trigger => AnimatorControllerParameterType.Trigger,
            _ => AnimatorControllerParameterType.Int
        };

        bool found = false;
        foreach (AnimatorControllerParameter p in animator.parameters)
        {
            if (p.name == property && p.type == wanted)
            {
                found = true;
                break;
            }
        }
        if (!found) return;

        switch (type)
        {
            case ParameterType.Int:
                animator.SetInteger(property, (int)v);
                break;
            case ParameterType.Float:
                animator.SetFloat(property, v);
                break;
            case ParameterType.Bool:
                animator.SetBool(property, v != 0f);
                break;
            case ParameterType.Trigger:
                animator.SetTrigger(property);
                break;
        }
    }

    public override bool ParseXmlAttribute(XAttribute attribute)
    {
        bool handled = base.ParseXmlAttribute(attribute);
        if (handled) return true;

        switch (attribute.Name.LocalName)
        {
            case "property":
                property = attribute.Value;
                return true;
            case "type":
                if (!Enum.TryParse(attribute.Value, true, out type))
                    type = ParameterType.Int;   // unknown type: int
                return true;
            case "scope":
                if (!Enum.TryParse(attribute.Value, true, out scope))
                    scope = ParameterScope.Weapon;   // unknown scope: weapon
                return true;
            case "value":
                rawValue = attribute.Value;
                return true;
            case "transform":
                partNames = attribute.Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < partNames.Length; i++)
                    partNames[i] = partNames[i].Trim();
                return true;
            case "parent_transform":
                parentTransform = attribute.Value.Trim();
                return true;
            case "show_when":
                showWhenRaw = attribute.Value.Trim();
                return true;
        }

        return false;
    }

    // The attributes can come in any order, so the value is interpreted once all of them have been read.
    public override void ParseXMLPostProcess()
    {
        base.ParseXMLPostProcess();

        if (showWhenRaw.Length > 0)
        {
            showWhenSet = true;
            if (type == ParameterType.Bool)
                showWhenValue = bool.TryParse(showWhenRaw, out bool shown) ? (shown ? 1f : 0f)
                    : (StringParsers.ParseFloat(showWhenRaw, 0, -1, NumberStyles.Any) != 0f ? 1f : 0f);
            else
                showWhenValue = StringParsers.ParseFloat(showWhenRaw, 0, -1, NumberStyles.Any);
        }

        if (rawValue.StartsWith("@"))
        {
            cvarRef = true;
            refCvarName = rawValue.Substring(1);
            return;
        }

        if (type == ParameterType.Trigger || rawValue.Length == 0)
            return;

        if (type == ParameterType.Bool)
        {
            if (bool.TryParse(rawValue, out bool b))
                value = b ? 1f : 0f;
            else
                value = StringParsers.ParseFloat(rawValue, 0, -1, NumberStyles.Any) != 0f ? 1f : 0f;
            return;
        }

        value = StringParsers.ParseFloat(rawValue, 0, -1, NumberStyles.Any);
    }
}

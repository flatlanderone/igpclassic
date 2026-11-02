using System.Collections.Generic;
using UnityEngine.Scripting;

[Preserve]
public class ConsoleCmdIzyArms : ConsoleCmdAbstract
{
    [PublicizedFrom(EAccessModifier.Protected)]
    public override string[] getCommands()
    {
        return new string[] { "izyarms" };
    }

    [PublicizedFrom(EAccessModifier.Protected)]
    public override string getDescription()
    {
        return "Toggles third person arms for IZY weapons between base and custom (this session only)";
    }

    [PublicizedFrom(EAccessModifier.Protected)]
    public override string getHelp()
    {
        return "Usage: izyarms\nToggles between the base game arms and the IZY custom arms in third person. Third person always starts on the base arms; the choice lasts for the current session.";
    }

    public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
    {
        ThirdPersonCustomArms.SetUseCustomArms(!ThirdPersonCustomArms.UseCustomArms);

        SingletonMonoBehaviour<SdtdConsole>.Instance.Output(
            "IZY third person arms: " + (ThirdPersonCustomArms.UseCustomArms ? "custom" : "base"));
    }
}

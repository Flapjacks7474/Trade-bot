using UnrealBuildTool;

public class FightForTheSouthTarget : TargetRules
{
	public FightForTheSouthTarget(TargetInfo Target) : base(Target)
	{
		Type = TargetType.Game;
		DefaultBuildSettings = BuildSettingsVersion.Latest;
		IncludeOrderVersion = EngineIncludeOrderVersion.Latest;
		ExtraModuleNames.AddRange(new string[] { "FFTSCore", "FightForTheSouth" });
	}
}

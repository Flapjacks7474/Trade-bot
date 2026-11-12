using UnrealBuildTool;

public class FightForTheSouthEditorTarget : TargetRules
{
	public FightForTheSouthEditorTarget(TargetInfo Target) : base(Target)
	{
		Type = TargetType.Editor;
		DefaultBuildSettings = BuildSettingsVersion.Latest;
		IncludeOrderVersion = EngineIncludeOrderVersion.Latest;
		ExtraModuleNames.AddRange(new string[] { "FFTSCore", "FightForTheSouth" });
	}
}

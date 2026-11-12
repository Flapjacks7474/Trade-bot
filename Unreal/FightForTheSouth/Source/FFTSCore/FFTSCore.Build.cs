using UnrealBuildTool;

// Portable simulation core (plain C++17, no engine types). The same sources build in CoreTests/ with g++/clang.
public class FFTSCore : ModuleRules
{
	public FFTSCore(ReadOnlyTargetRules Target) : base(Target)
	{
		PCHUsage = PCHUsageMode.NoPCHs;
		bUseUnity = false;
		CppStandard = CppStandardVersion.Cpp17;
		PublicDependencyModuleNames.AddRange(new string[] { "Core" });
	}
}

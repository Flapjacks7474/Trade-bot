using UnrealBuildTool;

// Portable simulation core (plain C++, no engine types; builds as C++17 or C++20). The same sources build in CoreTests/ with g++/clang.
public class FFTSCore : ModuleRules
{
	public FFTSCore(ReadOnlyTargetRules Target) : base(Target)
	{
		PCHUsage = PCHUsageMode.NoPCHs;
		bUseUnity = false;
		PublicDependencyModuleNames.AddRange(new string[] { "Core" });
	}
}

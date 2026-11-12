#include "FFTSGameSubsystem.h"
#include "HAL/FileManager.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"

DEFINE_LOG_CATEGORY_STATIC(LogFFTS, Log, All);

static std::vector<std::string> LoadJsonFiles(const FString& Dir, const FString& Prefix = TEXT(""))
{
	TArray<FString> Names;
	IFileManager::Get().FindFiles(Names, *(Dir / TEXT("*.json")), true, false);
	Names.Sort([](const FString& A, const FString& B)
	{
		const bool SA = A == TEXT("street_fighting.json"), SB = B == TEXT("street_fighting.json");
		return SA != SB ? SA : A < B;
	});
	std::vector<std::string> Out;
	for (const FString& Name : Names)
	{
		if (!Prefix.IsEmpty() && !Name.StartsWith(Prefix)) continue;
		FString Text;
		if (FFileHelper::LoadFileToString(Text, *(Dir / Name))) Out.push_back(UFFTSGameSubsystem::ToStd(Text));
	}
	return Out;
}

void UFFTSGameSubsystem::Initialize(FSubsystemCollectionBase& Collection)
{
	Super::Initialize(Collection);
	const FString Data = FPaths::ProjectContentDir() / TEXT("FFTSData");
	std::vector<std::string> Errors;
	const std::vector<std::string> Styles = LoadJsonFiles(Data / TEXT("Moves"));
	const std::vector<std::string> Fighters = LoadJsonFiles(Data / TEXT("Characters"), TEXT("fighters"));
	if (Styles.empty()) Errors.push_back("No style files found in " + ToStd(Data / TEXT("Moves")));
	Db.load(Styles, Fighters, Errors);
	for (const std::string& E : Errors)
	{
		ContentErrors.Add(FromStd(E));
		UE_LOG(LogFFTS, Error, TEXT("Content: %s"), *FromStd(E));
	}
	Loadout = MakeUnique<ffts::StyleLoadout>(Db, &BankState);
	bLoaded = true;
	UE_LOG(LogFFTS, Log, TEXT("FFTS content: %d styles, %d fighters, %d errors"), (int32)Db.styles().size(), (int32)Db.fighters().size(), ContentErrors.Num());
}

void UFFTSGameSubsystem::UnlockAllStylesForDojo()
{
	ffts::Bank Free(MAX_int64 / 4);
	ffts::StyleLoadout Temp(Db, &Free);
	for (const auto& S : Db.styles()) if (!S->isHybrid()) Temp.learn(S->id);
	for (const auto& S : Db.styles()) if (S->isHybrid()) Temp.learn(S->id);
	Loadout->restore(Temp.learned, Loadout->equipped);
}

TArray<FString> UFFTSGameSubsystem::BaseStyleIds() const
{
	TArray<FString> Out;
	for (const auto& S : Db.styles()) if (!S->isHybrid()) Out.Add(FromStd(S->id));
	return Out;
}

FString UFFTSGameSubsystem::StyleName(const FString& Id) const
{
	const ffts::StyleDef* S = Db.style(ToStd(Id));
	return S ? FromStd(S->displayName) : Id;
}

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
	StoryData.load([&Data](const std::string& Rel)
	{
		FString Text;
		FFileHelper::LoadFileToString(Text, *(Data / UTF8_TO_TCHAR(Rel.c_str())));
		return ToStd(Text);
	}, Errors);
	for (const std::string& E : Errors)
	{
		ContentErrors.Add(FromStd(E));
		UE_LOG(LogFFTS, Error, TEXT("Content: %s"), *FromStd(E));
	}
	Session = MakeUnique<ffts::Game>(StoryData);
	Loadout = MakeUnique<ffts::StyleLoadout>(Db, &Session->bank);
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

// ------------------------------------------------------------------------------------------------ story / saves
static constexpr double CmToM = 0.01;

void UFFTSGameSubsystem::Trigger(const FString& TriggerId)
{
	if (Session) Session->trigger(ToStd(TriggerId));
}

void UFFTSGameSubsystem::TickWorld(float DeltaSeconds, FVector PlayerLocation, bool bClockRunning)
{
	if (Session) Session->tick(DeltaSeconds, PlayerLocation.X * CmToM, PlayerLocation.Y * CmToM, bClockRunning);
}

FString UFFTSGameSubsystem::GetClockText() const { return Session ? FromStd(Session->clockText()) : FString(); }

FString UFFTSGameSubsystem::GetMoneyText() const { return Session ? FromStd(ffts::Bank::fmt(Session->bank.total())) : FString(); }

void UFFTSGameSubsystem::GetTrackedMission(FString& Title, FString& Objective) const
{
	Title.Reset(); Objective.Reset();
	if (!Session || Session->missions->tracked.empty()) return;
	const ffts::Json* Def = Session->missions->def(Session->missions->tracked);
	const ffts::MissionProgress* P = Session->missions->progress(Session->missions->tracked);
	if (!Def || !P) return;
	Title = FromStd((*Def)["Title"].str());
	for (const ffts::Json& O : (*Def)["Objectives"].items())
	{
		const std::string& Branch = O["Branch"].str();
		if (O["Optional"].boolean() || P->hasDone(O["Id"].str()) || (!Branch.empty() && Branch != P->branch)) continue;
		Objective = FromStd(O["Text"].str());
		return;
	}
}

TArray<FString> UFFTSGameSubsystem::TakeToasts()
{
	TArray<FString> Out;
	if (Session) for (const std::string& T : Session->takeToasts()) Out.Add(FromStd(T));
	return Out;
}

FString UFFTSGameSubsystem::SlotPath(const FString& Slot) const
{
	return FPaths::ProjectSavedDir() / TEXT("SaveGames") / (FPaths::MakeValidFileName(Slot) + TEXT(".json"));
}

bool UFFTSGameSubsystem::SaveToSlot(const FString& Slot, FVector PlayerLocation)
{
	if (!Session || !Loadout) return false;
	ffts::Json Styles = ffts::Json::makeObject();
	ffts::Json Learned = ffts::Json::makeArray(), Equipped = ffts::Json::makeArray();
	for (const std::string& Id : Loadout->learned) Learned.push(ffts::Json::makeString(Id));
	for (const std::string& Id : Loadout->equipped) Equipped.push(ffts::Json::makeString(Id));
	Styles.set("learned", Learned).set("equipped", Equipped);
	ffts::Json Save = ffts::Json::makeObject();
	Save.set("session", Session->capture(PlayerLocation.X * CmToM, PlayerLocation.Y * CmToM)).set("styles", Styles)
		.set("z", ffts::Json::makeNumber(PlayerLocation.Z));
	const FString Path = SlotPath(Slot);
	// Write to a temp file and move it into place, so a crash mid-write never leaves a half-written save.
	const FString Temp = Path + TEXT(".tmp");
	if (!FFileHelper::SaveStringToFile(FromStd(Save.dump()), *Temp, FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM)) return false;
	const bool bMoved = IFileManager::Get().Move(*Path, *Temp, true);
	UE_LOG(LogFFTS, Log, TEXT("Saved %s: %s"), *Path, bMoved ? TEXT("ok") : TEXT("FAILED"));
	return bMoved;
}

bool UFFTSGameSubsystem::LoadFromSlot(const FString& Slot, FVector& OutPlayerLocation)
{
	if (!Session || !Loadout) return false;
	FString Text;
	if (!FFileHelper::LoadFileToString(Text, *SlotPath(Slot))) return false;
	std::string Err;
	const ffts::Json Save = ffts::Json::parse(ToStd(Text), &Err);
	if (!Err.empty() || !Session->restore(Save["session"]))
	{
		UE_LOG(LogFFTS, Warning, TEXT("Save %s is invalid: %s"), *Slot, *FromStd(Err));
		return false;
	}
	std::vector<std::string> Learned, Equipped;
	for (const ffts::Json& Id : Save["styles"]["learned"].items()) Learned.push_back(Id.str());
	for (const ffts::Json& Id : Save["styles"]["equipped"].items()) Equipped.push_back(Id.str());
	Loadout->restore(Learned, Equipped);
	OutPlayerLocation = FVector(Session->savedX / CmToM, Session->savedZ / CmToM, Save["z"].num());
	return true;
}

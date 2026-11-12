#pragma once

#include "CoreMinimal.h"
#include "Subsystems/GameInstanceSubsystem.h"
#include "FFTSCombat.h"
#include "FFTSGameSubsystem.generated.h"

/**
 * Owns the game's rules for one play session: content (styles, moves, fighters) loaded from Content/FFTSData,
 * FINESS BANK and the player's style loadout. Everything here is the portable FFTSCore simulation; actors only
 * present and drive it.
 */
UCLASS()
class FIGHTFORTHESOUTH_API UFFTSGameSubsystem : public UGameInstanceSubsystem
{
	GENERATED_BODY()

public:
	virtual void Initialize(FSubsystemCollectionBase& Collection) override;

	bool IsContentValid() const { return bLoaded && ContentErrors.Num() == 0; }
	const TArray<FString>& GetContentErrors() const { return ContentErrors; }

	ffts::CombatDatabase& Database() { return Db; }
	ffts::Bank& Bank() { return BankState; }
	ffts::StyleLoadout& Styles() { return *Loadout; }

	/** Dojo mode: learn every style and hybrid so all of them can be tried (no money involved). */
	void UnlockAllStylesForDojo();

	/** Base styles in content order (street first). */
	TArray<FString> BaseStyleIds() const;
	FString StyleName(const FString& Id) const;

	static std::string ToStd(const FString& S) { return std::string(TCHAR_TO_UTF8(*S)); }
	static FString FromStd(const std::string& S) { return FString(UTF8_TO_TCHAR(S.c_str())); }

private:
	bool bLoaded = false;
	TArray<FString> ContentErrors;
	ffts::CombatDatabase Db;
	ffts::Bank BankState = ffts::Bank(4000);  // $40 starting cash, as in the Unity build
	TUniquePtr<ffts::StyleLoadout> Loadout;
};

#pragma once

#include "CoreMinimal.h"
#include "Subsystems/GameInstanceSubsystem.h"
#include "FFTSCombat.h"
#include "FFTSSession.h"
#include "FFTSGameSubsystem.generated.h"

/**
 * Owns the game's rules for one play session: content loaded from Content/FFTSData, the story session (FINESS BANK,
 * missions, dialogue, relationships, NPC schedules) and the player's style loadout, plus JSON save slots in
 * Saved/SaveGames. Everything here is the portable FFTSCore simulation; actors and UI only present and drive it.
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
	ffts::Bank& Bank() { return Session->bank; }
	ffts::StyleLoadout& Styles() { return *Loadout; }
	ffts::Game& GetSession() { return *Session; }   // valid after Initialize (always created, even if content failed to load)
	bool HasSession() const { return Session.IsValid(); }

	// ---- story / UI helpers (Blueprint-callable so widgets can be built without C++)
	/** Reports a world trigger (entered a location, interacted, finished something). */
	UFUNCTION(BlueprintCallable, Category = "FFTS")
	void Trigger(const FString& TriggerId);

	/** Advances the game clock and NPCs. PlayerX/Y are world units (cm); the simulation uses metres. */
	UFUNCTION(BlueprintCallable, Category = "FFTS")
	void TickWorld(float DeltaSeconds, FVector PlayerLocation, bool bClockRunning);

	UFUNCTION(BlueprintPure, Category = "FFTS")
	FString GetClockText() const;

	UFUNCTION(BlueprintPure, Category = "FFTS")
	FString GetMoneyText() const;

	/** Title and first open objective of the tracked main mission ("" when none). */
	UFUNCTION(BlueprintPure, Category = "FFTS")
	void GetTrackedMission(FString& Title, FString& Objective) const;

	/** Messages produced since the last call (mission updates, money, new contacts). */
	UFUNCTION(BlueprintCallable, Category = "FFTS")
	TArray<FString> TakeToasts();

	/** Saves the session and style loadout to Saved/SaveGames/<Slot>.json. */
	UFUNCTION(BlueprintCallable, Category = "FFTS")
	bool SaveToSlot(const FString& Slot, FVector PlayerLocation);

	/** Loads a slot written by SaveToSlot. Returns false (and changes nothing) if the file is missing or invalid. */
	UFUNCTION(BlueprintCallable, Category = "FFTS")
	bool LoadFromSlot(const FString& Slot, FVector& OutPlayerLocation);

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
	ffts::GameData StoryData;
	TUniquePtr<ffts::Game> Session;          // owns the one FINESS BANK ($40 starting cash, as in the Unity build)
	TUniquePtr<ffts::StyleLoadout> Loadout;  // pays for training through Session->bank
	FString SlotPath(const FString& Slot) const;
};

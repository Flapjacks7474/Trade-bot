#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "FFTSCombat.h"
#include "FFTSFightDirector.generated.h"

class AFFTSFighterCharacter;
class ACameraActor;
class UFFTSGameSubsystem;

/** Buttons latched between render frames so a quick tap is never lost between fixed simulation steps. */
struct FFFTSFightButtons
{
	ffts::Command Pressed = ffts::Command::None;
	float ScreenMove = 0.f;   // -1 left .. +1 right on screen
	bool bBlock = false;
};

/**
 * Runs one fight: steps the FFTSCore simulation at a fixed 60 ticks per second, feeds it the player's input and the
 * AI's, places both fighters on the fight line, picks each fighter's clip and time from the simulation state, and
 * drives the side-on fight camera. Port of the Unity FightDirector + FighterAnimator.
 *
 * Dojo controls (also shown on the HUD): Tab / Q cycle your primary / secondary style, V cycles the opponent's style,
 * N next opponent, R restart.
 */
UCLASS()
class FIGHTFORTHESOUTH_API AFFTSFightDirector : public AActor
{
	GENERATED_BODY()

public:
	AFFTSFightDirector();

	void StartFight();
	void NextOpponent();
	void CyclePlayerStyle(bool bSecondary);
	void CycleOpponentStyle();

	void SetButtons(const FFFTSFightButtons& Buttons);

	// ---- read by the HUD
	const ffts::CombatSimulation* Sim() const { return Simulation.Get(); }
	FString PlayerStyleLabel() const;
	FString OpponentStyleLabel() const;
	FString OpponentName() const;
	const TArray<FString>& Feed() const { return EventFeed; }
	FString ContentStatus() const;
	bool HasRig() const;

	UPROPERTY(EditAnywhere, Category = "FFTS")
	FVector FightOrigin = FVector(0, 0, 100);

protected:
	virtual void BeginPlay() override;
	virtual void Tick(float DeltaSeconds) override;

private:
	void StepOnce();
	void HandleEvents();
	void Present(float DeltaSeconds);
	void PresentFighter(AFFTSFighterCharacter* Body, const ffts::Fighter& F);
	void ChooseClip(const ffts::Fighter& F, FString& Key, float& Time, bool& bLoop) const;
	AFFTSFighterCharacter* SpawnFighter(const FLinearColor& Tint);
	void AddFeed(const FString& Line);

	UFFTSGameSubsystem* Game() const;

	TUniquePtr<ffts::CombatSimulation> Simulation;
	TUniquePtr<ffts::FighterAI> Ai;
	ffts::FighterDef PlayerDef, OpponentDef;

	UPROPERTY() TObjectPtr<AFFTSFighterCharacter> PlayerBody;
	UPROPERTY() TObjectPtr<AFFTSFighterCharacter> OpponentBody;
	UPROPERTY() TObjectPtr<ACameraActor> FightCamera;

	FFFTSFightButtons Latched;
	float Accumulator = 0.f;
	int32 OpponentIndex = 1;
	int32 PlayerPrimary = 0, PlayerSecondary = -1, OpponentStyle = -1;
	int32 FightCount = 0;
	TArray<FString> EventFeed;
};

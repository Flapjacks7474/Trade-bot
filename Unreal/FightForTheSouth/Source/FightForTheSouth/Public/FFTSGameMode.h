#pragma once

#include "CoreMinimal.h"
#include "GameFramework/GameModeBase.h"
#include "FFTSGameMode.generated.h"

/**
 * Dojo game mode: if the level is empty it builds a lit fight floor, then spawns the fight director.
 * Works in any map, so a fresh empty level is enough to play.
 */
UCLASS()
class FIGHTFORTHESOUTH_API AFFTSGameMode : public AGameModeBase
{
	GENERATED_BODY()

public:
	AFFTSGameMode();
	virtual void StartPlay() override;

private:
	void EnsureArena();
};

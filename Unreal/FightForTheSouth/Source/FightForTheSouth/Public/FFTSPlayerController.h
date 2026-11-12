#pragma once

#include "CoreMinimal.h"
#include "GameFramework/PlayerController.h"
#include "FFTSPlayerController.generated.h"

class AFFTSFightDirector;

/**
 * Reads keyboard and gamepad every frame and hands the fight director one latched button set per frame.
 * Keyboard: A/D move, Shift hold block, J light, K heavy, L kick, U grapple, Space dodge, I counter, O finisher, T taunt.
 * Gamepad: left stick move, LT block, X light, Y heavy, B kick, RB grapple, A dodge, LB counter, RT finisher, D-pad up taunt.
 * Dojo: Tab / Q cycle your styles, V opponent style, N next opponent, R restart.
 */
UCLASS()
class FIGHTFORTHESOUTH_API AFFTSPlayerController : public APlayerController
{
	GENERATED_BODY()

public:
	virtual void PlayerTick(float DeltaTime) override;
	virtual void BeginPlay() override;

private:
	AFFTSFightDirector* Director();
	TWeakObjectPtr<AFFTSFightDirector> CachedDirector;
};

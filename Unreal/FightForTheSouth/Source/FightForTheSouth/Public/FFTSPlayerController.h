#pragma once

#include "CoreMinimal.h"
#include "GameFramework/PlayerController.h"
#include "FFTSPlayerController.generated.h"

class AFFTSFightDirector;

/**
 * Reads keyboard and gamepad every frame and hands the fight director one latched button set per frame.
 * Keyboard: A/D move, Shift hold block, J light, K heavy, L kick, U grapple, Space dodge, I counter, O finisher, T taunt.
 * Gamepad (Xbox): left stick / D-pad move, LT block, X light, Y heavy, B kick, RB grapple, A dodge, LB counter,
 * RT finisher, D-pad up taunt. Dojo: R3 / L3 cycle your primary / secondary style, D-pad down opponent style,
 * View next opponent, Menu restart. Keyboard Dojo: Tab / Q your styles, V opponent style, N next opponent, R restart.
 * The HUD legend follows whichever device was used last; hits rumble the controller.
 */
UCLASS()
class FIGHTFORTHESOUTH_API AFFTSPlayerController : public APlayerController
{
	GENERATED_BODY()

public:
	virtual void PlayerTick(float DeltaTime) override;
	virtual void BeginPlay() override;

	/** True when the last input came from a gamepad (the HUD shows Xbox buttons then). */
	bool UsingGamepad() const { return bUsingGamepad; }

	/** Controller rumble: bHeavy uses the large (low-frequency) motors, otherwise the small ones. */
	void Rumble(float Intensity, float Duration, bool bHeavy);

private:
	AFFTSFightDirector* Director();
	TWeakObjectPtr<AFFTSFightDirector> CachedDirector;
	void UpdateInputDevice();
	bool bUsingGamepad = false;
};

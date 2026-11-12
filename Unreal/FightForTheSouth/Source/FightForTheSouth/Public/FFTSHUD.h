#pragma once

#include "CoreMinimal.h"
#include "GameFramework/HUD.h"
#include "FFTSHUD.generated.h"

/** Fight HUD drawn on the canvas: health, stamina and momentum bars, styles, event feed, result and controls. */
UCLASS()
class FIGHTFORTHESOUTH_API AFFTSHUD : public AHUD
{
	GENERATED_BODY()

public:
	virtual void DrawHUD() override;

private:
	void Bar(float X, float Y, float W, float H, float Fraction, const FLinearColor& Color, bool bRightToLeft);
	void Text(const FString& S, float X, float Y, const FLinearColor& Color, float Scale = 1.f);
};

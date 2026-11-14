#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Character.h"
#include "FFTSFighterCharacter.generated.h"

class UAnimSequence;
class UMaterialInterface;
class UMaterialInstanceDynamic;
class UStaticMeshComponent;

/**
 * A fighter's body. The fight director positions it and tells it which clip to show at which time; clips are found by
 * the move's AnimationKey ("anim.boxing.jab" -> /Game/FFTS/Anim/anim_boxing_jab), authored at 60 fps with the hit on
 * frame Startup, so on-screen impacts line up with simulation ticks.
 */
UCLASS()
class FIGHTFORTHESOUTH_API AFFTSFighterCharacter : public ACharacter
{
	GENERATED_BODY()

public:
	AFFTSFighterCharacter();

	/** Shows a clip at an exact time (seconds). Missing clips fall back to the style's idle, then the street idle. */
	void ShowClip(const FString& AnimationKey, float TimeSeconds, bool bLoop);

	/** Tints the body so fighters read apart: the rig's slots get a tinted copy of the engine's basic shape material. */
	void SetTint(const FLinearColor& Color);

	/** Brief hit flash. */
	void Flash();

	UPROPERTY(EditAnywhere, Category = "FFTS")
	FString SkeletalMeshPath = TEXT("/Game/FFTS/Characters/FFTS_FightRig.FFTS_FightRig");

	UPROPERTY(EditAnywhere, Category = "FFTS")
	FString AnimFolder = TEXT("/Game/FFTS/Anim");

	/** Unreal's Manny, used instead of the fight rig once Content/Python/ffts_retarget_manny.py has retargeted the clips. */
	UPROPERTY(EditAnywhere, Category = "FFTS")
	bool bPreferManny = true;

	UPROPERTY(EditAnywhere, Category = "FFTS")
	TArray<FString> MannyMeshPaths = {
		TEXT("/Game/Characters/UE5_Mannequins/Meshes/SKM_Manny"),
		TEXT("/Game/Characters/Mannequins/Meshes/SKM_Manny"),
		TEXT("/Game/Characters/Mannequins/Meshes/SKM_Manny_Simple"),
		TEXT("/Game/Characters/UEFN_Mannequin/Meshes/SKM_Manny"),
		TEXT("/Game/Characters/UEFN_Mannequin/Meshes/SKM_UEFN_Mannequin") };

	UPROPERTY(EditAnywhere, Category = "FFTS")
	FString MannyAnimFolder = TEXT("/Game/FFTS/AnimManny");

	/** Yaw that makes the imported mesh face +X (Blender exports face -Y). */
	UPROPERTY(EditAnywhere, Category = "FFTS")
	float MeshYawOffset = -90.f;

	UPROPERTY(VisibleAnywhere, Category = "FFTS")
	TObjectPtr<UStaticMeshComponent> FallbackBody;

	FString CurrentClip() const { return CurrentKey; }
	bool HasRig() const { return bHasRig; }
	bool IsManny() const { return bIsManny; }

protected:
	virtual void BeginPlay() override;
	virtual void Tick(float DeltaSeconds) override;

private:
	UAnimSequence* FindClip(const FString& Key);
	UPROPERTY() TMap<FString, TObjectPtr<UAnimSequence>> ClipCache;
	TSet<FString> MissingClips;
	FString CurrentKey;
	void ApplyBodyColor(const FLinearColor& Color);
	UPROPERTY() TObjectPtr<UMaterialInterface> TintBase;
	UPROPERTY() TArray<TObjectPtr<UMaterialInstanceDynamic>> BodyMaterials;
	FLinearColor TintColor = FLinearColor::White;
	bool bHasRig = false;
	bool bIsManny = false;
	float FlashTime = 0.f;
};

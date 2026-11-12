#include "FFTSFighterCharacter.h"
#include "Animation/AnimSequence.h"
#include "Animation/AnimSingleNodeInstance.h"
#include "Components/CapsuleComponent.h"
#include "Components/SkeletalMeshComponent.h"
#include "Components/StaticMeshComponent.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/StaticMesh.h"
#include "GameFramework/CharacterMovementComponent.h"
#include "Materials/MaterialInstanceDynamic.h"
#include "UObject/ConstructorHelpers.h"

AFFTSFighterCharacter::AFFTSFighterCharacter()
{
	PrimaryActorTick.bCanEverTick = true;
	GetCapsuleComponent()->InitCapsuleSize(34.f, 92.f);
	GetCharacterMovement()->bOrientRotationToMovement = false;
	bUseControllerRotationYaw = false;

	GetMesh()->SetRelativeLocation(FVector(0, 0, -92.f));
	GetMesh()->SetRelativeRotation(FRotator(0, MeshYawOffset, 0));
	GetMesh()->VisibilityBasedAnimTickOption = EVisibilityBasedAnimTickOption::AlwaysTickPoseAndRefreshBones;

	// Shown only until the fight rig is imported (see Content/Python/ffts_import_fight_rig.py).
	FallbackBody = CreateDefaultSubobject<UStaticMeshComponent>(TEXT("FallbackBody"));
	FallbackBody->SetupAttachment(GetCapsuleComponent());
	FallbackBody->SetCollisionEnabled(ECollisionEnabled::NoCollision);
	static ConstructorHelpers::FObjectFinder<UStaticMesh> Cyl(TEXT("/Engine/BasicShapes/Cylinder.Cylinder"));
	if (Cyl.Succeeded())
	{
		FallbackBody->SetStaticMesh(Cyl.Object);
		FallbackBody->SetRelativeScale3D(FVector(0.6f, 0.6f, 1.84f));
	}
}

void AFFTSFighterCharacter::BeginPlay()
{
	Super::BeginPlay();
	if (USkeletalMesh* RigMesh = LoadObject<USkeletalMesh>(nullptr, *SkeletalMeshPath))
	{
		GetMesh()->SetSkeletalMesh(RigMesh);
		GetMesh()->SetRelativeRotation(FRotator(0, MeshYawOffset, 0));
		GetMesh()->SetAnimationMode(EAnimationMode::AnimationSingleNode);
		bHasRig = true;
		FallbackBody->SetVisibility(false);
	}
}

UAnimSequence* AFFTSFighterCharacter::FindClip(const FString& Key)
{
	if (TObjectPtr<UAnimSequence>* Found = ClipCache.Find(Key)) return *Found;
	if (MissingClips.Contains(Key)) return nullptr;
	const FString Asset = Key.Replace(TEXT("."), TEXT("_"));
	UAnimSequence* Seq = LoadObject<UAnimSequence>(nullptr, *FString::Printf(TEXT("%s/%s.%s"), *AnimFolder, *Asset, *Asset), nullptr, LOAD_NoWarn | LOAD_Quiet);
	if (Seq) ClipCache.Add(Key, Seq); else MissingClips.Add(Key);
	return Seq;
}

void AFFTSFighterCharacter::ShowClip(const FString& Key, float TimeSeconds, bool bLoop)
{
	if (!bHasRig) return;
	UAnimSequence* Seq = FindClip(Key);
	FString UsedKey = Key;
	if (!Seq)
	{
		// anim.<style>.<move> -> anim.<style>.stance_idle -> anim.street.stance_idle
		int32 Dot;
		if (Key.FindLastChar('.', Dot)) { UsedKey = Key.Left(Dot) + TEXT(".stance_idle"); Seq = FindClip(UsedKey); }
		if (!Seq) { UsedKey = TEXT("anim.street.stance_idle"); Seq = FindClip(UsedKey); }
		if (!Seq) return;
		bLoop = true;
	}
	USkeletalMeshComponent* M = GetMesh();
	if (UsedKey != CurrentKey || M->GetAnimationMode() != EAnimationMode::AnimationSingleNode)
	{
		M->SetAnimationMode(EAnimationMode::AnimationSingleNode);
		M->PlayAnimation(Seq, bLoop);
		CurrentKey = UsedKey;
	}
	if (UAnimSingleNodeInstance* Inst = M->GetSingleNodeInstance())
	{
		const float Len = Seq->GetPlayLength();
		const float T = bLoop && Len > 0 ? FMath::Fmod(TimeSeconds, Len) : FMath::Min(TimeSeconds, Len);
		Inst->SetPlaying(false);
		Inst->SetPosition(T, false);
	}
}

void AFFTSFighterCharacter::SetTint(const FLinearColor& Color)
{
	auto TintComponent = [&](UMeshComponent* C)
	{
		if (!C) return;
		for (int32 i = 0; i < C->GetNumMaterials(); ++i)
		{
			UMaterialInstanceDynamic* MID = C->CreateAndSetMaterialInstanceDynamic(i);
			if (!MID) continue;
			MID->SetVectorParameterValue(TEXT("Color"), Color);
			MID->SetVectorParameterValue(TEXT("BaseColor"), Color);
			MID->SetVectorParameterValue(TEXT("Tint"), Color);
		}
	};
	TintComponent(FallbackBody);
}

void AFFTSFighterCharacter::Flash()
{
	FlashTime = 0.12f;
	GetMesh()->SetScalarParameterValueOnMaterials(TEXT("Flash"), 1.f);
}

void AFFTSFighterCharacter::Tick(float DeltaSeconds)
{
	Super::Tick(DeltaSeconds);
	if (FlashTime > 0.f)
	{
		FlashTime -= DeltaSeconds;
		if (FlashTime <= 0.f) GetMesh()->SetScalarParameterValueOnMaterials(TEXT("Flash"), 0.f);
	}
}

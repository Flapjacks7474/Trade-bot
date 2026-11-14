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
#include "Misc/Paths.h"
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

static USkeletalMesh* LoadMesh(const FString& Path)
{
	const FString Name = FPaths::GetBaseFilename(Path);
	const FString Full = Path.Contains(TEXT(".")) ? Path : Path + TEXT(".") + Name;
	return LoadObject<USkeletalMesh>(nullptr, *Full, nullptr, LOAD_NoWarn | LOAD_Quiet);
}

void AFFTSFighterCharacter::BeginPlay()
{
	Super::BeginPlay();
	USkeletalMesh* Body = nullptr;
	if (bPreferManny)
	{
		// Manny only when the retargeted clips are there too (the street idle is the last-resort fallback clip).
		const bool bClips = LoadObject<UAnimSequence>(nullptr, *(MannyAnimFolder + TEXT("/anim_street_stance_idle.anim_street_stance_idle")), nullptr, LOAD_NoWarn | LOAD_Quiet) != nullptr;
		for (const FString& P : MannyMeshPaths)
			if (bClips && (Body = LoadMesh(P)) != nullptr) break;
		if (Body) { bIsManny = true; AnimFolder = MannyAnimFolder; }
	}
	if (!Body) Body = LoadMesh(SkeletalMeshPath);
	if (Body)
	{
		GetMesh()->SetSkeletalMesh(Body);
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
	TintColor = Color;
	BodyMaterials.Reset();
	// M_FFTSTint (made by Content/Python/ffts_import_fight_rig.py) has a "Color" parameter; the engine fallback does not.
	if (!TintBase) TintBase = LoadObject<UMaterialInterface>(nullptr, TEXT("/Game/FFTS/Materials/M_FFTSTint.M_FFTSTint"), nullptr, LOAD_NoWarn | LOAD_Quiet);
	if (!TintBase) TintBase = LoadObject<UMaterialInterface>(nullptr, TEXT("/Engine/BasicShapes/BasicShapeMaterial.BasicShapeMaterial"));
	UMeshComponent* Body = bHasRig ? static_cast<UMeshComponent*>(GetMesh()) : static_cast<UMeshComponent*>(FallbackBody);
	if (!Body) return;
	for (int32 i = 0; i < Body->GetNumMaterials(); ++i)
	{
		// Manny keeps its own materials when they expose a tint; anything else gets the flat FFTS tint material.
		UMaterialInterface* Parent = nullptr;
		if (bIsManny)
		{
			UMaterialInterface* Own = Body->GetMaterial(i);
			FLinearColor Unused;
			if (Own && (Own->GetVectorParameterValue(FHashedMaterialParameterInfo(TEXT("Tint")), Unused)
				|| Own->GetVectorParameterValue(FHashedMaterialParameterInfo(TEXT("BaseColor")), Unused)
				|| Own->GetVectorParameterValue(FHashedMaterialParameterInfo(TEXT("Color")), Unused)))
				Parent = Own;
		}
		if (!Parent) Parent = TintBase;
		if (!Parent) continue;
		UMaterialInstanceDynamic* MID = UMaterialInstanceDynamic::Create(Parent, this);
		Body->SetMaterial(i, MID);
		BodyMaterials.Add(MID);
	}
	ApplyBodyColor(Color);
}

void AFFTSFighterCharacter::ApplyBodyColor(const FLinearColor& Color)
{
	for (UMaterialInstanceDynamic* MID : BodyMaterials)
		if (MID)
		{
			MID->SetVectorParameterValue(TEXT("Color"), Color);
			MID->SetVectorParameterValue(TEXT("Tint"), Color);
			MID->SetVectorParameterValue(TEXT("BaseColor"), Color);
		}
}

void AFFTSFighterCharacter::Flash()
{
	FlashTime = 0.1f;
	ApplyBodyColor(FLinearColor(1.f, 0.95f, 0.85f) * 2.f);
}

void AFFTSFighterCharacter::Tick(float DeltaSeconds)
{
	Super::Tick(DeltaSeconds);
	if (FlashTime > 0.f)
	{
		FlashTime -= DeltaSeconds;
		if (FlashTime <= 0.f) ApplyBodyColor(TintColor);
	}
}

#include "FFTSGameMode.h"
#include "Components/DirectionalLightComponent.h"
#include "Components/SkyAtmosphereComponent.h"
#include "Components/SkyLightComponent.h"
#include "Components/StaticMeshComponent.h"
#include "Engine/DirectionalLight.h"
#include "Engine/SkyLight.h"
#include "Engine/StaticMesh.h"
#include "Engine/StaticMeshActor.h"
#include "Engine/World.h"
#include "EngineUtils.h"
#include "FFTSFightDirector.h"
#include "FFTSHUD.h"
#include "FFTSPlayerController.h"
#include "GameFramework/SpectatorPawn.h"
#include "Materials/MaterialInstanceDynamic.h"

AFFTSGameMode::AFFTSGameMode()
{
	PlayerControllerClass = AFFTSPlayerController::StaticClass();
	HUDClass = AFFTSHUD::StaticClass();
	DefaultPawnClass = ASpectatorPawn::StaticClass();
}

void AFFTSGameMode::StartPlay()
{
	EnsureArena();
	bool bHasDirector = false;
	for (TActorIterator<AFFTSFightDirector> It(GetWorld()); It; ++It) { bHasDirector = true; break; }
	if (!bHasDirector) GetWorld()->SpawnActor<AFFTSFightDirector>(FVector::ZeroVector, FRotator::ZeroRotator);
	Super::StartPlay();
}

void AFFTSGameMode::EnsureArena()
{
	UWorld* W = GetWorld();
	bool bHasLight = false, bHasFloor = false;
	for (TActorIterator<ADirectionalLight> It(W); It; ++It) { bHasLight = true; break; }
	for (TActorIterator<AStaticMeshActor> It(W); It; ++It) { bHasFloor = true; break; }
	FActorSpawnParameters P; P.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
	if (!bHasFloor)
	{
		if (UStaticMesh* Plane = LoadObject<UStaticMesh>(nullptr, TEXT("/Engine/BasicShapes/Plane.Plane")))
		{
			AStaticMeshActor* Floor = W->SpawnActor<AStaticMeshActor>(FVector::ZeroVector, FRotator::ZeroRotator, P);
			Floor->SetMobility(EComponentMobility::Movable);
			Floor->GetStaticMeshComponent()->SetStaticMesh(Plane);
			Floor->SetActorScale3D(FVector(40, 40, 1));
			if (UMaterialInterface* M = LoadObject<UMaterialInterface>(nullptr, TEXT("/Engine/BasicShapes/BasicShapeMaterial.BasicShapeMaterial")))
			{
				UMaterialInstanceDynamic* MID = UMaterialInstanceDynamic::Create(M, Floor);
				MID->SetVectorParameterValue(TEXT("Color"), FLinearColor(0.08f, 0.08f, 0.09f));
				Floor->GetStaticMeshComponent()->SetMaterial(0, MID);
			}
			// Low walls at the arena ends (the simulation's walls are at +-6 m).
			if (UStaticMesh* Cube = LoadObject<UStaticMesh>(nullptr, TEXT("/Engine/BasicShapes/Cube.Cube")))
				for (float Side : { -1.f, 1.f })
				{
					AStaticMeshActor* Wall = W->SpawnActor<AStaticMeshActor>(FVector(Side * 650.f, 0, 150.f), FRotator::ZeroRotator, P);
					Wall->SetMobility(EComponentMobility::Movable);
					Wall->GetStaticMeshComponent()->SetStaticMesh(Cube);
					Wall->SetActorScale3D(FVector(0.5f, 8.f, 3.f));
				}
		}
	}
	if (!bHasLight)
	{
		ADirectionalLight* Sun = W->SpawnActor<ADirectionalLight>(FVector(0, 0, 800), FRotator(-40.f, 35.f, 0), P);
		if (UDirectionalLightComponent* SunLight = Cast<UDirectionalLightComponent>(Sun->GetLightComponent()))
		{
			SunLight->SetMobility(EComponentMobility::Movable);
			SunLight->SetIntensity(6.f);
			SunLight->SetAtmosphereSunLight(true);
		}
		ASkyLight* Sky = W->SpawnActor<ASkyLight>(FVector(0, 0, 500), FRotator::ZeroRotator, P);
		Sky->GetLightComponent()->SetMobility(EComponentMobility::Movable);
		Sky->GetLightComponent()->bRealTimeCapture = true;
		Sky->GetLightComponent()->SetIntensity(1.f);
		AActor* Atmos = W->SpawnActor<AActor>(FVector::ZeroVector, FRotator::ZeroRotator, P);
		USkyAtmosphereComponent* SkyAtmo = NewObject<USkyAtmosphereComponent>(Atmos);
		Atmos->SetRootComponent(SkyAtmo);
		SkyAtmo->RegisterComponent();
		Sky->GetLightComponent()->RecaptureSky();
	}
}

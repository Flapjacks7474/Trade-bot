#include "FFTSFightDirector.h"
#include "Camera/CameraActor.h"
#include "Camera/CameraComponent.h"
#include "Engine/World.h"
#include "FFTSFighterCharacter.h"
#include "FFTSGameSubsystem.h"
#include "Kismet/GameplayStatics.h"
#include "GameFramework/PlayerController.h"
#include "GameFramework/CharacterMovementComponent.h"

using namespace ffts;

static const TCHAR* Opponents[] = { TEXT("fighter.dre"), TEXT("fighter.rell"), TEXT("fighter.brick") };
static constexpr float TickSeconds = 1.f / 60.f;
static constexpr float MetersToCm = 100.f;

AFFTSFightDirector::AFFTSFightDirector()
{
	PrimaryActorTick.bCanEverTick = true;
	PrimaryActorTick.TickGroup = TG_PrePhysics;
}

UFFTSGameSubsystem* AFFTSFightDirector::Game() const
{
	UGameInstance* GI = GetGameInstance();
	return GI ? GI->GetSubsystem<UFFTSGameSubsystem>() : nullptr;
}

void AFFTSFightDirector::BeginPlay()
{
	Super::BeginPlay();
	if (UFFTSGameSubsystem* G = Game()) G->UnlockAllStylesForDojo();
	PlayerBody = SpawnFighter(FLinearColor(0.10f, 0.28f, 0.70f));   // player: blue
	OpponentBody = SpawnFighter(FLinearColor(0.60f, 0.10f, 0.08f));   // opponent: red

	FActorSpawnParameters P; P.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
	FightCamera = GetWorld()->SpawnActor<ACameraActor>(FightOrigin + FVector(0, 520, 60), FRotator(-6, -90, 0), P);
	if (FightCamera) FightCamera->GetCameraComponent()->SetFieldOfView(55.f);
	if (APlayerController* PC = UGameplayStatics::GetPlayerController(this, 0)) PC->SetViewTargetWithBlend(FightCamera, 0.f);
	StartFight();
}

AFFTSFighterCharacter* AFFTSFightDirector::SpawnFighter(const FLinearColor& Tint)
{
	FActorSpawnParameters P; P.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
	AFFTSFighterCharacter* C = GetWorld()->SpawnActor<AFFTSFighterCharacter>(AFFTSFighterCharacter::StaticClass(), FightOrigin, FRotator::ZeroRotator, P);
	if (C)
	{
		C->SetTint(Tint);
		C->GetCharacterMovement()->SetMovementMode(MOVE_None);   // the simulation owns position during fights
		C->SetActorEnableCollision(false);
	}
	return C;
}

void AFFTSFightDirector::StartFight()
{
	UFFTSGameSubsystem* G = Game();
	if (!G || !G->IsContentValid()) { AddFeed(TEXT("Content failed to load - see the Output Log (LogFFTS).")); return; }
	CombatDatabase& Db = G->Database();
	const FighterDef* Pd = Db.fighter("fighter.player");
	const FighterDef* Od = Db.fighter(UFFTSGameSubsystem::ToStd(Opponents[OpponentIndex % 3]));
	if (!Pd || !Od) { AddFeed(TEXT("Missing fighter data.")); return; }
	PlayerDef = *Pd; OpponentDef = *Od;

	TArray<FString> Base = G->BaseStyleIds();
	PlayerPrimary = FMath::Clamp(PlayerPrimary, 0, Base.Num() - 1);
	StyleLoadout& L = G->Styles();
	const FString Second = PlayerSecondary >= 0 && Base.IsValidIndex(PlayerSecondary) && PlayerSecondary != PlayerPrimary ? Base[PlayerSecondary] : FString();
	L.equip(UFFTSGameSubsystem::ToStd(Base[PlayerPrimary]), UFFTSGameSubsystem::ToStd(Second));
	std::shared_ptr<Moveset> Pm = L.buildMoveset();
	std::vector<std::string> OppStyles = OpponentDef.styles;
	if (OpponentStyle >= 0 && Base.IsValidIndex(OpponentStyle)) OppStyles = { UFFTSGameSubsystem::ToStd(Base[OpponentStyle]) };
	std::shared_ptr<Moveset> Om = Db.buildMoveset(OppStyles);
	if (!Pm || !Om) { AddFeed(TEXT("Could not build movesets.")); return; }

	++FightCount;
	Simulation = MakeUnique<CombatSimulation>(CombatConfig(), std::make_unique<Fighter>(PlayerDef, Pm, -1.1f),
		std::make_unique<Fighter>(OpponentDef, Om, 1.1f), "fight.ue." + std::to_string(FightCount));
	Ai = MakeUnique<FighterAI>(*Simulation->B, OpponentDef.aiStyle, OpponentDef.aiDifficulty, 0x5EEDULL + FightCount * 7919ULL);
	Accumulator = 0.f; Latched = FFFTSFightButtons();
	EventFeed.Reset();
	AddFeed(FString::Printf(TEXT("FIGHT! You (%s) vs %s (%s)"), *PlayerStyleLabel(), *OpponentName(), *OpponentStyleLabel()));
}

void AFFTSFightDirector::NextOpponent() { OpponentIndex = (OpponentIndex + 1) % 3; OpponentStyle = -1; StartFight(); }

void AFFTSFightDirector::CyclePlayerStyle(bool bSecondary)
{
	UFFTSGameSubsystem* G = Game(); if (!G) return;
	const int32 N = G->BaseStyleIds().Num(); if (N == 0) return;
	if (bSecondary) { PlayerSecondary = PlayerSecondary + 1 >= N ? -1 : PlayerSecondary + 1; if (PlayerSecondary == PlayerPrimary) PlayerSecondary = PlayerSecondary + 1 >= N ? -1 : PlayerSecondary + 1; }
	else PlayerPrimary = (PlayerPrimary + 1) % N;
	StartFight();
}

void AFFTSFightDirector::CycleOpponentStyle()
{
	UFFTSGameSubsystem* G = Game(); if (!G) return;
	const int32 N = G->BaseStyleIds().Num();
	OpponentStyle = OpponentStyle + 1 >= N ? -1 : OpponentStyle + 1;
	StartFight();
}

void AFFTSFightDirector::SetButtons(const FFFTSFightButtons& B)
{
	if (B.Pressed != Command::None) Latched.Pressed = B.Pressed;   // keep a press until a fixed step consumes it
	Latched.ScreenMove = B.ScreenMove;
	Latched.bBlock = B.bBlock;
}

void AFFTSFightDirector::Tick(float DeltaSeconds)
{
	Super::Tick(DeltaSeconds);
	if (!Simulation) return;
	Accumulator += FMath::Min(DeltaSeconds, 0.25f);
	while (Accumulator >= TickSeconds)
	{
		Accumulator -= TickSeconds;
		if (!Simulation->isOver()) StepOnce();
	}
	Present(DeltaSeconds);
}

void AFFTSFightDirector::StepOnce()
{
	Fighter& Me = *Simulation->A;
	FighterInput In;
	In.command = Latched.Pressed; Latched.Pressed = Command::None;
	In.block = Latched.bBlock;
	In.move = Latched.ScreenMove * Me.facing;   // screen right is +X; the simulation's move is "toward the opponent"
	const FighterInput Opp = Ai->think(*Simulation);
	Simulation->step(In, Opp);
	HandleEvents();
}

void AFFTSFightDirector::HandleEvents()
{
	for (const CombatEvent& E : Simulation->events)
	{
		const bool ByPlayer = E.actor == Simulation->A->id;
		const FString Who = ByPlayer ? TEXT("You") : OpponentName();
		const FString Move = UFFTSGameSubsystem::FromStd(E.moveId);
		auto MoveName = [&]() -> FString
		{
			for (const auto* F : { Simulation->A.get(), Simulation->B.get() })
				if (const MoveDef* M = F->moves->get(E.moveId)) return UFFTSGameSubsystem::FromStd(M->displayName);
			return Move;
		};
		if (E.type == "Hit")
		{
			AddFeed(FString::Printf(TEXT("%s: %s  %.0f dmg"), *Who, *MoveName(), E.amount));
			AFFTSFighterCharacter* Victim = ByPlayer ? OpponentBody : PlayerBody;
			if (Victim) Victim->Flash();
		}
		else if (E.type == "Countered") AddFeed(FString::Printf(TEXT("%s countered (%s)!"), *Who, *UFFTSGameSubsystem::FromStd(E.detail)));
		else if (E.type == "Blocked") AddFeed(FString::Printf(TEXT("%s's %s blocked"), *Who, *MoveName()));
		else if (E.type == "GuardBreak") AddFeed(TEXT("GUARD BREAK!"));
		else if (E.type == "Thrown") AddFeed(FString::Printf(TEXT("%s: %s%s"), *Who, *MoveName(), E.detail == "environment" ? TEXT(" into the wall!") : TEXT("")));
		else if (E.type == "ClinchStrike") AddFeed(FString::Printf(TEXT("%s: %s"), *Who, *MoveName()));
		else if (E.type == "Reversal") AddFeed(FString::Printf(TEXT("%s reversed it!"), *Who));
		else if (E.type == "FinisherStarted") AddFeed(FString::Printf(TEXT("%s: FINISHER - %s"), *Who, *MoveName()));
		else if (E.type == "Defeated") AddFeed(FString::Printf(TEXT("%s WINS (%s)"), ByPlayer ? TEXT("YOU") : *OpponentName(), *UFFTSGameSubsystem::FromStd(E.detail)));
		else if (E.type == "TimeUp") AddFeed(TEXT("TIME!"));
	}
}

void AFFTSFightDirector::Present(float DeltaSeconds)
{
	PresentFighter(PlayerBody, *Simulation->A);
	PresentFighter(OpponentBody, *Simulation->B);
	if (FightCamera)
	{
		const float Mid = (Simulation->A->x + Simulation->B->x) * 0.5f * MetersToCm;
		const float Gap = Simulation->distance() * MetersToCm;
		const FVector Target = FightOrigin + FVector(Mid, 380.f + Gap * 0.45f, 55.f);
		FightCamera->SetActorLocation(FMath::VInterpTo(FightCamera->GetActorLocation(), Target, DeltaSeconds, 6.f));
	}
}

void AFFTSFightDirector::PresentFighter(AFFTSFighterCharacter* Body, const Fighter& F)
{
	if (!Body) return;
	Body->SetActorLocation(FightOrigin + FVector(F.x * MetersToCm, 0, 0));
	Body->SetActorRotation(FRotator(0, F.facing > 0 ? 0.f : 180.f, 0));
	FString Key; float Time = 0; bool bLoop = false;
	ChooseClip(F, Key, Time, bLoop);
	Body->ShowClip(Key, Time, bLoop);
}

void AFFTSFightDirector::ChooseClip(const Fighter& F, FString& Key, float& Time, bool& bLoop) const
{
	const FString Idle = UFFTSGameSubsystem::FromStd(F.stance().idleAnimation.empty() ? std::string("anim.street.stance_idle") : F.stance().idleAnimation);
	const FString StylePrefix = Idle.LeftChop(FString(TEXT("stance_idle")).Len());
	bLoop = false;
	Time = F.stateTicks * TickSeconds;
	switch (F.state)
	{
	case State::Attacking: case State::Combo: case State::Throwing: case State::Finisher:
	{
		if (!F.move) { Key = Idle; bLoop = true; return; }
		Key = UFFTSGameSubsystem::FromStd(F.move->animationKey);
		// The fighter's speed stat changes startup; rescale so the authored impact frame lands on the hit tick.
		const float Authored = (float)F.move->startup;
		const float Scale = F.moveStartup > 0 ? Authored / F.moveStartup : 1.f;
		const float T = F.moveTick <= F.moveStartup ? F.moveTick * Scale : Authored + (F.moveTick - F.moveStartup);
		Time = T * TickSeconds;
		return;
	}
	case State::Blocking: Key = StylePrefix + TEXT("stance_guard"); Time = FMath::Min(F.stateTicks, 20) * TickSeconds; return;
	case State::BlockStun: Key = TEXT("anim.react.block_impact"); return;
	case State::HitStun: Key = F.stateDuration > 22 ? TEXT("anim.react.hit_head_heavy") : TEXT("anim.react.hit_head_light"); return;
	case State::Stunned: case State::FinisherVictim: Key = TEXT("anim.react.stunned"); bLoop = true; return;
	case State::KnockedDown: Key = TEXT("anim.react.knockdown"); return;
	case State::GettingUp: Key = TEXT("anim.react.get_up"); return;
	case State::Dodging: Key = (F.stance().defense == "Slip and roll" || F.stance().defense == "Shoulder roll") ? TEXT("anim.react.slip") : TEXT("anim.react.dodge_back"); return;
	case State::Grappled: Key = TEXT("anim.react.grappled"); bLoop = true; return;
	case State::Grappling: Key = StylePrefix + TEXT("stance_guard"); Time = 0.3f; return;
	case State::Defeated: Key = TEXT("anim.react.defeated"); return;
	case State::Moving: Key = StylePrefix + TEXT("shuffle_forward"); bLoop = true; return;
	default: Key = Idle; bLoop = true; return;
	}
}

void AFFTSFightDirector::AddFeed(const FString& Line)
{
	EventFeed.Add(Line);
	while (EventFeed.Num() > 7) EventFeed.RemoveAt(0);
}

static FString LoadoutLabel(const Moveset& M, const CombatDatabase& Db, const std::vector<std::string>& Styles)
{
	FString S;
	for (size_t i = 0; i < Styles.size(); ++i)
	{
		const StyleDef* D = Db.style(Styles[i]);
		if (i) S += TEXT(" + ");
		S += D ? UFFTSGameSubsystem::FromStd(D->displayName) : UFFTSGameSubsystem::FromStd(Styles[i]);
	}
	if (!M.hybridId().empty())
		if (const StyleDef* H = Db.style(M.hybridId())) S += FString::Printf(TEXT("  [HYBRID: %s]"), *UFFTSGameSubsystem::FromStd(H->displayName));
	return S;
}

FString AFFTSFightDirector::PlayerStyleLabel() const
{
	UFFTSGameSubsystem* G = Game();
	if (!G || !Simulation) return FString();
	return LoadoutLabel(*Simulation->A->moves, G->Database(), G->Styles().equipped);
}

FString AFFTSFightDirector::OpponentStyleLabel() const
{
	UFFTSGameSubsystem* G = Game();
	if (!G || !Simulation) return FString();
	TArray<FString> Base = G->BaseStyleIds();
	std::vector<std::string> S = OpponentDef.styles;
	if (OpponentStyle >= 0 && Base.IsValidIndex(OpponentStyle)) S = { UFFTSGameSubsystem::ToStd(Base[OpponentStyle]) };
	return LoadoutLabel(*Simulation->B->moves, G->Database(), S);
}

FString AFFTSFightDirector::OpponentName() const { return UFFTSGameSubsystem::FromStd(OpponentDef.displayName); }

FString AFFTSFightDirector::ContentStatus() const
{
	UFFTSGameSubsystem* G = Game();
	if (!G) return TEXT("No game subsystem");
	if (!G->IsContentValid()) return FString::Printf(TEXT("CONTENT ERRORS: %d (see Output Log)"), G->GetContentErrors().Num());
	return FString();
}

bool AFFTSFightDirector::HasRig() const { return PlayerBody && PlayerBody->HasRig(); }
